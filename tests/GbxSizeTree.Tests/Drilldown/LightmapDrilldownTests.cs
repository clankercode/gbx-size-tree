using System.Buffers.Binary;
using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.LZO;
using GBX.NET.ZLib;
using GbxSizeTree.Drilldown;
using GbxSizeTree.Model;
using GbxSizeTree.Tests.Fixtures;

namespace GbxSizeTree.Tests.Drilldown;

public sealed class LightmapDrilldownTests
{
    private const uint LightmapChunkId = 0x0304305B;
    private static readonly Lazy<(
        CGameCtnChallenge Map,
        byte[] DecompressedFile,
        RawChunkRegion Region,
        LightmapInfo Info)> Sample = new(LoadSample);

    [Fact]
    public void Inspect_SampleLightmap_ReportsVersionFramesAndThreeBuffersPerFrame()
    {
        var sample = InspectSample();

        Assert.True(sample.Info.HasLightmaps);
        Assert.Equal(10, sample.Info.Version);
        Assert.Equal(3, sample.Info.FrameCount);
        Assert.Equal(3, sample.Info.Frames.Count);
        Assert.All(sample.Info.Frames, frame => Assert.Equal(3, frame.BlobBytes.Count));
    }

    [Fact]
    public void Inspect_SampleLightmap_ReportsRawAndParsedZlibSizes()
    {
        var sample = InspectSample();

        Assert.Equal(1_570_755, sample.Info.ZlibCompressedBytes);
        Assert.Equal(1_838_848, sample.Info.ZlibUncompressedBytes);

        Assert.NotNull(sample.Map.LightmapCacheData);
        var cacheData = sample.Map.LightmapCacheData;
        Assert.Equal(sample.Info.ZlibCompressedBytes, cacheData.Data.Length);
        Assert.Equal(sample.Info.ZlibUncompressedBytes, cacheData.UncompressedSize);
    }

    [Fact]
    public void Inspect_SampleLightmap_AccountsForEntirePayload()
    {
        var sample = InspectSample();
        var accountedBytes =
            16L +
            sizeof(int) +
            sizeof(int) +
            sample.Info.Frames.Sum(frame => frame.BlobBytes.Sum(length => sizeof(int) + length)) +
            (2L * sizeof(int)) +
            sample.Info.ZlibCompressedBytes;
        var remainder = sample.Region.PayloadLength - accountedBytes;

        if (remainder != 0)
        {
            var message = DescribeRemainder(
                sample.DecompressedFile,
                sample.Region,
                accountedBytes,
                remainder);
            Assert.True(remainder is > 0 and < 64, message);
            Assert.Fail(message);
        }

        Assert.Equal(sample.Region.PayloadLength, accountedBytes);
    }

    [Fact]
    public void Inspect_NullRegion_ReturnsNull()
    {
        var result = new LightmapDrilldown().Inspect(
            ReadOnlyMemory<byte>.Empty,
            null,
            new CGameCtnChallenge());

        Assert.Null(result);
    }

    private static (
        CGameCtnChallenge Map,
        byte[] DecompressedFile,
        RawChunkRegion Region,
        LightmapInfo Info) InspectSample()
    {
        SampleMap.SkipUnlessAvailable();
        return Sample.Value;
    }

    private static (
        CGameCtnChallenge Map,
        byte[] DecompressedFile,
        RawChunkRegion Region,
        LightmapInfo Info) LoadSample()
    {
        Gbx.LZO = new Lzo();
        Gbx.ZLib = new ZLib();

        var map = Gbx.Parse<CGameCtnChallenge>(SampleMap.Path).Node;
        using var decompressed = new MemoryStream();
        Gbx.Decompress(SampleMap.Path, decompressed);
        var decompressedFile = decompressed.ToArray();
        var region = FindSkippableRegion(decompressedFile, LightmapChunkId);
        Assert.NotNull(region);
        var info = new LightmapDrilldown().Inspect(decompressedFile, region, map);
        Assert.NotNull(info);

        return (map, decompressedFile, region, info);
    }

    // Minimal local id + SKIP scan; tests intentionally do not depend on the production scanner.
    private static RawChunkRegion? FindSkippableRegion(ReadOnlySpan<byte> bytes, uint chunkId)
    {
        const uint skipMarker = 0x534B4950;

        for (var offset = 0; offset <= bytes.Length - 12; offset++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]) != chunkId ||
                BinaryPrimitives.ReadUInt32LittleEndian(bytes[(offset + 4)..]) != skipMarker)
            {
                continue;
            }

            var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(bytes[(offset + 8)..]);
            if (payloadLength < 0 || payloadLength > bytes.Length - offset - 12)
            {
                continue;
            }

            return new RawChunkRegion(
                chunkId,
                RawChunkKind.Skippable,
                offset,
                payloadLength + 12L,
                offset + 12L,
                payloadLength,
                null);
        }

        return null;
    }

    private static string DescribeRemainder(
        byte[] decompressedFile,
        RawChunkRegion region,
        long accountedBytes,
        long remainder)
    {
        var payloadEnd = checked((int)(region.PayloadOffset + region.PayloadLength));
        var boundary = checked((int)(region.PayloadOffset + Math.Clamp(
            accountedBytes,
            0,
            region.PayloadLength)));
        var contextStart = Math.Max(0, boundary - 16);
        var contextEnd = Math.Min(decompressedFile.Length, payloadEnd + 16);
        var context = Convert.ToHexString(
            decompressedFile.AsSpan(contextStart, contextEnd - contextStart));

        return $"Lightmap accounting left an exact remainder of {remainder} byte(s): " +
               $"accounted={accountedBytes}, payload={region.PayloadLength}; " +
               $"surrounding bytes [{contextStart}..{contextEnd})={context}.";
    }
}
