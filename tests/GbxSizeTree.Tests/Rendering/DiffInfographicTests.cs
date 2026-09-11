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
        Assert.True(scene.Height >= DiffInfographic.MinimumHeight);
        Assert.Equal(new DiffInfographicCounts(215, 215, 0), scene.Placements.Counts);
        Assert.Equal(new DiffInfographicCounts(1, 1, 1), scene.Embedded.Counts);
        Assert.Equal(new DiffInfographicDetailCounts(1, 0, 0), scene.DetailCounts);
        Assert.Equal(400, scene.Spatial.PlottedChanges);
        Assert.Equal(30, scene.Spatial.SampledOutChanges);
        Assert.Equal(0, scene.Spatial.InvalidChangePositions);
        Assert.Equal(0, scene.Spatial.UnpositionedChanges);
        Assert.Equal(430, scene.Spatial.PlottedContext);
        Assert.Equal(0, scene.Spatial.SampledOutContext);
        Assert.Equal(1, scene.Spatial.InvalidContextPositions);
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

    [Theory]
    [InlineData("NO SIZE CHANGE")]
    [InlineData("+9223372036.85 GB")]
    public void HeroDelta_FitsMeasuredLeftColumnWithoutTruncatingNumber(string text)
    {
        var font = DiffInfographicPainter.FitHeroDeltaFont(text);
        var bounds = SixLabors.Fonts.TextMeasurer.MeasureBounds(text, new SixLabors.Fonts.TextOptions(font));

        Assert.True(68 + bounds.X + bounds.Width <= 668.5f);
        Assert.True(font.Size < 82);
    }

    [Fact]
    public void BuildScene_BoundsContributionHighlightsAndReservesExactOmissionCount()
    {
        var contributions = Enumerable.Range(0, 22).Select(i => new ValueChange<GbxSizeTree.Measure.EmbeddedFileContribution>(null,
            new($"contribution-{i}", 1, 1, null, $"unavailable-{i}"))).ToArray();
        var scene = DiffInfographic.BuildScene(Empty() with { EmbeddedContributions = contributions }, "a", "b");
        var highlights = Assert.Single(scene.Sections, x => x.Id == "embedded-highlights");

        Assert.Equal(5, highlights.Lines.Count);
        Assert.Equal("+ 19 more embedded highlights omitted", highlights.Lines[^2].Text);
        Assert.Equal("Marginal measurements are non-additive; 22 unavailable contribution sides: unavailable-0", highlights.Lines[^1].Text);
    }

    [Fact]
    public void BuildScene_ContributionNoteCountsUnavailableLeftAndRightSides()
    {
        var contributions = Enumerable.Range(0, 5).Select(i => new ValueChange<GbxSizeTree.Measure.EmbeddedFileContribution>(
            new($"contribution-{i}", 1, 1, i == 0 ? null : 1, i == 0 ? "left unavailable" : null),
            new($"contribution-{i}", 1, 1, i == 1 ? null : 1, i == 1 ? "right unavailable" : null))).ToArray();
        var scene = DiffInfographic.BuildScene(Empty() with { EmbeddedContributions = contributions }, "a", "b");
        var highlights = Assert.Single(scene.Sections, x => x.Id == "embedded-highlights");

        Assert.Equal(5, highlights.Lines.Count);
        Assert.Equal("+ 2 more embedded highlights omitted", highlights.Lines[^2].Text);
        Assert.Equal("Marginal measurements are non-additive; 2 unavailable contribution sides: left unavailable", highlights.Lines[^1].Text);
    }

    [Fact]
    public void BuildScene_PropertyWarningRetainsFinalSlotAndOmitsOnlyRealDetails()
    {
        var report = Empty() with
        {
            EmbeddedPropertyChanges = [new("container", "left", "right", new(
                Enumerable.Range(0, 6).Select(i => new EmbeddedPropertyChange($"property-{i}", new("old"), new("new"))).ToArray(), true,
                [new("opaque", "left", "partial")], [new("opaque", "right", "partial")]))],
        };

        var scene = DiffInfographic.BuildScene(report, "a", "b");
        var properties = Assert.Single(scene.Sections, x => x.Id == "deep-properties");

        Assert.Equal(5, properties.Lines.Count);
        Assert.Equal("+ 3 more deep-property details omitted", properties.Lines[^2].Text);
        Assert.Equal("WARNING · 2 opaque or partial-coverage diagnostics; content hashes still prove the entries changed.", properties.Lines[^1].Text);
    }

    [Fact]
    public void BuildScene_GrownCanvasKeepsAllSectionLinesRenderableAndCoverageVisible()
    {
        var notices = Enumerable.Range(0, 405).Select(i => new ValueChange<ItemSnapshot>(null, Item($"notice-{i}", i, i))).ToList();
        notices.Add(new(null, Item("invalid", double.NaN, double.PositiveInfinity)));
        var context = Enumerable.Range(0, 905).Select(i => Item($"context-{i}", i, i)).Append(Item("invalid-context", double.NegativeInfinity, 0)).ToArray();
        var report = Empty() with
        {
            Items = notices,
            Blocks = [new(null, Block("unpositioned", 0, 0) with { PhysicalPosition = null })],
            LeftItemSnapshots = context,
            Embedded = Enumerable.Range(0, 8).Select(i => new ValueChange<EmbeddedSnapshot>(null, new($"embed-{i}", "hash", i, i))).ToArray(),
            EmbeddedPropertyChanges = [new("container", "left", "right", new(
                Enumerable.Range(0, 8).Select(i => new EmbeddedPropertyChange($"property-{i}", new("old"), new("new"))).ToArray(), true,
                [new("opaque", "left", "partial")], [new("opaque", "right", "partial")]))],
            MetadataChanges = Enumerable.Range(0, 8).Select(i => new MapMetadataChange($"metadata-{i}", new("old"), new("new"))).ToArray(),
            Chunks = Enumerable.Range(0, 8).Select(i => new Change("old", "new", $"chunk-{i}")).ToArray(),
            Warnings = Enumerable.Range(0, 7).Select(i => $"source warning {i}").ToArray(),
        };

        var scene = DiffInfographic.BuildScene(report, "a", "b");
        var detailSections = scene.Sections.Where(x => x.Top >= 1000).ToArray();
        var coverage = Assert.Single(detailSections, x => x.Id == "coverage");

        Assert.True(scene.Height >= 2100);
        Assert.All(detailSections, section => Assert.True(section.Top + 66 + section.Lines.Count * 57 <= section.Bottom));
        Assert.All(detailSections, section => Assert.True(section.Bottom <= scene.Height - 36));
        Assert.Contains("+ 4 more embedded highlights omitted", Assert.Single(detailSections, x => x.Id == "embedded-highlights").Lines.Select(x => x.Text));
        Assert.Contains("+ 5 more deep-property details omitted", Assert.Single(detailSections, x => x.Id == "deep-properties").Lines.Select(x => x.Text));
        var metadata = Assert.Single(detailSections, x => x.Id == "metadata");
        Assert.Equal(24, metadata.Lines.Count);
        Assert.DoesNotContain(metadata.Lines.Select(x => x.Text), x => x.Contains("omitted", StringComparison.Ordinal));
        Assert.Contains("+ 5 more chunk observations omitted", Assert.Single(detailSections, x => x.Id == "chunks").Lines.Select(x => x.Text));
        Assert.Equal(scene.Warnings, coverage.Lines.Select(x => x.Text));
        Assert.Equal("Coverage notes: 11 below", DiffInfographicPainter.SpatialFooterLabel(scene));
    }

    [Fact]
    public void BuildScene_NoCoverageNoticesUsesExplicitEmptyFooterLabel()
    {
        var scene = DiffInfographic.BuildScene(Empty(), "a", "b");

        Assert.Empty(scene.Warnings);
        Assert.Equal("Coverage notes: none", DiffInfographicPainter.SpatialFooterLabel(scene));
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
        Assert.True(image.Height >= DiffInfographic.MinimumHeight);
        Assert.True(output.Length > 20_000);
        var encoded = output.ToArray();
        Assert.Equal(encoded, secondOutput.ToArray());
        Assert.Equal("PNG", System.Text.Encoding.ASCII.GetString(encoded, 1, 3));
    }

    [Fact]
    public void BuildScene_HeroCountsEntitiesOnlyAndCountsUnpositionedBlocks()
    {
        var report = Empty() with
        {
            Blocks = [new(null, Block("missing", 0, 0) with { PhysicalPosition = null })],
            Embedded = [new(new("asset", "a", 1, 1), new("asset", "b", 2, 2))],
            EmbeddedPropertyChanges = [new("asset", "a", "b", new(
                [new("A", new("old"), new("new")), new("B", new("old"), new("new"))], true, [], []))],
            Chunks = [new("1", "2", "chunk")],
            MapName = new("Before", "After"),
            MetadataChanges = [new("map.name", new("Before"), new("After")), new("editor.medal", new(Integer: 100), new(Integer: 90))],
        };

        var scene = DiffInfographic.BuildScene(report, "a", "b");

        Assert.Equal(new DiffInfographicCounts(1, 0, 0), scene.Placements.Counts);
        Assert.Equal(new DiffInfographicCounts(0, 0, 1), scene.Embedded.Counts);
        Assert.Equal(new DiffInfographicDetailCounts(2, 2, 1), scene.DetailCounts);
        Assert.Equal(1, scene.Spatial.UnpositionedChanges);
        Assert.Equal(0, scene.Spatial.PlottedChanges);
        Assert.Equal("Placements", scene.Placements.Title);
        Assert.Equal("Embedded", scene.Embedded.Title);
    }

    [Fact]
    public void BuildScene_MetadataAdditionAndRemovalAreNotCalledModified()
    {
        var report = Empty() with
        {
            Password = new("False", "True"),
            MetadataChanges =
            [
                new("custom.added", null, new("yes")),
                new("custom.removed", new("yes"), null),
            ],
        };

        var scene = DiffInfographic.BuildScene(report, "a", "b");
        var metadata = Assert.Single(scene.Sections, x => x.Id == "metadata");

        Assert.Equal(new DiffInfographicCounts(0, 0, 0), scene.Placements.Counts);
        Assert.Equal(new DiffInfographicCounts(0, 0, 0), scene.Embedded.Counts);
        Assert.Equal(3, scene.DetailCounts.Metadata);
        Assert.Contains(metadata.Lines.Select(x => x.Text), x => x == "+ custom.added: yes");
        Assert.Contains(metadata.Lines.Select(x => x.Text), x => x == "− custom.removed: yes");
        Assert.Contains(metadata.Lines.Select(x => x.Text), x => x == "~ Password present");
        Assert.Contains(metadata.Lines.Select(x => x.Text), x => x == "- False");
        Assert.Contains(metadata.Lines.Select(x => x.Text), x => x == "+ True");
        Assert.All(metadata.Lines, x => Assert.Equal(x.Text.StartsWith('~'), !x.Mono));
    }

    [Fact]
    public void BuildScene_SpatialCoverageSeparatesChangesFromContext()
    {
        var context = Enumerable.Range(0, 905).Select(i => Item($"context-{i}", i, i)).ToArray();
        var report = Empty() with
        {
            Items =
            [
                new(null, Item("valid", 1, 1)),
                new(null, Item("invalid", double.NaN, 2)),
            ],
            Blocks = [new(null, Block("unpositioned", 0, 0) with { PhysicalPosition = null })],
            LeftItemSnapshots = context.Append(Item("invalid-context", double.PositiveInfinity, 0)).ToArray(),
        };

        var scene = DiffInfographic.BuildScene(report, "a", "b");

        Assert.Equal(1, scene.Spatial.PlottedChanges);
        Assert.Equal(0, scene.Spatial.SampledOutChanges);
        Assert.Equal(1, scene.Spatial.InvalidChangePositions);
        Assert.Equal(1, scene.Spatial.UnpositionedChanges);
        Assert.Equal(900, scene.Spatial.PlottedContext);
        Assert.Equal(5, scene.Spatial.SampledOutContext);
        Assert.Equal(1, scene.Spatial.InvalidContextPositions);
        Assert.Contains(scene.Warnings, x => x.Contains("5 context points sampled out", StringComparison.Ordinal));
        Assert.Contains(scene.Warnings, x => x.Contains("1 invalid change position", StringComparison.Ordinal));
        Assert.Contains(scene.Warnings, x => x.Contains("1 invalid context position", StringComparison.Ordinal));
        Assert.Contains(scene.Warnings, x => x.Contains("1 unpositioned change", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildScene_NormalizesExtremeAndEqualHugePositionsToFiniteUnitCoordinates()
    {
        var report = Empty() with
        {
            Items =
            [
                new(null, Item("min", -double.MaxValue, -double.MaxValue)),
                new(null, Item("max", double.MaxValue, double.MaxValue)),
                new(null, Item("equal", double.MaxValue, double.MaxValue)),
            ],
        };

        var scene = DiffInfographic.BuildScene(report, "a", "b");

        Assert.Equal(0, scene.Spatial.Changes.Min(x => x.X));
        Assert.Equal(1, scene.Spatial.Changes.Max(x => x.X));
        Assert.Equal(0, scene.Spatial.Changes.Min(x => x.Z));
        Assert.Equal(1, scene.Spatial.Changes.Max(x => x.Z));
        Assert.All(scene.Spatial.Changes, point =>
        {
            Assert.True(float.IsFinite(point.X));
            Assert.True(float.IsFinite(point.Z));
            Assert.InRange(point.X, 0, 1);
            Assert.InRange(point.Z, 0, 1);
        });
    }

    [Fact]
    public void BuildScene_SpatialRangeAndCoverageLabelsStayBoundedWhenMeasured()
    {
        var report = Empty() with
        {
            Items = [new(null, Item("min", -double.MaxValue, -double.MaxValue)), new(null, Item("max", double.MaxValue, double.MaxValue))],
        };

        var scene = DiffInfographic.BuildScene(report, "a", "b");
        var rangeFont = MonoTestFont(15);
        var coverageFont = MonoTestFont(15, bold: true);
        var fittedCoverage = DiffInfographicText.Fit(scene.Spatial.CoverageLabel, coverageFont, 440);
        var coverageWidth = SixLabors.Fonts.TextMeasurer.MeasureAdvance(fittedCoverage, new SixLabors.Fonts.TextOptions(coverageFont)).Width;
        var fittedRange = DiffInfographicText.Fit(scene.Spatial.RangeLabel, rangeFont, 1200 - coverageWidth - 32);
        var rangeWidth = SixLabors.Fonts.TextMeasurer.MeasureAdvance(fittedRange, new SixLabors.Fonts.TextOptions(rangeFont)).Width;

        Assert.True(rangeWidth + coverageWidth + 32 <= 1200.5f);
    }

    private static SixLabors.Fonts.Font MonoTestFont(float size, bool bold = false)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "GbxSizeTree", "Resources", "Fonts", bold
            ? "JetBrainsMono-Bold.ttf" : "JetBrainsMono-Regular.ttf");
        var collection = new SixLabors.Fonts.FontCollection();
        return collection.Add(path).CreateFont(size);
    }

    [Fact]
    public void MonoFontResources_AreEmbeddedJetBrainsMono()
    {
        Assert.Equal("JetBrains Mono", DiffInfographicFonts.Mono(15).Family.Name);
        Assert.Equal("JetBrains Mono", DiffInfographicFonts.MonoBold(15).Family.Name);
    }

    [Fact]
    public void BuildScene_MetadataLinesAreRenderedMonospace()
    {
        var report = Empty() with
        {
            MapUid = new("u5byRl2QnqZ6a1e_YumY._6plk", "36ROAOA.O5tyi7744S_L9xyQ1k"),
            MetadataChanges = [new("editor.version", new(Integer: 100), new(Integer: 101))],
        };

        var scene = DiffInfographic.BuildScene(report, "a", "b");
        var metadata = Assert.Single(scene.Sections, x => x.Id == "metadata");

        Assert.Equal(
            ["~ Map UID", "- u5byRl2QnqZ6a1e_YumY._6plk", "+ 36ROAOA.O5tyi7744S_L9xyQ1k", "~ editor.version", "- 100", "+ 101"],
            metadata.Lines.Select(x => x.Text));
        Assert.False(metadata.Lines[0].Mono);
        Assert.True(metadata.Lines[1].Mono);
        Assert.True(metadata.Lines[2].Mono);
    }

    [Fact]
    public void BuildScene_ProseSectionLinesAreNotMarkedMonospace()
    {
        var scene = DiffInfographic.BuildScene(Empty() with { Chunks = [new("old", "new", "chunk")] }, "a", "b");
        var chunks = Assert.Single(scene.Sections, x => x.Id == "chunks");

        Assert.All(chunks.Lines, x => Assert.False(x.Mono));
    }

    [Fact]
    public void BuildScene_PlacementSummaryGroupsByKindAndNameAndSortsCountDescThenName()
    {
        var report = Empty() with
        {
            Blocks =
            [
                new(null, Block("PlatformTechInvisible", 0, 0)),
                new(null, Block("PlatformTechInvisible", 32, 0)),
                new(null, Block("PlatformTechInvisible", 64, 0)),
                new(null, Block("PlatformTechInvisible", 96, 0)),
                new(null, Block("PlatformTechInvisible", 128, 0)),
                new(Block("Zed", 160, 0), null),
                new(Block("Zed", 192, 0), null),
                new(Block("Mood", 224, 0), Block("Mood", 256, 0)),
                new(Block("Mood", 288, 0), Block("Mood", 320, 0)),
                new(Block("Mood", 352, 0), Block("Mood", 384, 0)),
            ],
            Items =
            [
                new(null, Item("Items/BF2/speq/bf2 special.Item.Gbx", 0, 32)),
                new(null, Item("Items/BF2/speq/bf2 special.Item.Gbx", 32, 32)),
            ],
        };

        var scene = DiffInfographic.BuildScene(report, "a", "b");
        var summary = Assert.Single(scene.Sections, x => x.Id == "placement-summary");

        Assert.Equal(
            ["+ 5x PlatformTechInvisible", "~ 3x Mood", "+ 2x Items/BF2/speq/bf2 special.Item.Gbx", "− 2x Zed"],
            summary.Lines.Select(x => x.Text));
        Assert.Equal("Placement summary · 4 names", summary.Title);
        Assert.All(summary.Lines, x => Assert.True(x.Mono));
    }

    [Fact]
    public void BuildScene_EmptyDiffHasNoPlacementSummarySection()
    {
        var scene = DiffInfographic.BuildScene(Empty(), "", "");

        Assert.DoesNotContain(scene.Sections, x => x.Id == "placement-summary");
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
        var allText = string.Join('\n', scene.Sections.SelectMany(x => x.Lines.Select(line => line.Text)));

        Assert.DoesNotContain("Scale", allText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("13", allText);
        Assert.Contains("non-additive", allText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("trial budget exhausted", allText);
        Assert.Equal(1, scene.Placements.Counts.Added);
        Assert.Equal(1, scene.Placements.Counts.Removed);
    }

    [Fact]
    public void BuildScene_EmptyDiffUsesMinimumCanvasAndNoDetails()
    {
        var scene = DiffInfographic.BuildScene(Empty(), "", "");

        Assert.InRange(scene.Height, DiffInfographic.MinimumHeight, int.MaxValue);
        Assert.Equal(new DiffInfographicCounts(0, 0, 0), scene.Placements.Counts);
        Assert.Equal(new DiffInfographicCounts(0, 0, 0), scene.Embedded.Counts);
        Assert.Equal(0, scene.Spatial.PlottedChanges);
        Assert.Equal(["hero", "change-counts", "spatial-context"], scene.Sections.Select(x => x.Id));
    }

    [Fact]
    public void BuildScene_TypedLegacyMetadataIsKeptWhenLegacyProjectionIsAbsent()
    {
        var report = Empty() with { MetadataChanges = [new("map.name", new("Before"), new("After"))] };

        var scene = DiffInfographic.BuildScene(report, "a", "b");
        var metadata = Assert.Single(scene.Sections, x => x.Id == "metadata");

        Assert.Equal(1, scene.DetailCounts.Metadata);
        Assert.Equal(["~ map.name", "- Before", "+ After"], metadata.Lines.Select(x => x.Text));
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

        Assert.Equal(2, scene.DetailCounts.Metadata);
        Assert.Equal(new DiffInfographicCounts(0, 0, 0), scene.Placements.Counts);
        Assert.Equal(new DiffInfographicCounts(0, 0, 0), scene.Embedded.Counts);
        Assert.Equal(6, metadata.Lines.Count);
        Assert.Single(metadata.Lines, x => x.Text.StartsWith("~ Map name", StringComparison.Ordinal));
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
