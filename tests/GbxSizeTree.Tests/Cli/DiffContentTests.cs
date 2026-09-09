using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.Game;
using GbxSizeTree.Cli.Modes;
using GbxSizeTree.Container;
using GbxSizeTree.Tests.Fixtures;

namespace GbxSizeTree.Tests.Cli;

public sealed class DiffContentTests
{
    [Theory]
    [InlineData(0x0304305Bu)]
    [InlineData(0x03043043u)]
    [InlineData(0x03043049u)]
    [InlineData(0x03043FFFu)]
    public void EqualLengthPayloadChanges_HaveLegacyKeyAndDigest(uint id)
    {
        var changes = MapContentComparison.Compare(Container(Skip(id, [1, 2, 3])), Container(Skip(id, [1, 9, 3])));
        var chunk = Assert.Single(changes, c => c.Key == $"body:{id:X8}");
        Assert.Contains("sha256=", chunk.Left!);
        Assert.NotEqual(chunk.Left, chunk.Right);
        Assert.Contains(changes, c => c.Key == "content:decompressed-body");
    }

    [Fact]
    public void RepeatedBodyIds_DoNotOverwriteEarlierPayloadsOrRemovals()
    {
        var first = Skip(0x0304305B, [1]);
        var second = Skip(0x0304305B, [2]);
        var changed = MapContentComparison.Compare(Container([.. first, .. second]), Container([.. Skip(0x0304305B, [9]), .. second]));
        Assert.Contains(changed, c => c.Key == "body:0304305B");
        Assert.DoesNotContain(changed, c => c.Key == "body:0304305B#2");
        var removed = MapContentComparison.Compare(Container([.. first, .. second]), Container(first));
        Assert.Contains(removed, c => c.Key == "body:0304305B#2" && c.Right is null);
    }

    [Fact]
    public void HeaderDuplicates_ContentHeavyFlagAndRemovalAreDetected()
    {
        var left = Container([], (0x03043005, new byte[] { 1 }), (0x03043005, new byte[] { 2 }));
        var right = Container([], (0x03043005, new byte[] { 9 }), (0x03043005, new byte[] { 2 }));
        Assert.Contains(MapContentComparison.Compare(left, right), c => c.Key == "header:03043005");
        Assert.Contains(MapContentComparison.Compare(left, Container([], (0x03043005, new byte[] { 1 }))),
            c => c.Key == "header:03043005#2" && c.Right is null);
        right = (byte[])left.Clone();
        right[28] ^= 0x80;
        Assert.Contains(MapContentComparison.Compare(left, right), c => c.Key == "content:container");
    }

