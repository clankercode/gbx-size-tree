using System.Buffers.Binary;
using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.LZO;
using GBX.NET.ZLib;
using GbxSizeTree.Drilldown;
using GbxSizeTree.Model;
using GbxSizeTree.Tests.Fixtures;

namespace GbxSizeTree.Tests.Drilldown;

public class MiscDrilldownTests
{
    private static readonly Lazy<(CGameCtnChallenge Map, byte[] Bytes)> Sample =
        new(LoadSampleDecompressed);

    [Fact]
    public void ScriptMetadata_WithNullRegion_ReturnsNull()
    {
        var result = new ScriptMetadataDrilldown().Inspect(
            ReadOnlyMemory<byte>.Empty,
            null,
            new CGameCtnChallenge());

        Assert.Null(result);
    }

    [Fact]
    public void MediaTracker_EmptyMapAndZeroChunkBytes_ReturnsNull()
    {
        var result = new MediaTrackerDrilldown().Inspect(new CGameCtnChallenge(), 0);

        Assert.Null(result);
    }

    [Fact]
    public void MediaTracker_EmptyMapAndPositiveChunkBytes_ReturnsEmptyInfo()
    {
        var result = new MediaTrackerDrilldown().Inspect(new CGameCtnChallenge(), 100);

        Assert.NotNull(result);
        Assert.Equal(100, result.ChunkBytes);
        Assert.Equal(0, result.ClipCount);
        Assert.Null(result.Summary);
    }

    [Fact]
    public void MediaTracker_ClipAndZeroChunkBytes_ReturnsClipInfo()
    {
        var map = new CGameCtnChallenge
        {
            ClipIntro = new CGameCtnMediaClip(),
        };

        var result = new MediaTrackerDrilldown().Inspect(map, 0);

        Assert.NotNull(result);
        Assert.Equal(0, result.ChunkBytes);
        Assert.Equal(1, result.ClipCount);
        Assert.Equal("intro", result.Summary);
    }

    [Fact]
    public void SampleScriptMetadataRegion_ReturnsBoundedParsedInfo()
    {
        SampleMap.SkipUnlessAvailable();
        var (map, bytes) = Sample.Value;
        var region = FindSkippableRegion(bytes, 0x03043044);

        var result = new ScriptMetadataDrilldown().Inspect(bytes, region, map);

        Assert.NotNull(region);
        Assert.NotNull(result);
        Assert.Equal(region.Length, result.ChunkBytes);
        Assert.True(result.ChunkBytes > 0);
        Assert.True(result.TraitNames.Count <= 20);

        var traits = map.ScriptMetadata?.Traits;
        if (traits is null)
        {
            Assert.Null(result.EntryCount);
            Assert.Empty(result.TraitNames);
        }
        else
        {
            Assert.Equal(traits.Count, result.EntryCount);
            Assert.Equal(traits.Keys.Take(20), result.TraitNames);
        }
    }

    [Fact]
    public void SampleMediaTracker_ReturnsConsistentClipCount()
    {
        SampleMap.SkipUnlessAvailable();
        var (map, _) = Sample.Value;

        var result = new MediaTrackerDrilldown().Inspect(map, 1_000);
        var expectedClipCount =
            (map.ClipIntro is null ? 0 : 1) +
            (map.ClipPodium is null ? 0 : 1) +
            (map.ClipGroupInGame?.Clips?.Count ?? 0) +
            (map.ClipGroupEndRace?.Clips?.Count ?? 0) +
            (map.ClipAmbiance is null ? 0 : 1);

        Assert.NotNull(result);
        Assert.True(result.ClipCount >= 0);
        Assert.Equal(expectedClipCount, result.ClipCount);
        Assert.Equal(ExpectedSummary(map), result.Summary);
    }

    private static string? ExpectedSummary(CGameCtnChallenge map)
    {
        var parts = new List<string>();

        if (map.ClipIntro is not null)
        {
            parts.Add("intro");
        }

        if (map.ClipPodium is not null)
        {
            parts.Add("podium");
        }

        AddExpectedGroup(map.ClipGroupInGame?.Clips?.Count ?? 0, "in-game", parts);
        AddExpectedGroup(map.ClipGroupEndRace?.Clips?.Count ?? 0, "end-race", parts);

        if (map.ClipAmbiance is not null)
        {
            parts.Add("ambiance");
        }

        return parts.Count == 0 ? null : string.Join(" + ", parts);
    }

    private static void AddExpectedGroup(int count, string name, List<string> parts)
    {
        if (count > 0)
        {
            parts.Add($"{count} {name} clip{(count == 1 ? string.Empty : "s")}");
        }
    }

    private static (CGameCtnChallenge Map, byte[] Bytes) LoadSampleDecompressed()
    {
        Gbx.LZO = new Lzo();
        Gbx.ZLib = new ZLib();

        var gbx = Gbx.Parse<CGameCtnChallenge>(SampleMap.Path);
        using var input = File.OpenRead(SampleMap.Path);
        using var output = new MemoryStream();
        Gbx.Decompress(input, output);
        return (gbx.Node, output.ToArray());
    }

    // Skippable chunk framing and ids are documented in docs/FORMAT-NOTES.md.
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
}
