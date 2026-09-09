using GbxSizeTree.Cli.Modes;
using GbxSizeTree.Cli.Rendering;
using GbxSizeTree.Semantics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace GbxSizeTree.Tests.Rendering;

public sealed class DiffInfographicTests
{
    [Fact]
    public void BuildScene_ReportsExactCountsAndBoundedSpatialOmissions()
    {
        var items = Enumerable.Range(0, 430).Select(i => new ValueChange<ItemSnapshot>(
            i % 2 == 0 ? Item($"removed-{i}", i, i % 17) : null,
            i % 2 == 0 ? null : Item($"added-{i}", i, i % 17))).ToArray();
        var report = Empty() with
        {
            Items = items,
            LeftItemSnapshots = items.Select(x => x.Left).OfType<ItemSnapshot>().Append(Item("bad", double.NaN, double.PositiveInfinity)).ToArray(),
            RightItemSnapshots = items.Select(x => x.Right).OfType<ItemSnapshot>().ToArray(),
            Embedded =
            [
                new(null, new("new.Item.Gbx", "new", 100, 400)),
                new(new("gone.Item.Gbx", "old", 80, 300), null),
                new(new("edit.Item.Gbx", "a", 20, 200), new("edit.Item.Gbx", "b", 25, 240)),
            ],
            MetadataChanges = [new("map.name", new("Before"), new("After"))],
        };

        var scene = DiffInfographic.BuildScene(report, "old.Map.Gbx", "new.Map.Gbx");

        Assert.Equal(1400, scene.Width);
        Assert.InRange(scene.Height, 900, 2200);
        Assert.Equal(new DiffInfographicCounts(216, 216, 1), scene.Counts);
        Assert.Equal(400, scene.Spatial.PlottedChanges);
        Assert.Equal(30, scene.Spatial.OmittedChanges);
        Assert.Equal(1, scene.Spatial.IgnoredNonFinitePositions);
        Assert.Contains(scene.Sections, x => x.Id == "spatial-context");
        Assert.Contains(scene.Sections, x => x.Id == "embedded-highlights");
        Assert.All(scene.Sections, x =>
        {
            Assert.InRange(x.Left, 0, scene.Width);
            Assert.InRange(x.Right, x.Left, scene.Width);
            Assert.InRange(x.Top, 0, scene.Height);
            Assert.InRange(x.Bottom, x.Top, scene.Height);
        });
    }

    [Fact]
    public void Render_EncodesPngWithEmbeddedFontsAndSafeExtremeText()
    {
        var hostile = "\0bad\ud800 / 世界 / e\u0301 / " + string.Concat(Enumerable.Repeat("very-long-name-", 500));
        var report = Empty() with
        {
            LeftBytes = long.MinValue,
            RightBytes = long.MaxValue,
            Items = [new(null, Item(hostile, double.NaN, double.NegativeInfinity))],
            Embedded = [new(null, new(hostile, "hash", 0, 0))],
            Warnings = [hostile],
            EmbeddedPropertyChanges = [new(hostile, "left", "right", new(
                [new(hostile, new("old"), new("new"))], false,
                [new("opaque", hostile, hostile)], []))],
        };

        using Image<Rgba32> image = DiffInfographic.Render(report, hostile, "");
        using Image<Rgba32> second = DiffInfographic.Render(report, hostile, "");
        using var output = new MemoryStream();
        using var secondOutput = new MemoryStream();
        image.SaveAsPng(output);
        second.SaveAsPng(secondOutput);

        Assert.Equal(1400, image.Width);
        Assert.InRange(image.Height, 900, 2200);
        Assert.True(output.Length > 20_000);
        var encoded = output.ToArray();
        Assert.Equal(encoded, secondOutput.ToArray());
        Assert.Equal("PNG", System.Text.Encoding.ASCII.GetString(encoded, 1, 3));
    }

