using System.Buffers.Binary;
using GbxSizeTree.Container;
using GbxSizeTree.Model;
using GbxSizeTree.Tests.Fixtures;

namespace GbxSizeTree.Tests.Container;

public class SkippableChunkScannerTests
{
    [Fact]
    public void Scan_AnchoredChunksAndGap_ProduceExactCompleteRegions()
    {
        var body = new List<byte>();
        AppendSkippable(body, 0x03043040, EncapsulatedPayload(7, 23));

        var payloadWithFakeFrame = new List<byte>(EncapsulatedPayload(3, 19));
        AppendSkippable(payloadWithFakeFrame, 0x03043068, [0x11, 0x22, 0x33, 0x44]);
        AppendSkippable(body, 0x03043044, [.. payloadWithFakeFrame]);

        var gapOffset = body.Count;
        AppendUInt32(body, 0x0304301F);
        body.AddRange([0x10, 0x20, 0x30, 0x40, 0x50, 0x60]);

        // This plausible frame must not resynchronize because it only chains to an
        // implausible-id fake frame. The real C frame below chains to the terminator.
        AppendSkippable(body, 0x03043062, []);
        AppendSkippable(body, 0x99000001, []);
        body.Add(0xAA);
        var gapLength = body.Count - gapOffset;

        AppendSkippable(body, 0x03043054, EncapsulatedPayload(1, 31));
        AppendUInt32(body, 0xFACADE01);

        var scan = new SkippableChunkScanner().Scan(body.ToArray());

        AssertCompleteCoverage(scan, body.Count);
        Assert.True(scan.Resynchronized);
        Assert.Equal(gapLength, scan.UnattributedBytes);
        Assert.Equal(5, scan.Regions.Count);

        Assert.Collection(
            scan.Regions,
            region => AssertRegion(region, 0x03043040, RawChunkKind.Skippable),
            region => AssertRegion(region, 0x03043044, RawChunkKind.Skippable),
            region =>
            {
                AssertRegion(region, 0x0304301F, RawChunkKind.NonSkippable);
                Assert.Equal(gapOffset, region.Offset);
                Assert.Equal(gapLength, region.Length);
            },
            region => AssertRegion(region, 0x03043054, RawChunkKind.Skippable),
            region => AssertRegion(region, 0xFACADE01, RawChunkKind.Terminator));

        Assert.DoesNotContain(scan.Regions, region => region.ChunkId == 0x03043068);
        Assert.DoesNotContain(scan.Regions, region => region.ChunkId == 0x03043062);
        Assert.DoesNotContain(scan.Regions, region => region.ChunkId == 0x99000001);
        Assert.Equal(23, scan.Regions[0].EncapsulatedInnerSize);
        Assert.Equal(19, scan.Regions[1].EncapsulatedInnerSize);
        Assert.Equal(31, scan.Regions[3].EncapsulatedInnerSize);
    }

    [Fact]
    public void Scan_MalformedEncapsulation_DegradesWithWarnings()
    {
        byte[][] malformedPayloads =
        [
            [0, 0, 0, 0, 0, 0, 0, 0],
            EncapsulationHeader(version: 1, zeroMarker: 7, innerSize: 0),
            EncapsulationHeader(version: 1, zeroMarker: 0, innerSize: -1),
            EncapsulationHeader(version: 1, zeroMarker: 0, innerSize: 1),
        ];

        foreach (var payload in malformedPayloads)
        {
            var body = new List<byte>();
            AppendSkippable(body, 0x03043044, payload);

            var scan = new SkippableChunkScanner().Scan(body.ToArray());

            var region = Assert.Single(scan.Regions);
            Assert.Null(region.EncapsulatedInnerSize);
            Assert.Contains(scan.Warnings, warning => warning.Code == "raw-body-invalid-encapsulation");
            AssertCompleteCoverage(scan, body.Count);
        }
    }

    [Fact]
    public void Scan_MalformedBytes_DegradesToCompleteNonSkippableCoverage()
    {
        var body = new byte[]
        {
            0x40, 0x30, 0x04, 0x03,
            0x50, 0x49, 0x4B, 0x53,
            0xFF, 0xFF, 0xFF, 0xFF,
            0xAA,
        };

        var scan = new SkippableChunkScanner().Scan(body);

        AssertCompleteCoverage(scan, body.Length);
        var region = Assert.Single(scan.Regions);
        Assert.Equal(RawChunkKind.NonSkippable, region.Kind);
        Assert.Equal(body.Length, region.Length);
        Assert.Equal(body.Length, scan.UnattributedBytes);
        Assert.NotEmpty(scan.Warnings);
    }

