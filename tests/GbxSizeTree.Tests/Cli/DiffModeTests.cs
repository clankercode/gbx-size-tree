using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.Game;
using GbxSizeTree.Cli.Modes;

namespace GbxSizeTree.Tests.Cli;

public sealed class DiffModeTests
{
    [Fact]
    public void CompareMaps_PreservesDuplicatePlacements()
    {
        var block = new CGameCtnBlock { Name = "duplicate", Coord = new(1, 2, 3) };
        var item = new CGameCtnAnchoredObject { AbsolutePositionInMap = new(1, 2, 3) };
        var report = DiffMode.CompareMaps(new() { Blocks = [block, block], AnchoredObjects = [item] },
            new() { Blocks = [block], AnchoredObjects = [item, item] });
        Assert.NotNull(Assert.Single(report.Blocks).Left);
        Assert.NotNull(Assert.Single(report.Items).Right);
    }

    [Fact]
    public void CompareMaps_DetectsColorScaleAndFreeTransformChanges()
    {
        var left = new CGameCtnChallenge
        {
            Blocks = [new() { Name = "free", IsFree = true, AbsolutePositionInMap = new(1, 2, 3) }],
            AnchoredObjects = [new() { Scale = 1, Color = (DifficultyColor)0 }],
        };
        var right = new CGameCtnChallenge
        {
            Blocks = [new() { Name = "free", IsFree = true, AbsolutePositionInMap = new(1.5f, 2, 3) }],
            AnchoredObjects = [new() { Scale = 2, Color = (DifficultyColor)1 }],
        };
        var report = DiffMode.CompareMaps(left, right);
        Assert.Equal(2, report.Blocks.Count);
        Assert.Equal(2, report.Items.Count);
        Assert.Equal(1.5, report.Blocks.Single(c => c.Right is not null).Right!.PhysicalPosition!.Value.X);
    }

    [Fact]
    public void CompareMaps_BakedBlocksRequireAllAndUseMidpoints()
    {
        var right = new CGameCtnChallenge { BakedBlocks = [new() { Name = "baked", Coord = new(1, 2, 3) }] };
        Assert.Empty(DiffMode.CompareMaps(new(), right).BakedBlocks);
        var report = DiffMode.CompareMaps(new(), right, all: true);
        Assert.Equal(new SpatialPosition(48, 20, 112), Assert.Single(report.BakedBlocks).Right!.PhysicalPosition);
        Assert.Single(report.RightBakedSnapshots);
    }

    [Fact]
    public void CompareMaps_EmbeddedContentChangesUseTypedDataEvenWithSameSizes()
    {
        var left = MapWithEmbed("folder/asset.Item.Gbx", "aaaa");
        var right = MapWithEmbed("folder/asset.Item.Gbx", "bbbb");
        var change = Assert.Single(DiffMode.CompareMaps(left, right).Embedded);
        Assert.NotNull(change.Left);
        Assert.NotNull(change.Right);
        Assert.Equal(change.Left.Uncompressed, change.Right.Uncompressed);
        Assert.Equal(change.Left.Compressed, change.Right.Compressed);
        Assert.NotEqual(change.Left.Sha256, change.Right.Sha256);
        Assert.Equal("folder/asset.Item.Gbx", change.Right.Path);
    }

    [Fact]
    public void CompareMaps_CaseOnlyEmbedRenamePreservesDistinctPaths()
    {
        var report = DiffMode.CompareMaps(MapWithEmbed("asset.Item.Gbx", "data"), MapWithEmbed("Asset.Item.Gbx", "data"));
        Assert.Equal(2, report.Embedded.Count);
        Assert.Equal("asset.Item.Gbx", report.Embedded.Single(c => c.Left is not null).Left!.Path);
        Assert.Equal("Asset.Item.Gbx", report.Embedded.Single(c => c.Right is not null).Right!.Path);
    }

    [Fact]
    public void CompareMaps_CaseDistinctZipEntriesAreNotOverwritten()
    {
        var left = MapWithEmbeds(("asset.Item.Gbx", "old"), ("Asset.Item.Gbx", "keep"));
        var right = MapWithEmbeds(("asset.Item.Gbx", "new"), ("Asset.Item.Gbx", "keep"));
        var report = DiffMode.CompareMaps(left, right);
        Assert.Equal(2, report.LeftEmbeddedSnapshots.Count);
        Assert.Equal("asset.Item.Gbx", Assert.Single(report.Embedded).Left!.Path);
        Assert.NotEqual(report.Embedded[0].Left!.Sha256, report.Embedded[0].Right!.Sha256);
    }

    [Fact]
    public void CompareMaps_RejectsAmbiguousDuplicateZipPaths()
    {
        var map = MapWithEmbeds(("asset.Item.Gbx", "first"), ("asset.Item.Gbx", "second"));
        Assert.Throws<InvalidDataException>(() => DiffMode.CompareMaps(new(), map));
    }

