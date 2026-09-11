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
    public void BuildScene_SelectsLargestEmbeddedDeltasThenSortsHighlightsByOrdinalPath()
    {
        var report = Empty() with
        {
            Embedded =
            [
                new(null, new("Z/tiny.Item.Gbx", "new", 1, 1)),
                new(null, new("E/fifth.Item.Gbx", "new", 500, 500)),
                new(new("C/third.Item.Gbx", "old", 800, 800), new("C/third.Item.Gbx", "new", 100, 100)),
                new(null, new("A/first.Item.Gbx", "new", 900, 900)),
                new(new("F/sixth.Item.Gbx", "old", 400, 400), null),
                new(new("B/second.Item.Gbx", "old", 800, 800), null),
                new(null, new("D/fourth.Item.Gbx", "new", 600, 600)),
            ],
            EmbeddedContributions = [new(null, new("contribution-only.Item.Gbx", 1, 1, 1, null))],
        };

        var scene = DiffInfographic.BuildScene(report, "a", "b");
        var highlights = Assert.Single(scene.Sections, x => x.Id == "embedded-highlights");

        var rows = Assert.IsAssignableFrom<IReadOnlyList<DiffInfographicTableRow>>(highlights.Table!.Rows);
        Assert.Equal(
            ["A/first.Item.Gbx", "B/second.Item.Gbx", "C/third.Item.Gbx", "D/fourth.Item.Gbx", "E/fifth.Item.Gbx"],
            rows.Select(x => x.Path));
        Assert.Equal(["+900 B", "−800 B", "−700 B", "+600 B", "+500 B"], rows.Select(x => x.Value));
        Assert.Equal(["+", "−", "~", "+", "+"], rows.Select(x => x.Marker));
        Assert.Equal(
            [DiffInfographicChangeKind.Added, DiffInfographicChangeKind.Removed, DiffInfographicChangeKind.Changed, DiffInfographicChangeKind.Added, DiffInfographicChangeKind.Added],
            rows.Select(x => x.Kind));
        Assert.DoesNotContain(rows, x => x.Path.Contains("contribution", StringComparison.Ordinal));
        Assert.DoesNotContain("ZIP", string.Join(' ', rows.SelectMany(x => new[] { x.Marker, x.Path, x.Value })), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["+ 2 more embedded changes omitted", "Outer-map marginal measurements are non-additive and are never summed."], highlights.Lines.Select(x => x.Text));
    }

    [Fact]
    public void BuildScene_ListsEveryEmbeddedChangeCompactlyInOrdinalPathOrder()
    {
        var changes = Enumerable.Range(0, 18)
            .Select(i => (i % 3) switch
            {
                0 => new ValueChange<EmbeddedSnapshot>(null, new($"Z/{17 - i:D2}-added.Item.Gbx", "new", i + 10, i + 100)),
                1 => new ValueChange<EmbeddedSnapshot>(new($"A/{17 - i:D2}-removed.Item.Gbx", "old", i + 20, i + 200), null),
                _ => new ValueChange<EmbeddedSnapshot>(
                    new($"M/{17 - i:D2}-modified.Item.Gbx", "old", i + 30, i + 300),
                    new($"M/{17 - i:D2}-modified.Item.Gbx", "new", i + 35, i + 305)),
            })
            .ToArray();
        var scene = DiffInfographic.BuildScene(Empty() with { Embedded = changes }, "a", "b");

        var highlights = Assert.Single(scene.Sections, x => x.Id == "embedded-highlights");
        var complete = Assert.Single(scene.Sections, x => x.Id == "embedded-changes");
        var rows = Assert.IsType<DiffInfographicTable>(complete.Table).Rows;

        Assert.Equal(DiffInfographicTableDensity.Standard, highlights.Table!.Density);
        Assert.Equal(DiffInfographicTableDensity.Compact, complete.Table!.Density);
        Assert.True(DiffInfographicPainter.TableFont(complete.Table.Density).Size < DiffInfographicPainter.TableFont(highlights.Table.Density).Size);
        Assert.Equal(changes.Length, rows.Count);
        Assert.Equal(rows.Select(x => x.Path).Order(StringComparer.Ordinal), rows.Select(x => x.Path));
        Assert.Equal(6, rows.Count(x => x.Kind == DiffInfographicChangeKind.Added && x.Marker == "+"));
        Assert.Equal(6, rows.Count(x => x.Kind == DiffInfographicChangeKind.Removed && x.Marker == "−"));
        Assert.Equal(6, rows.Count(x => x.Kind == DiffInfographicChangeKind.Changed && x.Marker == "~"));
        Assert.All(rows.Where(x => x.Kind == DiffInfographicChangeKind.Added), x => Assert.StartsWith("+", x.Value, StringComparison.Ordinal));
        Assert.All(rows.Where(x => x.Kind == DiffInfographicChangeKind.Removed), x => Assert.StartsWith("−", x.Value, StringComparison.Ordinal));
        Assert.All(rows.Where(x => x.Kind == DiffInfographicChangeKind.Changed), x => Assert.Equal("+5 B", x.Value));
        Assert.Empty(complete.Lines);
        Assert.DoesNotContain("omitted", complete.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(complete.Lines, x => x.Text.Contains("omitted", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            DiffInfographicLayout.SectionHeight(complete.Lines, complete.Table, complete.Right - complete.Left),
            complete.Bottom - complete.Top);
        Assert.True(complete.Bottom <= scene.Height - 36);
    }

    [Fact]
    public void BuildScene_FlowsLargeCompleteListAcrossPanelsWithoutOmittingRows()
    {
        var changes = Enumerable.Range(0, 633)
            .Select(i => new ValueChange<EmbeddedSnapshot>(null, new($"Items/{632 - i:D3}.Item.Gbx", "new", i, i)))
            .ToArray();

        var scene = DiffInfographic.BuildScene(Empty() with { Embedded = changes }, "a", "b");
        var complete = scene.Sections.Where(x => x.Id == "embedded-changes" || x.Id.StartsWith("embedded-changes-", StringComparison.Ordinal)).ToArray();
        var rows = complete.SelectMany(x => x.Table!.Rows).ToArray();

        Assert.Equal(3, complete.Length);
        Assert.Equal("embedded-changes", complete[0].Id);
        Assert.Equal("embedded-changes-2", complete[1].Id);
        Assert.Equal("embedded-changes-3", complete[2].Id);
        Assert.Equal("All embedded changes · 633", complete[0].Title);
        Assert.Equal("All embedded changes · 633 · continued 2/3", complete[1].Title);
        Assert.All(complete, x => Assert.InRange(x.Table!.Rows.Count, 1, 300));
        Assert.Equal(changes.Length, rows.Length);
        Assert.Equal(changes.Length, rows.Select(x => x.Path).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(rows.Select(x => x.Path).Order(StringComparer.Ordinal), rows.Select(x => x.Path));
        Assert.All(complete, x => Assert.Equal(DiffInfographicTableDensity.Compact, x.Table!.Density));
        var embeddedAccent = DiffInfographicPainter.DetailAccent("embedded-changes").ToPixel<Rgba32>();
        Assert.All(complete, x => Assert.Equal(embeddedAccent, DiffInfographicPainter.DetailAccent(x.Id).ToPixel<Rgba32>()));
        Assert.True(scene.Height <= 16_383);
        Assert.All(complete, x => Assert.True(x.Bottom <= scene.Height - 36));
    }

    [Fact]
    public void HighlightPath_ElidesIntermediateFoldersToFitAndKeepsFileName()
    {
        var font = MonoTestFont(16);

        var path = DiffInfographicPainter.ElideTablePath(
            "Items/Environment/Stadium/VeryLongCollection/bf2 Item.Gbx", font, 250);

        Assert.Equal("Items/.../bf2 Item.Gbx", path);
        Assert.True(TextWidth(path, font) <= 250.5f);
    }

    [Fact]
    public void HighlightPath_KeepsBothEndsOfTheFileNameWhenNoFolderElisionCanFit()
    {
        var font = MonoTestFont(16);
        var name = "absurdly-long-custom-item-name-that-never-fits-anywhere.Item.Gbx";

        var folderless = DiffInfographicPainter.ElideTablePath(name, font, 260);
        var nested = DiffInfographicPainter.ElideTablePath($"Items/Environment/Stadium/{name}", font, 260);

        Assert.Equal(folderless, nested);
        foreach (var elided in new[] { folderless, nested })
        {
            Assert.True(TextWidth(elided, font) <= 260.5f);
            Assert.Contains("…", elided, StringComparison.Ordinal);
            Assert.EndsWith(".Item.Gbx", elided, StringComparison.Ordinal);
            Assert.StartsWith("absurdly", elided, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void HighlightPath_DropsFoldersWithoutElidingAFileNameThatFits()
    {
        var font = MonoTestFont(16);
        const string name = "CustomRockFormationLargeMossy_v3.Item.Gbx";
        var width = TextWidth(name, font) + .5f;

        var path = DiffInfographicPainter.ElideTablePath($"Items/Environment/Stadium/{name}", font, width);

        Assert.Equal(name, path);
    }

    [Fact]
    public void HighlightTable_ReservesMeasuredRoomForExtremeSizeChangesSoPathsNeverOverlap()
    {
        var font = MonoTestFont(16);
        DiffInfographicTableRow[] extreme =
        [
            new("+", "Items/Environment/Stadium/Collection/first.Item.Gbx", "+9223372036.85 GB", DiffInfographicChangeKind.Added),
            new("−", "Items/Environment/Stadium/Collection/second.Item.Gbx", "−9223372036.85 GB", DiffInfographicChangeKind.Removed),
        ];
        DiffInfographicTableRow[] small = [new("+", "a.Item.Gbx", "+1 B", DiffInfographicChangeKind.Added)];

        var columns = DiffInfographicPainter.MeasureTableColumns(extreme, 70, 690);
        var smallColumns = DiffInfographicPainter.MeasureTableColumns(small, 70, 690);

        Assert.True(columns.PathX + columns.PathWidth <= columns.ValueLeft);
        Assert.True(columns.PathWidth > 0);
        Assert.True(smallColumns.PathWidth > columns.PathWidth, "a narrower value column must hand its room back to the path");
        Assert.All(extreme, row =>
        {
            Assert.True(columns.ValueRight - TextWidth(row.Value, font) >= columns.ValueLeft - .5f);
            Assert.True(TextWidth(DiffInfographicPainter.ElideTablePath(row.Path, font, columns.PathWidth), font) <= columns.PathWidth + .5f);
        });
    }

    [Fact]
    public void BuildScene_ContributionEntriesRemainASeparateBoundedNote()
    {
        var contributions = Enumerable.Range(0, 22).Select(i => new ValueChange<GbxSizeTree.Measure.EmbeddedFileContribution>(null,
            new($"contribution-{i}", 1, 1, null, $"unavailable-{i}"))).ToArray();
        var scene = DiffInfographic.BuildScene(Empty() with { EmbeddedContributions = contributions }, "a", "b");
        var highlights = Assert.Single(scene.Sections, x => x.Id == "embedded-highlights");

        Assert.Empty(highlights.Table!.Rows);
        Assert.Equal(["Marginal measurements are non-additive; 22 unavailable contribution sides: unavailable-0"], highlights.Lines.Select(x => x.Text));
    }

    [Fact]
    public void BuildScene_ContributionNoteCountsUnavailableLeftAndRightSides()
    {
        var contributions = Enumerable.Range(0, 5).Select(i => new ValueChange<GbxSizeTree.Measure.EmbeddedFileContribution>(
            new($"contribution-{i}", 1, 1, i == 0 ? null : 1, i == 0 ? "left unavailable" : null),
            new($"contribution-{i}", 1, 1, i == 1 ? null : 1, i == 1 ? "right unavailable" : null))).ToArray();
        var scene = DiffInfographic.BuildScene(Empty() with { EmbeddedContributions = contributions }, "a", "b");
        var highlights = Assert.Single(scene.Sections, x => x.Id == "embedded-highlights");

        Assert.Equal(["Marginal measurements are non-additive; 2 unavailable contribution sides: left unavailable"], highlights.Lines.Select(x => x.Text));
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
        // The shared layout mirrors the painter's measured wrapping and table consumption.
        Assert.All(detailSections, section =>
        {
            var expectedHeight = DiffInfographicLayout.SectionHeight(
                section.Lines, section.Table, section.Right - section.Left);
            Assert.Equal(expectedHeight, section.Bottom - section.Top);
        });
        Assert.All(detailSections, section => Assert.True(section.Bottom <= scene.Height - 36));
        Assert.Equal(5, Assert.Single(detailSections, x => x.Id == "embedded-highlights").Table!.Rows.Count);
        Assert.Contains("+ 5 more deep-property details omitted", Assert.Single(detailSections, x => x.Id == "deep-properties").Lines.Select(x => x.Text));
        var metadata = Assert.Single(detailSections, x => x.Id == "metadata");
        Assert.Equal(24, metadata.Lines.Count);
        Assert.DoesNotContain(metadata.Lines.Select(x => x.Text), x => x.Contains("omitted", StringComparison.Ordinal));
        Assert.Contains("+ 5 more chunk observations omitted", Assert.Single(detailSections, x => x.Id == "chunks").Lines.Select(x => x.Text));
        Assert.Equal(scene.Warnings, coverage.Lines.Select(x => x.Text));
        Assert.Equal("Coverage notes: 10 below", DiffInfographicPainter.SpatialFooterLabel(scene));
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
        Assert.Contains(metadata.Lines.Select(x => x.Text), x => x == "− False");
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
    public void BuildScene_ViewportUsesEveryFiniteChangeBeforeSamplingAndNeverContext()
    {
        var changes = Enumerable.Range(0, 430)
            .Select(i => new ValueChange<ItemSnapshot>(null, Item($"change-{i}", i == 429 ? 10_000 : 100 + i % 21, 200 + i % 11)))
            .ToArray();
        var report = Empty() with
        {
            Items = changes,
            LeftItemSnapshots = [Item("far-left-context", -50_000, -40_000), Item("far-right-context", 70_000, 80_000)],
        };

        var scene = DiffInfographic.BuildScene(report, "a", "b");
        var viewport = Assert.IsType<DiffInfographicViewport>(scene.Spatial.Viewport);

        Assert.Equal(400, scene.Spatial.PlottedChanges);
        Assert.Equal(30, scene.Spatial.SampledOutChanges);
        Assert.True(viewport.MaxX >= 10_000);
        Assert.True(viewport.MinX > -50_000);
        Assert.True(viewport.MaxZ < 80_000);
        Assert.All(scene.Spatial.Changes, point =>
        {
            Assert.InRange(point.X, 0, 1);
            Assert.InRange(point.Z, 0, 1);
        });
        Assert.Equal(2, scene.Spatial.EdgePinnedContext);
        Assert.Equal(2, scene.Spatial.Context.Count(point => point.EdgePinned));
        Assert.Contains(scene.Warnings, x => x.Contains("outside the padded change viewport", StringComparison.Ordinal));
        Assert.DoesNotContain(scene.Warnings, x => x.Contains("percentile", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildScene_ChangeViewportHasBoundedPaddingSquareMetreSpanAndExplicitTicks()
    {
        var scene = DiffInfographic.BuildScene(Empty() with
        {
            Items =
            [
                new(null, Item("south-west", 100, 200)),
                new(null, Item("north-east", 120, 210)),
            ],
        }, "a", "b");
        var viewport = Assert.IsType<DiffInfographicViewport>(scene.Spatial.Viewport);

        Assert.Equal(92, viewport.MinX);
        Assert.Equal(128, viewport.MaxX);
        Assert.Equal(187, viewport.MinZ);
        Assert.Equal(223, viewport.MaxZ);
        Assert.Equal(viewport.MaxX - viewport.MinX, viewport.MaxZ - viewport.MinZ, 10);
        Assert.Equal("X 92.0 m", viewport.XMinimumLabel);
        Assert.Equal("X 128.0 m", viewport.XMaximumLabel);
        Assert.Equal("Z 187.0 m", viewport.ZMinimumLabel);
        Assert.Equal("Z 223.0 m", viewport.ZMaximumLabel);
        Assert.Equal("JetBrains Mono", DiffInfographicPainter.SpatialAxisFont.Family.Name);
        Assert.Equal(DiffInfographicPainter.SpatialPlotBounds.Width, DiffInfographicPainter.SpatialPlotBounds.Height);
        var labels = DiffInfographicPainter.MeasureSpatialAxisLabels(viewport);
        Assert.Equal(14, labels.XMinimum.Font.Size);
        Assert.Equal(14, labels.XMaximum.Font.Size);
        Assert.Equal(14, labels.ZMinimum.Font.Size);
        Assert.Equal(14, labels.ZMaximum.Font.Size);
    }

    [Fact]
    public void BuildScene_AsymmetricExtremeViewportKeepsEqualSafeHalfSpansAndContainsChanges()
    {
        var positions = new[]
        {
            (X: -double.MaxValue, Z: double.MaxValue / 2),
            (X: 0d, Z: double.MaxValue),
        };
        var scene = DiffInfographic.BuildScene(Empty() with
        {
            Items = positions.Select((position, i) =>
                new ValueChange<ItemSnapshot>(null, Item($"extreme-{i}", position.X, position.Z))).ToArray(),
        }, "a", "b");
        var viewport = Assert.IsType<DiffInfographicViewport>(scene.Spatial.Viewport);

        var xHalfSpan = viewport.MaxX / 2 - viewport.MinX / 2;
        var zHalfSpan = viewport.MaxZ / 2 - viewport.MinZ / 2;
        Assert.Equal(xHalfSpan, zHalfSpan);
        Assert.All(positions, position =>
        {
            Assert.InRange(position.X, viewport.MinX, viewport.MaxX);
            Assert.InRange(position.Z, viewport.MinZ, viewport.MaxZ);
        });
        Assert.Equal(0, scene.Spatial.EdgePinnedChanges);
    }

    [Fact]
    public void BuildScene_HighOffsetNarrowViewportHasDistinctBoundedEndpointLabels()
    {
        var scene = DiffInfographic.BuildScene(Empty() with
        {
            Items =
            [
                new(null, Item("south-west", 10_000_000_000, 20_000_000_000)),
                new(null, Item("north-east", 10_000_000_020, 20_000_000_020)),
            ],
        }, "a", "b");
        var viewport = Assert.IsType<DiffInfographicViewport>(scene.Spatial.Viewport);

        Assert.NotEqual(viewport.MinX, viewport.MaxX);
        Assert.NotEqual(viewport.MinZ, viewport.MaxZ);
        Assert.NotEqual(viewport.XMinimumLabel, viewport.XMaximumLabel);
        Assert.NotEqual(viewport.ZMinimumLabel, viewport.ZMaximumLabel);
        Assert.All(new[]
        {
            viewport.XMinimumLabel,
            viewport.XMaximumLabel,
            viewport.ZMinimumLabel,
            viewport.ZMaximumLabel,
        }, label => Assert.InRange(label.Length, 1, 32));
    }

    [Fact]
    public void SpatialAxisLabels_ExtremeOffsetDrawBoundsFitWithoutXOverlap()
    {
        var scene = DiffInfographic.BuildScene(Empty() with
        {
            Items = [new(null, Item("singleton", 1e300, -1e300))],
        }, "a", "b");
        var viewport = Assert.IsType<DiffInfographicViewport>(scene.Spatial.Viewport);
        var labels = DiffInfographicPainter.MeasureSpatialAxisLabels(viewport);
        var plot = DiffInfographicPainter.SpatialPlotBounds;

        var xMinimum = DrawBounds(labels.XMinimum);
        var xMaximum = DrawBounds(labels.XMaximum);
        var zMinimum = DrawBounds(labels.ZMinimum);
        var zMaximum = DrawBounds(labels.ZMaximum);
        Assert.InRange(xMinimum.Left, plot.Left, plot.Right);
        Assert.InRange(xMinimum.Right, plot.Left, plot.Right);
        Assert.InRange(xMaximum.Left, plot.Left, plot.Right);
        Assert.InRange(xMaximum.Right, plot.Left, plot.Right);
        Assert.True(xMinimum.Right + 12 <= xMaximum.Left);
        Assert.InRange(zMinimum.Left, 0, plot.X);
        Assert.InRange(zMinimum.Right, 0, plot.X);
        Assert.InRange(zMaximum.Left, 0, plot.X);
        Assert.InRange(zMaximum.Right, 0, plot.X);
        Assert.InRange(labels.XMinimum.Font.Size, 10, 14);
    }

    [Fact]
    public void BuildScene_NoFinitePositionedChangesDoesNotFabricateViewportOrPlotContext()
    {
        var report = Empty() with
        {
            Items = [new(null, Item("invalid", double.NaN, 1))],
            Blocks = [new(null, Block("unpositioned", 0, 0) with { PhysicalPosition = null })],
            LeftItemSnapshots = [Item("context-a", 10, 20), Item("context-b", 30, 40)],
        };

        var scene = DiffInfographic.BuildScene(report, "a", "b");

        Assert.Null(scene.Spatial.Viewport);
        Assert.Empty(scene.Spatial.Changes);
        Assert.Empty(scene.Spatial.Context);
        Assert.Equal(0, scene.Spatial.PlottedContext);
        Assert.Equal(2, scene.Spatial.ContextWithoutViewport);
        Assert.Equal("No finite positioned changes · viewport unavailable", scene.Spatial.RangeLabel);
        Assert.Contains(scene.Warnings, x => x.Contains("2 context points not plotted because no finite positioned change defines a viewport", StringComparison.Ordinal));
        Assert.Contains(scene.Warnings, x => x.Contains("1 invalid change position", StringComparison.Ordinal));
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
        var viewport = Assert.IsType<DiffInfographicViewport>(scene.Spatial.Viewport);
        Assert.True(double.IsFinite(viewport.MinX));
        Assert.True(double.IsFinite(viewport.MaxX));
        Assert.True(double.IsFinite(viewport.MinZ));
        Assert.True(double.IsFinite(viewport.MaxZ));
        Assert.Equal(viewport.MaxX - viewport.MinX, viewport.MaxZ - viewport.MinZ);
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

    private static float TextWidth(string text, SixLabors.Fonts.Font font) =>
        SixLabors.Fonts.TextMeasurer.MeasureAdvance(text, new SixLabors.Fonts.TextOptions(font)).Width;

    private static RectangleF DrawBounds(InfographicAxisLabel label)
    {
        var measured = SixLabors.Fonts.TextMeasurer.MeasureBounds(
            label.Text, new SixLabors.Fonts.TextOptions(label.Font));
        return new(label.Origin.X + measured.X, label.Origin.Y + measured.Y, measured.Width, measured.Height);
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
            ["~ Map UID", "− u5byRl2QnqZ6a1e_YumY._6plk", "+ 36ROAOA.O5tyi7744S_L9xyQ1k", "~ editor.version", "− 100", "+ 101"],
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
    public void BuildScene_CompactsPlacementSummaryToRenderedRowsBeforeFirstPlacementTable()
    {
        var names = Enumerable.Range(0, 21).Select(i => $"Placement {i:D2}").ToArray();
        var report = Empty() with
        {
            Blocks = names.Select((name, i) => new ValueChange<BlockSnapshot>(null, Block(name, i * 32, 0))).ToArray(),
        };

        var scene = DiffInfographic.BuildScene(report, "a", "b");
        var summary = Assert.Single(scene.Sections, x => x.Id == "placement-summary");
        var firstPlacementTable = Assert.Single(scene.Sections, x => x.Id == "ordinary-block-changes");
        var sectionWidth = summary.Right - summary.Left;
        var renderedRowCounts = summary.Lines
            .Select(line => DiffInfographicLayout.WrapLine(line, sectionWidth).Count)
            .ToArray();

        Assert.Equal(21, summary.Lines.Count);
        Assert.All(renderedRowCounts, count => Assert.Equal(1, count));
        Assert.Equal(
            summary.Top + DiffInfographicLayout.SectionHeader
                + (renderedRowCounts.Sum() * DiffInfographicLayout.WrappedRowHeight)
                + (summary.Lines.Count * DiffInfographicLayout.LineBottomGap)
                + DiffInfographicLayout.SectionBottomPadding,
            summary.Bottom);

        var lastRenderedRowBottom = summary.Top + DiffInfographicLayout.SectionHeader
            + (renderedRowCounts.Sum() * DiffInfographicLayout.WrappedRowHeight)
            + ((summary.Lines.Count - 1) * DiffInfographicLayout.LineBottomGap);
        Assert.Equal(
            DiffInfographicLayout.LineBottomGap
                + DiffInfographicLayout.SectionBottomPadding
                + DiffInfographicLayout.SectionGap,
            firstPlacementTable.Top - lastRenderedRowBottom);
    }

    [Fact]
    public void BuildScene_PlacementSummaryReservesBothRowsForWrappedGroupedName()
    {
        var wrappingName = "ZZZ " + string.Join(' ', Enumerable.Repeat("long-placement-name", 24));
        var report = Empty() with
        {
            Blocks =
            [
                new(null, Block(wrappingName, 0, 0)),
                new(null, Block(wrappingName, 32, 0)),
            ],
        };

        var scene = DiffInfographic.BuildScene(report, "a", "b");
        var summary = Assert.Single(scene.Sections, x => x.Id == "placement-summary");
        var firstPlacementTable = Assert.Single(scene.Sections, x => x.Id == "ordinary-block-changes");
        var line = Assert.Single(summary.Lines);
        var wrappedRows = DiffInfographicLayout.WrapLine(line, summary.Right - summary.Left);

        Assert.StartsWith("+ 2x ZZZ", line.Text, StringComparison.Ordinal);
        Assert.Equal(2, wrappedRows.Count);
        Assert.Equal(
            DiffInfographicLayout.SectionHeader
                + DiffInfographicLayout.LineBlockHeight(wrappedRows.Count)
                + DiffInfographicLayout.SectionBottomPadding,
            summary.Bottom - summary.Top);
        Assert.Equal(summary.Bottom + DiffInfographicLayout.SectionGap, firstPlacementTable.Top);
    }

    [Fact]
    public void BuildScene_ListsEveryAddedAndRemovedPlacementInSeparateSpatiallyOrderedTables()
    {
        var ordinaryModified = new ValueChange<BlockSnapshot>(
            Block("Ordinary modified", 640, 0), Block("Ordinary modified", 672, 0));
        var bakedModified = new ValueChange<BlockSnapshot>(
            Block("Baked modified", 640, 32), Block("Baked modified", 672, 32));
        var itemModified = new ValueChange<ItemSnapshot>(
            Item("Items/Modified.Item.Gbx", 640, 64), Item("Items/Modified.Item.Gbx", 672, 64));
        var normal = Block("Normal added", 0, 0);
        var ghost = Block("Ghost removed", 96, 0) with { IsGhost = true };
        var free = Block("Free added", 192, 0) with
        {
            IsFree = true,
            PhysicalPosition = new(193.25, 4.5, 1.75),
        };
        var tiedAdded = Item("Items/Tied added.Item.Gbx", 96.75, 192.25);
        var tiedRemoved = Item("Items/Tied removed.Item.Gbx", 96.75, 192.25);
        var report = Empty() with
        {
            Blocks =
            [
                ordinaryModified,
                new(null, free),
                new(ghost, null),
                new(null, normal),
            ],
            BakedBlocks =
            [
                bakedModified,
                new(null, Block("Baked added", 0, 96)),
                new(Block("Baked removed", 96, 96), null),
            ],
            Items =
            [
                itemModified,
                new(null, Item("Items/Environment/Very/Long/Added custom item.Item.Gbx", 0.25, 192.5)),
                new(Item("Items/Removed.Item.Gbx", 96.75, 192.25), null),
                new(null, tiedAdded),
                new(tiedRemoved, null),
            ],
        };

        var scene = DiffInfographic.BuildScene(report, "a", "b");
        var summaryIndex = scene.Sections.ToList().FindIndex(x => x.Id == "placement-summary");
        var ordinaryIndex = scene.Sections.ToList().FindIndex(x => x.Id == "ordinary-block-changes");
        var bakedIndex = scene.Sections.ToList().FindIndex(x => x.Id == "baked-block-changes");
        var itemIndex = scene.Sections.ToList().FindIndex(x => x.Id == "item-changes");
        var ordinary = scene.Sections[ordinaryIndex];
        var baked = scene.Sections[bakedIndex];
        var items = scene.Sections[itemIndex];

        Assert.Equal(summaryIndex + 1, ordinaryIndex);
        Assert.Equal(ordinaryIndex + 1, bakedIndex);
        Assert.Equal(bakedIndex + 1, itemIndex);
        Assert.All(new[] { ordinary, baked, items }, section =>
        {
            Assert.Equal(70, section.Left);
            Assert.Equal(1330, section.Right);
            Assert.Equal(DiffInfographicTableDensity.Compact, section.Table!.Density);
            Assert.Equal("+/−", section.Table.MarkerHeader);
            Assert.Equal("POSITION", section.Table.ValueHeader);
        });
        Assert.Equal("NAME", ordinary.Table!.PathHeader);
        Assert.Equal("NAME", baked.Table!.PathHeader);
        Assert.Equal("NAME / PATH", items.Table!.PathHeader);
        Assert.Equal(
            [
                ("+", "Normal added", "--"),
                ("−", "Ghost removed", "--"),
                ("+", "Free added", "(193.25, 4.5, 1.75)"),
            ],
            ordinary.Table!.Rows.Select(x => (x.Marker, x.Path, x.Value)));
        Assert.Equal(
            [("+", "Baked added", "(0.0, 0.0, 96.0)"), ("−", "Baked removed", "(96.0, 0.0, 96.0)")],
            baked.Table!.Rows.Select(x => (x.Marker, x.Path, x.Value)));
        Assert.Equal(
            [
                ("+", "Items/Environment/Very/Long/Added custom item.Item.Gbx", "(0.25, 0.0, 192.5)"),
                ("−", "Items/Removed.Item.Gbx", "(96.75, 0.0, 192.25)"),
                ("+", "Items/Tied added.Item.Gbx", "(96.75, 0.0, 192.25)"),
                ("−", "Items/Tied removed.Item.Gbx", "(96.75, 0.0, 192.25)"),
            ],
            items.Table!.Rows.Select(x => (x.Marker, x.Path, x.Value)));
        Assert.Contains(scene.Sections[summaryIndex].Lines, x => x.Text == "~ 1x Ordinary modified");
        Assert.Contains(scene.Sections[summaryIndex].Lines, x => x.Text == "~ 1x Baked modified");
        Assert.Contains(scene.Sections[summaryIndex].Lines, x => x.Text == "~ 1x Items/Modified.Item.Gbx");
        Assert.DoesNotContain(new[] { ordinary, baked, items }.SelectMany(x => x.Table!.Rows),
            row => row.Kind == DiffInfographicChangeKind.Changed || row.Marker == "~");
    }

    [Fact]
    public void BuildScene_FlowsUnboundedPlacementRowsAcrossFullWidthContinuationPanels()
    {
        var changes = Enumerable.Range(0, 633)
            .Select(i => new ValueChange<ItemSnapshot>(null,
                Item($"Items/Stress/Very/Long/Folder/{i:D3}-custom.Item.Gbx", (632 - i) * 96, i % 5)))
            .Append(new(Item("Items/modified.Item.Gbx", -96, 0), Item("Items/modified.Item.Gbx", -32, 0)))
            .ToArray();

        var scene = DiffInfographic.BuildScene(Empty() with { Items = changes }, "a", "b");
        var complete = scene.Sections
            .Where(x => x.Id == "item-changes" || x.Id.StartsWith("item-changes-", StringComparison.Ordinal))
            .ToArray();
        var rows = complete.SelectMany(x => x.Table!.Rows).ToArray();

        Assert.Equal(3, complete.Length);
        Assert.Equal(["item-changes", "item-changes-2", "item-changes-3"], complete.Select(x => x.Id));
        Assert.Equal("Added / removed items · 633", complete[0].Title);
        Assert.Equal("Added / removed items · 633 · continued 2/3", complete[1].Title);
        Assert.All(complete, section =>
        {
            Assert.Equal(70, section.Left);
            Assert.Equal(1330, section.Right);
            Assert.InRange(section.Table!.Rows.Count, 1, 300);
            Assert.Equal(DiffInfographicTableDensity.Compact, section.Table.Density);
            Assert.Equal(
                DiffInfographicLayout.SectionHeight(section.Lines, section.Table, section.Right - section.Left),
                section.Bottom - section.Top);
            Assert.True(section.Bottom <= scene.Height - 36);
        });
        Assert.Equal(633, rows.Length);
        Assert.Equal(633, rows.Select(x => x.Path).Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(rows, x => x.Kind == DiffInfographicChangeKind.Changed);
        Assert.Equal(rows, DiffSpatialGroups.Order(changes, x => x.PhysicalPosition, x => x.Key)
            .Where(x => x.Change.Left is null || x.Change.Right is null)
            .Select(x => x.Change.Right ?? x.Change.Left!)
            .Select(x => rows.Single(row => row.Path == x.Path)));
    }

    [Fact]
    public void PlacementTable_UsesMeasuredNameElisionAndPositionReservation()
    {
        var font = MonoTestFont(14);
        DiffInfographicTableRow[] rows =
        [
            new("+", "Items/Environment/Stadium/Collection/absurdly-long-custom-item-name-that-never-fits-anywhere.Item.Gbx",
                "(−9223372036854775808, 0.125, 9223372036854775807)", DiffInfographicChangeKind.Added),
        ];
        var columns = DiffInfographicPainter.MeasureTableColumns(rows, 70, 1330, DiffInfographicTableDensity.Compact,
            "POSITION");
        var elided = DiffInfographicPainter.ElideTablePath(rows[0].Path, font, columns.PathWidth);

        Assert.True(columns.PathX + columns.PathWidth <= columns.ValueLeft);
        Assert.True(TextWidth(elided, font) <= columns.PathWidth + .5f);
        Assert.NotEqual(rows[0].Path, elided);
        Assert.True(elided.Contains("...", StringComparison.Ordinal) || elided.Contains('…'));
        Assert.EndsWith(".Item.Gbx", elided, StringComparison.Ordinal);
    }

    [Fact]
    public void Footer_DescribesCompletePlacementDetailWithoutCallingItBounded()
    {
        Assert.DoesNotContain("bounded", DiffInfographicPainter.FooterLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("complete placement", DiffInfographicPainter.FooterLabel, StringComparison.OrdinalIgnoreCase);
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
        Assert.Equal(["~ map.name", "− Before", "+ After"], metadata.Lines.Select(x => x.Text));
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
            Blocks = Enumerable.Range(0, 54).Select(i => (i % 4) switch
            {
                0 => new ValueChange<BlockSnapshot>(null, Block("RoadTechStraight", i * 32, (i % 7) * 32)),
                1 => new ValueChange<BlockSnapshot>(Block("RoadTechCurve", i * 32, (i % 7) * 32), null),
                2 => new ValueChange<BlockSnapshot>(null, Block("FreeDecorationWithAnUnusuallyLongDescriptiveName", i * 32, (i % 5) * 4, (i % 7) * 32) with { IsFree = true }),
                _ => new ValueChange<BlockSnapshot>(Block("OrdinaryModifiedExcludedFromTables", i * 32, (i % 7) * 32),
                    Block("OrdinaryModifiedExcludedFromTables", (i + 1) * 32, (i % 7) * 32)),
            }).ToArray(),
            BakedBlocks = Enumerable.Range(0, 42).Select(i => (i % 3) switch
            {
                0 => new ValueChange<BlockSnapshot>(null, Block("BakedPlatformLongName", i * 24, i % 6, (i % 4) * 48)),
                1 => new ValueChange<BlockSnapshot>(Block("BakedWallLongName", i * 24, i % 6, (i % 4) * 48), null),
                _ => new ValueChange<BlockSnapshot>(Block("BakedModifiedExcludedFromTables", i * 24, i % 6, (i % 4) * 48),
                    Block("BakedModifiedExcludedFromTables", i * 24 + 8, i % 6, (i % 4) * 48)),
            }).ToArray(),
            Items = Enumerable.Range(0, 480).Select(i => (i % 3) switch
            {
                0 => new ValueChange<ItemSnapshot>(null, Item($"Items/Environment/Stadium/VeryLongCollection/{i % 12:D2}-Added custom item with long name.Item.Gbx", (i / 2) * 16, (i % 6) * 24)),
                1 => new ValueChange<ItemSnapshot>(Item($"Items/Environment/Stadium/VeryLongCollection/{i % 12:D2}-Removed custom item with long name.Item.Gbx", (i / 2) * 16, (i % 6) * 24), null),
                _ => new ValueChange<ItemSnapshot>(
                    Item($"Items/Modified/{i % 12:D2}-Excluded.Item.Gbx", i * 16, i * 8),
                    Item($"Items/Modified/{i % 12:D2}-Excluded.Item.Gbx", i * 16 + 4, i * 8 + 4)),
            }).Concat([
                new(null, Item("Items/Coincident/Added at exactly coincident position.Item.Gbx", 2_048.5, 144.25)),
                new(Item("Items/Coincident/Removed at exactly coincident position.Item.Gbx", 2_048.5, 144.25), null),
            ]).ToArray(),
            LeftBlockSnapshots = [Block("Context A", 0, 0), Block("Context B", 160, 120)],
            RightBlockSnapshots = [Block("Context A", 0, 0), Block("Context C", 200, 150)],
            Embedded = Enumerable.Range(0, 24).Select(i => (i % 3) switch
            {
                0 => new ValueChange<EmbeddedSnapshot>(null, new($"Embedded/Items/Lighting/{i:D2}-AmberStrips.Item.Gbx", "new", 184_000 + i, 410_000 + i)),
                1 => new ValueChange<EmbeddedSnapshot>(new($"Embedded/Items/Signs/{i:D2}-OldSponsor.Item.Gbx", "old", 92_000 + i, 230_000 + i), null),
                _ => new ValueChange<EmbeddedSnapshot>(
                    new($"Embedded/Items/Stadium/{i:D2}-Grandstand.Item.Gbx", "old", 412_000 + i, 900_000 + i),
                    new($"Embedded/Items/Stadium/{i:D2}-Grandstand.Item.Gbx", "new", 510_000 + i, 1_020_000 + i)),
            }).ToArray(),
            MapName = new("Night Circuit v1", "Night Circuit v2"),
            EmbeddedPropertyChanges = [new("Grandstand.Item.Gbx", "old", "new", new(
                [new("Item > EntityModel > Prefab > Ent#2 > Solid2Model > Material#1 > Name", new("Concrete"), new("Carbon"))], true, [], []))],
            Warnings = ["One custom chunk remained opaque; file-level change detection is still complete."],
        };
        using (var image = DiffInfographic.Render(synthetic, "Night Circuit — draft.Map.Gbx", "Night Circuit — release.Map.Gbx"))
            image.SaveAsPng(Path.Combine(outputDirectory!, "diff-infographic-placement-stress.png"));

        var localizedWithFarContext = Empty() with
        {
            RightBytes = 1_012_800,
            Items =
            [
                new(null, Item("Localized added", 1_004, 2_006)),
                new(Item("Localized removed", 1_018, 2_011), null),
                new(Item("Localized modified", 1_010, 2_018), Item("Localized modified", 1_014, 2_016)),
            ],
            LeftItemSnapshots = [Item("Far west context", -90_000, 70_000), Item("Near context", 1_000, 2_000)],
            RightItemSnapshots = [Item("Far east context", 120_000, -80_000), Item("Near context", 1_024, 2_024)],
        };
        using (var image = DiffInfographic.Render(localizedWithFarContext, "Localized before.Map.Gbx", "Localized after.Map.Gbx"))
            image.SaveAsPng(Path.Combine(outputDirectory!, "diff-infographic-localized-far-context.png"));

        var noPositionedChanges = Empty() with
        {
            Blocks = [new(null, Block("Unpositioned block", 0, 0) with { PhysicalPosition = null })],
            Items = [new(null, Item("Invalid item", double.NaN, double.PositiveInfinity))],
            LeftItemSnapshots = [Item("Context only A", -500, 700)],
            RightItemSnapshots = [Item("Context only B", 900, -1_100)],
        };
        using (var image = DiffInfographic.Render(noPositionedChanges, "No positions before.Map.Gbx", "No positions after.Map.Gbx"))
            image.SaveAsPng(Path.Combine(outputDirectory!, "diff-infographic-no-positioned-changes.png"));

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
        Block(name, x, 0, z);

    private static BlockSnapshot Block(string name, int x, int y, int z) =>
        new(name, x / 32, y / 8, z / 32, new(x, y, z), null, "North", 0, 0, false, false, true, "Default", "Normal", 0);

    private static ItemSnapshot Item(string path, double x, double z) =>
        new(path, new(x, 0, z), default, "Default", 1, default, "None", "Normal", 0);

    private static DiffReport Empty() => new(1_000_000, 1_000_000, [], [], [], [], [], null, null, null, null, null);
}