    [Fact]
    public void Scan_Sample_FindsExpectedMapChunksAndCoversWholeBody()
    {
        SampleMap.SkipUnlessAvailable();
        var body = DecompressedBody.GetBody(SampleMap.Path);

        var scan = new SkippableChunkScanner().Scan(body);

        Assert.Equal(SampleMap.BodyUncompressed, body.Length);
        AssertCompleteCoverage(scan, SampleMap.BodyUncompressed);
        Assert.True(scan.Resynchronized);
        Assert.Contains(scan.Regions, region =>
            region.Kind == RawChunkKind.NonSkippable && region.ChunkId == 0x0304301F);

        uint[] requiredSkippableIds =
        [
            0x03043040,
            0x03043044,
            0x03043048,
            0x03043054,
            0x0304305B,
            0x03043062,
            0x03043068,
            0x03043069,
        ];
        foreach (var chunkId in requiredSkippableIds)
        {
            Assert.Contains(scan.Regions, region =>
                region.Kind == RawChunkKind.Skippable && region.ChunkId == chunkId);
        }

        var optionalZoneGenealogies = scan.Regions.FirstOrDefault(region => region.ChunkId == 0x03043043);
        if (optionalZoneGenealogies is not null)
        {
            Assert.Equal(RawChunkKind.Skippable, optionalZoneGenealogies.Kind);
        }

        Assert.Contains(scan.Regions, region =>
            region.Kind == RawChunkKind.Skippable
            && region.ChunkId == 0x03043054
            && region.PayloadLength >= 1_216_568);
        Assert.Contains(scan.Regions, region =>
            region.Kind == RawChunkKind.Skippable
            && region.ChunkId == 0x0304305B
            && region.Length > 1_570_755);
    }

    private static void AssertCompleteCoverage(RawBodyScan scan, long bodyLength)
    {
        Assert.Equal(bodyLength, scan.ScannedBytes);
        Assert.Equal(bodyLength, scan.Regions.Sum(region => region.Length));

        long expectedOffset = 0;
        foreach (var region in scan.Regions)
        {
            Assert.Equal(expectedOffset, region.Offset);
            Assert.True(region.Length > 0);
            Assert.InRange(region.Offset + region.Length, 0, bodyLength);
            Assert.InRange(region.PayloadOffset, region.Offset, region.Offset + region.Length);
            Assert.InRange(region.PayloadOffset + region.PayloadLength, region.PayloadOffset, region.Offset + region.Length);
            expectedOffset += region.Length;
        }

        Assert.Equal(bodyLength, expectedOffset);
    }

    private static void AssertRegion(RawChunkRegion region, uint chunkId, RawChunkKind kind)
    {
        Assert.Equal(chunkId, region.ChunkId);
        Assert.Equal(kind, region.Kind);
    }

    private static byte[] EncapsulatedPayload(int version, int innerSize)
    {
        var payload = new List<byte>(EncapsulationHeader(version, zeroMarker: 0, innerSize));
        payload.AddRange(new byte[innerSize]);
        return [.. payload];
    }

    private static byte[] EncapsulationHeader(int version, int zeroMarker, int innerSize)
    {
        var payload = new List<byte>();
        AppendInt32(payload, version);
        AppendInt32(payload, zeroMarker);
        AppendInt32(payload, innerSize);
        return [.. payload];
    }

    private static void AppendSkippable(List<byte> bytes, uint chunkId, byte[] payload)
    {
        AppendUInt32(bytes, chunkId);
        AppendUInt32(bytes, 0x534B4950);
        AppendInt32(bytes, payload.Length);
        bytes.AddRange(payload);
    }

    private static void AppendUInt32(List<byte> bytes, uint value)
    {
        Span<byte> encoded = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(encoded, value);
        bytes.AddRange(encoded);
    }

    private static void AppendInt32(List<byte> bytes, int value)
    {
        Span<byte> encoded = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(encoded, value);
        bytes.AddRange(encoded);
    }
}
