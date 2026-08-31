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

    private static GbxSizeTree.Model.EmbeddedZipInfo InspectSample()
        => InspectSampleWithMap().Result;

    private static (CGameCtnChallenge Map, GbxSizeTree.Model.EmbeddedZipInfo Result) InspectSampleWithMap()
    {
        SampleMap.SkipUnlessAvailable();
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
}
