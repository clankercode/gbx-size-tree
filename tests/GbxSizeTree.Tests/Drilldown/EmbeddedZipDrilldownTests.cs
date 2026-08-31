using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.LZO;
using GBX.NET.ZLib;
using GbxSizeTree.Drilldown;
using GbxSizeTree.Tests.Fixtures;
using System.IO.Compression;

namespace GbxSizeTree.Tests.Drilldown;

public sealed class EmbeddedZipDrilldownTests
{
    private static readonly Lazy<(
        CGameCtnChallenge Map,
        GbxSizeTree.Model.EmbeddedZipInfo Result)> Sample = new(LoadSample);

    [Fact]
    public void Inspect_MapWithoutEmbeddedData_ReturnsNull()
    {
        var result = new EmbeddedZipDrilldown().Inspect(new CGameCtnChallenge());

        Assert.Null(result);
    }

    [Fact]
    public void Inspect_SampleMap_ReportsZipAndEntrySizes()
    {
        var result = InspectSample();

        Assert.Equal(1_216_568, result.ZipBytes);
        Assert.NotEmpty(result.Entries);
        Assert.True(result.Entries.Sum(x => x.CompressedBytes) <= result.ZipBytes);
        Assert.All(result.Entries, entry => Assert.False(string.IsNullOrEmpty(entry.Path)));
        Assert.Equal(result.Entries.Sum(x => x.UncompressedBytes), result.EntriesUncompressedBytes);
    }

    [Fact]
    public void Inspect_SampleMap_HasNoCompressedGbxBodies()
    {
        var result = InspectSample();

        // See docs/FORMAT-NOTES.md, "Embedded-items optimization": every embedded
        // GBX in this sample has an uncompressed ('U') body.
        Assert.All(result.Entries, entry => Assert.False(entry.HasCompressedGbxBody));
    }

    [Fact]
    public void Inspect_SyntheticCompressedGbxBody_DetectsIt()
    {
        var map = new CGameCtnChallenge
        {
            EmbeddedZipData = CreateZipWithCompressedGbxBody()
        };

        var result = Assert.IsType<GbxSizeTree.Model.EmbeddedZipInfo>(new EmbeddedZipDrilldown().Inspect(map));
        var entry = Assert.Single(result.Entries);

        Assert.True(entry.IsGbx);
        Assert.True(entry.HasCompressedGbxBody);
    }

    [Fact]
    public void Inspect_UnsupportedItem_RecoversVertexCountFromUncompressedVertexStreamChunks()
    {
        var map = new CGameCtnChallenge
        {
            EmbeddedZipData = CreateZipWithRawVertexStream(vertexCount: 1_234)
        };

        var result = Assert.IsType<GbxSizeTree.Model.EmbeddedZipInfo>(new EmbeddedZipDrilldown().Inspect(map));
        var entry = Assert.Single(result.Entries);

        Assert.Equal(1_234, entry.VertexCount);
        Assert.True(entry.VertexCountEstimated);
    }

    [Fact]
    public void Inspect_UnframedVertexChunkPattern_DoesNotInventVertexCount()
    {
        var map = new CGameCtnChallenge
        {
            EmbeddedZipData = CreateZipWithRawVertexStream(vertexCount: 1_234, includeNodeFraming: false)
        };

        var result = Assert.IsType<GbxSizeTree.Model.EmbeddedZipInfo>(new EmbeddedZipDrilldown().Inspect(map));

        Assert.Null(Assert.Single(result.Entries).VertexCount);
    }

    [Fact]
    public void Inspect_OversizedItem_SkipsVertexInspection()
    {
        var map = new CGameCtnChallenge
        {
            EmbeddedZipData = CreateZipWithOversizedItem()
        };

        var result = Assert.IsType<GbxSizeTree.Model.EmbeddedZipInfo>(new EmbeddedZipDrilldown().Inspect(map));

        Assert.Null(Assert.Single(result.Entries).VertexCount);
    }

    [Fact]
    public void Inspect_SampleMap_HasValidGbxStemsAndReferencedIdents()
    {
        var result = InspectSample();

        Assert.NotEmpty(result.ReferencedIdents);
        Assert.All(result.Entries.Where(x => x.IsGbx), entry =>
        {
            var stem = Path.GetFileNameWithoutExtension(entry.Path);
            Assert.False(string.IsNullOrEmpty(stem));
        });
    }

    [Fact]
    public void Inspect_SampleMap_ReportsVerticesForEmbeddedItems()
    {
        var result = InspectSample();
        var items = result.Entries
            .Where(entry => entry.Path.EndsWith(".Item.Gbx", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(items);
        Assert.Contains(items, entry => entry.VertexCount is > 0);
        Assert.All(
            result.Entries.Where(entry => !entry.Path.EndsWith(".Item.Gbx", StringComparison.OrdinalIgnoreCase)),
            entry => Assert.Null(entry.VertexCount));
    }

    private static GbxSizeTree.Model.EmbeddedZipInfo InspectSample()
        => InspectSampleWithMap().Result;

    private static (CGameCtnChallenge Map, GbxSizeTree.Model.EmbeddedZipInfo Result) InspectSampleWithMap()
    {
        SampleMap.SkipUnlessAvailable();
        return Sample.Value;
    }

    private static (CGameCtnChallenge Map, GbxSizeTree.Model.EmbeddedZipInfo Result) LoadSample()
    {
        Gbx.LZO = new Lzo();
        Gbx.ZLib = new ZLib();
        var map = Gbx.Parse<CGameCtnChallenge>(SampleMap.Path).Node;

        var result = Assert.IsType<GbxSizeTree.Model.EmbeddedZipInfo>(new EmbeddedZipDrilldown().Inspect(map));
        return (map, result);
    }

    private static byte[] CreateZipWithCompressedGbxBody()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("Synthetic.Item.Gbx");
            using var entryStream = entry.Open();
            entryStream.Write([
                (byte)'G', (byte)'B', (byte)'X',
                6, 0,
                (byte)'B', (byte)'U', (byte)'C',
                0, 0, 0, 0
            ]);
        }

        return stream.ToArray();
    }

    private static byte[] CreateZipWithRawVertexStream(int vertexCount, bool includeNodeFraming = true)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("Synthetic.Item.Gbx");
            using var entryStream = entry.Open();
            entryStream.Write([
                (byte)'G', (byte)'B', (byte)'X',
                6, 0,
                (byte)'B', (byte)'U', (byte)'U',
                0, 0, 0, 0,
            ]);
            if (includeNodeFraming)
            {
                entryStream.Write([1, 0, 0, 0, 0x00, 0x60, 0x05, 0x09]);
            }
            entryStream.Write([
                0x00, 0x60, 0x05, 0x09,
                1, 0, 0, 0,
                (byte)vertexCount, (byte)(vertexCount >> 8),
                (byte)(vertexCount >> 16), (byte)(vertexCount >> 24),
            ]);
        }

        return stream.ToArray();
    }

    private static byte[] CreateZipWithOversizedItem()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("Oversized.Item.Gbx", CompressionLevel.SmallestSize);
            using var entryStream = entry.Open();
            var megabyte = new byte[1024 * 1024];
            for (var i = 0; i < 17; i++)
            {
                entryStream.Write(megabyte);
            }
        }

        return stream.ToArray();
    }
}