    [Fact]
    public void BuildScene_DoesNotPairMarginalMeasurementsOrExposeScale()
    {
        var report = Empty() with
        {
            Items = [new(Item("same", 1, 1) with { Scale = 1 }, null), new(null, Item("same", 1, 1) with { Scale = 2 })],
            EmbeddedContributions =
            [
                new(new("a", 10, 20, 4, null), new("a", 11, 21, 9, null)),
                new(null, new("b", 12, 22, null, "trial budget exhausted")),
            ],
        };

        var scene = DiffInfographic.BuildScene(report, "a", "b");
        var allText = string.Join('\n', scene.Sections.SelectMany(x => x.Lines));

        Assert.DoesNotContain("Scale", allText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("13", allText);
        Assert.Contains("non-additive", allText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("trial budget exhausted", allText);
        Assert.Equal(1, scene.Counts.Added);
        Assert.Equal(1, scene.Counts.Removed);
    }

    [Fact]
    public void BuildScene_EmptyDiffUsesMinimumCanvasAndNoDetails()
    {
        var scene = DiffInfographic.BuildScene(Empty(), "", "");

        Assert.InRange(scene.Height, DiffInfographic.MinimumHeight, DiffInfographic.MaximumHeight);
        Assert.Equal(new DiffInfographicCounts(0, 0, 0), scene.Counts);
        Assert.Equal(0, scene.Spatial.PlottedChanges);
        Assert.Equal(["hero", "change-counts", "spatial-context"], scene.Sections.Select(x => x.Id));
    }

    [Fact]
    public void BuildScene_DoesNotDoubleCountLegacyMetadata()
    {
        var report = Empty() with
        {
            MapName = new("Before", "After"),
            MetadataChanges = [new("map.name", new("Before"), new("After")), new("editor.medal", new(Integer: 100), new(Integer: 90))],
        };

        var scene = DiffInfographic.BuildScene(report, "a", "b");
        var metadata = Assert.Single(scene.Sections, x => x.Id == "metadata");

        Assert.Equal(2, scene.Counts.Changed);
        Assert.Equal(2, metadata.Lines.Count);
        Assert.Single(metadata.Lines, x => x.StartsWith("Map name:", StringComparison.Ordinal));
    }

    [Fact]
    public void PreviewHarness_SaveAsPngForSyntheticAndRealMapsWhenRequested()
    {
        var outputDirectory = Environment.GetEnvironmentVariable("GBX_SIZE_TREE_INFOGRAPHIC_PREVIEW");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(outputDirectory), "Set GBX_SIZE_TREE_INFOGRAPHIC_PREVIEW to generate preview PNGs.");
        Directory.CreateDirectory(outputDirectory!);

        var synthetic = Empty() with
        {
            RightBytes = 1_684_217,
            Blocks =
            [
                new(null, Block("RoadTechStraight", 32, 32)),
                new(Block("RoadTechCurve", 96, 64), null),
                new(null, Block("DecorationNeon", 128, 100)),
            ],
            LeftBlockSnapshots = [Block("Context A", 0, 0), Block("Context B", 160, 120)],
            RightBlockSnapshots = [Block("Context A", 0, 0), Block("Context C", 200, 150)],
            Embedded =
            [
                new(new("Embedded/Items/Stadium/Grandstand.Item.Gbx", "old", 412_000, 900_000), new("Embedded/Items/Stadium/Grandstand.Item.Gbx", "new", 510_000, 1_020_000)),
                new(null, new("Embedded/Items/Lighting/AmberStrips.Item.Gbx", "new", 184_000, 410_000)),
                new(new("Embedded/Items/Signs/OldSponsor.Item.Gbx", "old", 92_000, 230_000), null),
            ],
            MapName = new("Night Circuit v1", "Night Circuit v2"),
            EmbeddedPropertyChanges = [new("Grandstand.Item.Gbx", "old", "new", new(
                [new("Item > EntityModel > Prefab > Ent#2 > Solid2Model > Material#1 > Name", new("Concrete"), new("Carbon"))], true, [], []))],
            Warnings = ["One custom chunk remained opaque; file-level change detection is still complete."],
        };
        using (var image = DiffInfographic.Render(synthetic, "Night Circuit — draft.Map.Gbx", "Night Circuit — release.Map.Gbx"))
            image.SaveAsPng(Path.Combine(outputDirectory!, "diff-infographic-synthetic.png"));

        var left = Environment.GetEnvironmentVariable("GBX_SIZE_TREE_INFOGRAPHIC_LEFT");
        var right = Environment.GetEnvironmentVariable("GBX_SIZE_TREE_INFOGRAPHIC_RIGHT");
        if (File.Exists(left) && File.Exists(right))
        {
            var real = DiffMode.CompareFiles(left, right, all: true);
            using var image = DiffInfographic.Render(real, left, right);
            image.SaveAsPng(Path.Combine(outputDirectory!, "diff-infographic-v205-v206.png"));
        }
    }

    private static BlockSnapshot Block(string name, int x, int z) =>
        new(name, x / 32, 0, z / 32, new(x, 0, z), null, "North", 0, 0, false, false, true, "Default", "Normal", 0);

    private static ItemSnapshot Item(string path, double x, double z) =>
        new(path, new(x, 0, z), default, "Default", 1, default, "None", "Normal", 0);

    private static DiffReport Empty() => new(1_000_000, 1_000_000, [], [], [], [], [], null, null, null, null, null);
}