    [Fact]
    public void Json_PreservesEnvelopeStringChangesAndStructuredSnapshots()
    {
        var left = MapWithEmbed("asset.Item.Gbx", "aaaa");
        var right = MapWithEmbed("asset.Item.Gbx", "bbbb");
        right.Blocks = [new() { Name = "new", Coord = new(1, 2, 3) }];
        using var json = JsonDocument.Parse(DiffMode.RenderJson(DiffMode.CompareMaps(left, right)));
        var root = json.RootElement;
        Assert.Equal(JsonValueKind.String, root.GetProperty("Embedded")[0].GetProperty("Left").ValueKind);
        Assert.Equal("asset.Item.Gbx", root.GetProperty("Embedded")[0].GetProperty("Key").GetString());
        var typed = root.GetProperty("EmbeddedChanges")[0];
        Assert.Equal("asset.Item.Gbx", typed.GetProperty("Left").GetProperty("Path").GetString());
        Assert.Equal(4, typed.GetProperty("Right").GetProperty("Uncompressed").GetInt64());
        Assert.Equal(1, typed.GetProperty("Right").GetProperty("Ratio").GetDouble());
        Assert.NotEqual(typed.GetProperty("Left").GetProperty("Sha256").GetString(), typed.GetProperty("Right").GetProperty("Sha256").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("Blocks")[0].GetProperty("Left").ValueKind);
        Assert.Contains("new|coord=", root.GetProperty("Blocks")[0].GetProperty("Right").GetString());
        Assert.Equal(48, root.GetProperty("RightBlockSnapshots")[0].GetProperty("PhysicalPosition").GetProperty("X").GetDouble());
        Assert.NotEmpty(root.GetProperty("LeftEmbeddedSnapshots")[0].GetProperty("Sha256").GetString()!);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("Password").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("LeftBakedSnapshots").ValueKind);
    }

    [Fact]
    public void SpatialOrdering_IsSignedFractionalLargeAndCultureIndependent()
    {
        var positions = new[] { new SpatialPosition(2048, 0, 0), new(-.125, 0, 0), new(.25, 0, 0), new(.125, 0, 0), new(1e20, 0, 0) };
        Assert.Equal(new[] { -.125, .125, .25, 2048, 1e20 }, positions.Order().Select(p => p.X));
        Assert.True(new SpatialPosition(0, 4, 1).CompareTo(new(0, 0, 2)) > 0);
        Assert.True(new SpatialPosition(0, 1, 0).CompareTo(new(0, 2, 0)) < 0);
        Assert.Equal(((double)int.MaxValue + .5) * 32, SpatialPosition.Midpoint(new(int.MaxValue, 0, 0)).X);
        var culture = CultureInfo.CurrentCulture;
        try
        {
            var commaCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            commaCulture.NumberFormat.NumberDecimalSeparator = ",";
            CultureInfo.CurrentCulture = commaCulture;
            Assert.Equal("(0.125, -0.5, 1E+20)", new SpatialPosition(.125, -.5, 1e20).ToString());
            Assert.Contains("ratio=0.5", new EmbeddedSnapshot("path", "hash", 1, 2).ToValue());
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [Fact]
    public void CompareMaps_SortsMixedChangesAndJsonIndependentlyOfInputOrder()
    {
        var near = new CGameCtnBlock { Name = "near", Coord = new(1, 0, 0) };
        var far = new CGameCtnBlock { Name = "far", Coord = new(3, 0, 0) };
        var middle = new CGameCtnBlock { Name = "middle", IsFree = true, AbsolutePositionInMap = new(80, 4, 16) };
        var report = DiffMode.CompareMaps(new() { Blocks = [far, near] }, new() { Blocks = [middle] });
        Assert.Equal(new[] { "near", "middle", "far" }, report.Blocks.Select(c => (c.Right ?? c.Left)!.Name));
        Assert.Equal(DiffMode.RenderJson(report), DiffMode.RenderJson(
            DiffMode.CompareMaps(new() { Blocks = [near, far] }, new() { Blocks = [middle] })));
    }

    [Theory]
    [InlineData("color")]
    [InlineData("scale")]
    [InlineData("rotation")]
    [InlineData("pivot")]
    [InlineData("animation")]
    [InlineData("lightmap")]
    [InlineData("flags")]
    public void CompareMaps_DetectsIndividualItemProperties(string property)
    {
        var item = new CGameCtnAnchoredObject();
        switch (property)
        {
            case "color": item.Color = (DifficultyColor)1; break;
            case "scale": item.Scale = 2; break;
            case "rotation": item.YawPitchRoll = new(.25f, 0, 0); break;
            case "pivot": item.PivotPosition = new(0, 1, 0); break;
            case "animation": item.AnimPhaseOffset = (CGameCtnAnchoredObject.EPhaseOffset)1; break;
            case "lightmap": item.LightmapQuality = (LightmapQuality)1; break;
            case "flags": item.Flags = 1; break;
        }
        var report = DiffMode.CompareMaps(new() { AnchoredObjects = [new()] }, new() { AnchoredObjects = [item] });
        Assert.Equal(2, report.Items.Count);
    }

    [Fact]
    public void JsonAndSpatialOrdering_HandleNonFiniteAndMissingPositions()
    {
        var report = DiffMode.CompareMaps(new(), new()
        {
            Blocks = [new() { Name = "missing", IsFree = true }],
            AnchoredObjects = [new() { AbsolutePositionInMap = new(float.NaN, float.PositiveInfinity, float.NegativeInfinity) }],
        });
        using var json = JsonDocument.Parse(DiffMode.RenderJson(report));
        Assert.Equal("NaN", json.RootElement.GetProperty("RightItemSnapshots")[0].GetProperty("PhysicalPosition").GetProperty("X").GetString());
        Assert.Null(Assert.Single(report.Blocks).Right!.PhysicalPosition);
        Assert.True(new SpatialPosition(double.NaN, 0, 0).CompareTo(new(0, 0, 0)) < 0);
    }

    private static CGameCtnChallenge MapWithEmbed(string path, string content) => MapWithEmbeds((path, content));

    private static CGameCtnChallenge MapWithEmbeds(params (string Path, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path, CompressionLevel.NoCompression).Open());
                writer.Write(content);
            }
        }
        return new() { EmbeddedZipData = stream.ToArray() };
    }
}