    [Theory]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(17)]
    public void ContainerFields_AreCovered(int offset)
    {
        var left = Container([1, 2, 3]);
        var right = (byte[])left.Clone();
        right[offset] = offset == 8 ? (byte)'E' : (byte)(right[offset] + 1);
        var changes = MapContentComparison.Compare(left, right);
        Assert.Contains(changes, c => c.Key == "content:container");
        Assert.DoesNotContain(changes, c => c.Key == "content:decompressed-body");
    }

    [Fact]
    public void NonSkippableOpaqueTailAndFalseAnchor_AreCoveredWithoutClaimingChunkCoverage()
    {
        byte[] body = [0x49, 0x30, 0x04, 0x03, 1, 2, 3, .. Skip(0x03043FFF, [8]), 4, 5];
        var right = (byte[])body.Clone();
        right[^1] = 6;
        var changes = MapContentComparison.Compare(Container(body), Container(right));
        Assert.Contains(changes, c => c.Key == "content:decompressed-body" && c.Left != c.Right);
        var candidate = Assert.Single(changes, c => c.Key == "body:03043049");
        Assert.Contains("candidate", candidate.Left!);
        Assert.Contains("not exhaustive", candidate.Left!);
    }

    [Fact]
    public void IdenticalBytes_HaveNoChanges()
    {
        var bytes = Container(Skip(0x03043029, [1, 2]));
        Assert.Empty(MapContentComparison.Compare(bytes, (byte[])bytes.Clone()));
    }

    [Fact]
    public void PasswordChunkRemoval_IsReportedIndependentlyOfPasswordMetadata()
    {
        var changes = MapContentComparison.Compare(Container(Skip(0x03043029, [1, 2])), Container([]));
        Assert.Contains(changes, c => c.Key == "body:03043029" && c.Left is not null && c.Right is null);
    }

    [Fact]
    public void CompareMaps_AllDetectsSerializedChunkRemoval()
    {
        var left = new CGameCtnChallenge();
        left.Chunks.Create<CGameCtnChallenge.Chunk03043029>();
        Assert.Empty(DiffMode.CompareMaps(left, new()).Chunks);
        Assert.Contains(DiffMode.CompareMaps(left, new(), all: true).Chunks, c => c.Key == "body:03043029" && c.Right is null);
    }

    [Fact]
    public void Sample_SameFileAndSavedChunkRemovalUseContentComparison()
    {
        SampleMap.SkipUnlessAvailable();
        var original = File.ReadAllBytes(SampleMap.Path);
        Assert.Empty(MapContentComparison.Compare(original, original));
        Gbx.LZO ??= new GBX.NET.LZO.Lzo();
        Gbx.ZLib = new GBX.NET.ZLib.ZLib();
        using var input = new MemoryStream(original);
        var gbx = Gbx.Parse<CGameCtnChallenge>(input);
        Assert.Contains(gbx.Node.Chunks, c => c.Id == 0x0304305B);
        gbx.Node.Chunks.Remove(0x0304305B);
        using var output = new MemoryStream();
        gbx.Save(output);
        var changes = MapContentComparison.Compare(original, output.ToArray());
        Assert.Contains(changes, c => c.Key == "body:0304305B" && c.Right is null);
        Assert.Contains(changes, c => c.Key == "content:decompressed-body");
    }

    [Fact]
    public void EverySyntheticBodyByte_IsCoveredEvenWhenFramingChanges()
    {
        byte[] body = [0x49, 0x30, 0x04, 0x03, 7, 8, .. Skip(0x0304305B, [1, 2, 3]), 0x01, 0xDE, 0xCA, 0xFA, 9];
        var left = Container(body);
        for (var offset = 0; offset < body.Length; offset++)
        {
            var changed = (byte[])body.Clone();
            changed[offset] ^= 1;
            Assert.Contains(MapContentComparison.Compare(left, Container(changed)), c => c.Key == "content:decompressed-body");
        }
    }

    [Fact]
    public void Sample_BodyTailAndContainerEncodingAreCovered()
    {
        SampleMap.SkipUnlessAvailable();
        var original = File.ReadAllBytes(SampleMap.Path);
        Gbx.LZO = new GBX.NET.LZO.Lzo();
        using var input = new MemoryStream(original);
        using var output = new MemoryStream();
        Gbx.Decompress(input, output);
        var decompressed = output.ToArray();
        var encoding = MapContentComparison.Compare(original, decompressed);
        Assert.Contains(encoding, c => c.Key == "content:container");
        Assert.Contains(encoding, c => c.Key == "content:stored-body");
        Assert.DoesNotContain(encoding, c => c.Key == "content:decompressed-body");
        var layout = GbxContainerReader.Read(decompressed);
        var mutated = (byte[])decompressed.Clone();
        mutated[^1] ^= 1;
        Assert.Contains(MapContentComparison.Compare(decompressed, mutated), c => c.Key == "content:decompressed-body");
        Assert.True(layout.BodyOffset < mutated.Length);
    }

    [Fact]
    public void Sample_CompareFilesAllAndJsonPreserveContentChanges()
    {
        SampleMap.SkipUnlessAvailable();
        var original = File.ReadAllBytes(SampleMap.Path);
        var layout = GbxContainerReader.Read(original);
        var thumbnail = Assert.Single(layout.HeaderChunks, c => c.ChunkId == 0x03043007);
        var changed = (byte[])original.Clone();
        changed[checked((int)thumbnail.FileOffset + 40)] ^= 1;
        var path = Path.Combine(Path.GetTempPath(), $"gbx-content-{Guid.NewGuid():N}.Map.gbx");
        try
        {
            File.WriteAllBytes(path, changed);
            Assert.Empty(DiffMode.CompareFiles(SampleMap.Path, path).Chunks);
            var report = DiffMode.CompareFiles(SampleMap.Path, path, all: true);
            Assert.Equal(report.LeftBytes, report.RightBytes);
            Assert.Contains(report.Chunks, c => c.Key == "header:03043007" && c.Left != c.Right);
            using var json = JsonDocument.Parse(DiffMode.RenderJson(report));
            Assert.Contains(json.RootElement.GetProperty("Chunks").EnumerateArray(),
                c => c.GetProperty("Key").GetString() == "content:container" && c.GetProperty("Left").ValueKind == JsonValueKind.String);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CompareMaps_AllWithoutChunksIsExplicitlySerializedContentOnly()
    {
        var left = new CGameCtnChallenge { DecoBaseHeightOffset = 1 };
        var right = new CGameCtnChallenge { DecoBaseHeightOffset = 2 };
        Assert.Empty(DiffMode.CompareMaps(left, right, all: true).Chunks);
        left.Chunks.Create<CGameCtnChallenge.Chunk03043052>();
        right.Chunks.Create<CGameCtnChallenge.Chunk03043052>();
        Assert.Contains(DiffMode.CompareMaps(left, right, all: true).Chunks, c => c.Key == "body:03043052" && c.Left != c.Right);
    }

    [Fact]
    public void UnsupportedExternalReferenceTable_ThrowsInsteadOfClaimingEquality()
    {
        var bytes = Container([]);
        bytes[21] = 1;
        var error = Assert.Throws<NotSupportedException>(() => MapContentComparison.Compare(bytes, bytes));
        Assert.Contains("External-node", error.Message);
    }

    [Fact]
    public void CompareFiles_AllFallsBackToContentOnlyForOpaqueNonMapContainers()
    {
        var left = Container(Skip(0x03043FFF, [1, 2]));
        var right = Container(Skip(0x03043FFF, [1, 9]));
        var leftPath = Path.GetTempFileName();
        var rightPath = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(leftPath, left);
            File.WriteAllBytes(rightPath, right);
            Assert.ThrowsAny<Exception>(() => DiffMode.CompareFiles(leftPath, rightPath));
            var report = DiffMode.CompareFiles(leftPath, rightPath, all: true);
            Assert.Contains(report.Chunks, c => c.Key == "content:decompressed-body" && c.Left != c.Right);
            Assert.Contains(report.Chunks, c => c.Key == "content-only-fallback" && c.Left is not null);
            Assert.Empty(report.Blocks);
            Assert.Null(report.MapUid);
        }
        finally { File.Delete(leftPath); File.Delete(rightPath); }
    }

    [Fact]
    public void CompareFiles_AllFallbackReportsRemovalWithoutFalseEquality()
    {
        var leftPath = Path.GetTempFileName();
        var rightPath = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(leftPath, Container(Skip(0x03043FFF, [1])));
            File.WriteAllBytes(rightPath, Container([]));
            var report = DiffMode.CompareFiles(leftPath, rightPath, all: true);
            Assert.NotEmpty(report.Chunks);
            Assert.Contains(report.Chunks, c => c.Key == "body:03043FFF" && c.Right is null);
            Assert.Contains(report.Chunks, c => c.Key == "content-only-fallback");
        }
        finally { File.Delete(leftPath); File.Delete(rightPath); }
    }

    private static byte[] Skip(uint id, byte[] payload)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(id);
        writer.Write(0x534B4950u);
        writer.Write(payload.Length);
        writer.Write(payload);
        return stream.ToArray();
    }

    private static byte[] Container(byte[] body, params (uint Id, byte[] Payload)[] headers)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("GBX"u8);
        writer.Write((short)6);
        writer.Write("BUUR"u8);
        writer.Write(0x03043000u);
        writer.Write(headers.Length == 0 ? 0 : 4 + headers.Sum(h => 8 + h.Payload.Length));
        if (headers.Length > 0)
        {
            writer.Write(headers.Length);
            foreach (var (id, payload) in headers) { writer.Write(id); writer.Write(payload.Length); }
            foreach (var (_, payload) in headers) writer.Write(payload);
        }
        writer.Write(1);
        writer.Write(0);
        writer.Write(body);
        return stream.ToArray();
    }
}
