using GBX.NET;
using GBX.NET.Engines.Game;
using GbxSizeTree.Cli.Modes;
using GbxSizeTree.Cli.Output;
using GbxSizeTree.Cli.Rendering;
using Spectre.Console.Testing;

namespace GbxSizeTree.Tests.Rendering;

public sealed class DiffRendererTests
{
    [Fact]
    public void SpatialOrder_KeepsNearbyClusterTogetherInsteadOfSortingByOneAxis()
    {
        var near = new SpatialPosition(1, 0, 1);
        var adjacent = new SpatialPosition(1.1, 0, 1);
        var distant = new SpatialPosition(1.05, 0, 1000);
        var ordered = new[] { distant, adjacent, near }.Order().ToArray();
        Assert.Equal(new[] { near, adjacent, distant }, ordered);
    }

    [Fact]
    public void CompactPath_FollowsDirectoryRuleAndPreservesOtherExtensions()
    {
        Assert.Equal(@"BF2\Gen…\New…\Flat\MiniFlatDirt", DiffRenderer.CompactPath(@"BF2\General\NewQuaterPlatforms\Flat\MiniFlatDirt.Item.Gbx"));
        Assert.Equal("Gen…/asset.Block.Gbx", DiffRenderer.CompactPath("General/asset.Block.Gbx"));
    }

    [Fact]
    public void Render_OrdersAssetsBeforeInstancesAndShowsMarkersAndTotals()
    {
        var report = Empty() with
        {
            Embedded = [new(new("old.Item.Gbx", "old-hash", 10, 20), null), new(null, new("new.Item.Gbx", "new-hash", 20, 40))],
            Items = [new(ItemSnapshot.From(new CGameCtnAnchoredObject { AbsolutePositionInMap = new(1, 2, 3) }), null)],
            Blocks = [new(null, BlockSnapshot.From(new CGameCtnBlock { Name = "block", Coord = new(1, 2, 3) }))],
            MapName = new("Before", "After"),
        };
        var console = new TestConsole().Width(120);
        DiffRenderer.Render(console, report, "old.Map.Gbx", "new.Map.Gbx");

        var output = console.Output;
        Assert.True(output.IndexOf("Embedded files", StringComparison.Ordinal)
            < output.IndexOf("Placed items", StringComparison.Ordinal));
        Assert.True(output.IndexOf("Placed items", StringComparison.Ordinal)
            < output.IndexOf("Blocks", StringComparison.Ordinal));
        Assert.Contains("-", output);
        Assert.Contains("old", output);
        Assert.Contains("+", output);
        Assert.Contains("new", output);
        Assert.Contains("~ Map name", output);
        Assert.Contains("-200", output);
        Assert.Contains("+1 added", output);
        Assert.Contains("-1 removed", output);
        Assert.DoesNotContain("Embedded/items", output);
        Assert.DoesNotContain("\u001b", output);
    }

    [Fact]
    public void Render_EscapesMarkupAndControlCharacters()
    {
        var console = new TestConsole().Width(120);
        var report = Empty() with { Embedded = [new(null, new("[red]asset[/]\u001b[2J\nforged", "hash", 10, 20))] };
        DiffRenderer.Render(console, report, "[old]", "new");
        Assert.Contains("forged", console.Output);
        Assert.Contains("[old]", console.Output);
        Assert.DoesNotContain("\u001b", console.Output);
    }

    [Fact]
    public void Render_IdenticalMapHasExplicitEmptyMessage()
    {
        var console = new TestConsole();
        DiffRenderer.Render(console, Empty() with { RightBytes = 1000 }, "same", "same");
        Assert.Contains("No differences in the compared fields.", console.Output);
        Assert.DoesNotContain("Embedded files", console.Output);
    }

    [Theory]
    [InlineData(null, false, "xterm-256color", false, true)]
    [InlineData(null, true, "xterm-256color", false, false)]
    [InlineData(null, false, "dumb", false, false)]
    [InlineData(null, false, "xterm", true, false)]
    [InlineData(true, true, "dumb", true, true)]
    [InlineData(false, false, "xterm", false, false)]
    public void ColorPolicy_RespectsTerminalEnvironmentAndExplicitOverrides(
        bool? requested, bool redirected, string terminal, bool noColor, bool expected)
    {
        Assert.Equal(expected, DiffRenderer.ShouldUseColor(requested, redirected, terminal, noColor));
    }

    [Fact]
    public void Embedded_ShowsTypedSizesAndTransitionsAcrossFormatsWithoutHashes()
    {
        var report = Empty() with
        {
            Embedded = [new(new("Embedded/items/Folder/asset.Item.Gbx", "old-secret-hash", 20, 100),
                new("Embedded/items/Folder/asset.Item.Gbx", "new-secret-hash", 30, 120))],
        };
        foreach (var output in Outputs(report))
        {
            Assert.Contains("ZIP bytes", output);
            Assert.Contains("Raw bytes", output);
            Assert.Contains("ZIP / raw", output);
            Assert.Contains("20 → 30", output);
            Assert.Contains("100 → 120", output);
            Assert.Contains("20.00% of raw → 25.00% of raw", output);
            Assert.Contains("asset", output);
            Assert.DoesNotContain("secret-hash", output);
            Assert.DoesNotContain("compressed=", output);
        }
    }

    [Theory]
    [InlineData("new-hash", 20, 100, "Content changed (SHA-256); raw and ZIP sizes unchanged")]
    [InlineData("old-hash", 15, 100, "Content unchanged; ZIP encoding size changed (compression-only)")]
    [InlineData("new-hash", 15, 90, "Content changed (SHA-256); raw size changed; ZIP size changed")]
    [InlineData("new-hash", 20, 120, "Content changed (SHA-256); raw size changed; ZIP size unchanged")]
    public void Embedded_ExplainsContentAndCompressionWithoutInferringArchiveSize(string hash, long zip, long raw, string explanation)
    {
        var report = Empty() with
        {
            Embedded = [new(new("asset.bin", "old-hash", 20, 100), new("asset.bin", hash, zip, raw))],
        };
        foreach (var output in Outputs(report))
        {
            var plain = output.Replace("\\", "");
            Assert.Contains(explanation, plain);
            Assert.Contains($"20 → {zip} ({zip - 20:+0;-0;0} B)", plain);
            Assert.Contains($"100 → {raw} ({raw - 100:+0;-0;0} B)", plain);
            Assert.Contains("not total ZIP archive or outer-map size", plain);
            Assert.DoesNotContain("old-hash", plain);
            Assert.DoesNotContain("new-hash", plain);
        }
    }

    [Fact]
    public void Embedded_AddRemoveDeltasUseAbsentAsZeroAndKeepEmptyEntries()
    {
        var report = Empty() with
        {
            Embedded = [new(null, new("added", "hash", 10, 20)), new(new("removed", "hash", 30, 40), null),
                new(null, new("empty", "hash", 0, 0))],
        };
        foreach (var output in Outputs(report))
        {
            var plain = output.Replace("\\", "");
            Assert.Contains("10 (+10 B)", plain);
            Assert.Contains("20 (+20 B)", plain);
            Assert.Contains("30 (-30 B)", plain);
            Assert.Contains("40 (-40 B)", plain);
            Assert.Contains("0 (0 B)", plain);
            Assert.Contains("Entry added", plain);
            Assert.Contains("Entry removed", plain);
        }
    }

    [Fact]
    public void Items_HavePositionRotationColorAndMeaningfulProperties()
    {
        var item = ItemSnapshot.From(new CGameCtnAnchoredObject { AbsolutePositionInMap = new(1.25f, 2, 3), Scale = 2 });
        var report = Empty() with { Items = [new(item, null), new(null, item with { Color = "Blue", Scale = 3, AnimationPhase = "Half" })] };
        foreach (var output in Outputs(report))
        {
            Assert.Contains("Position", output);
            Assert.Contains("Rotation", output);
            Assert.Contains("Color", output);
            Assert.DoesNotContain("Scale", output);
            Assert.Contains("Animation", output);
            Assert.Contains("Blue", output);
            Assert.DoesNotContain("position=", output);
        }
    }

    [Fact]
    public void AllFormats_MixMarkersAndSortNumericallyWithoutTruncationOrWrapping()
    {
        var item = ItemSnapshot.From(new CGameCtnAnchoredObject());
        var report = Empty() with
        {
            Items = [
                new(item with { Path = "high", PhysicalPosition = new(2048, 0, 0) }, null),
                new(null, item with { Path = "near", PhysicalPosition = new(.125, 0, 0) }),
                new(item with { Path = "negative", PhysicalPosition = new(-.25, 0, 0) }, null),
                new(null, item with { Path = "far", PhysicalPosition = new(.5, 0, 0) }),
            ],
        };
        foreach (var output in Outputs(report))
        {
            Assert.True(output.IndexOf("negative", StringComparison.Ordinal) < output.IndexOf("near", StringComparison.Ordinal));
            Assert.True(output.IndexOf("near", StringComparison.Ordinal) < output.IndexOf("far", StringComparison.Ordinal));
            Assert.True(output.IndexOf("far", StringComparison.Ordinal) < output.IndexOf("high", StringComparison.Ordinal));
        }
        Assert.Equal(DiffRenderer.RenderMarkdown(report, "a", "b"),
            DiffRenderer.RenderMarkdown(report with { Items = report.Items.Reverse().ToArray() }, "a", "b"));
    }

    [Fact]
    public void BlocksAndBakedBlocks_ShowGridMidpointsAndFreeTransforms()
    {
        var grid = BlockSnapshot.From(new CGameCtnBlock { Name = "grid", Coord = new(1, 2, 3), IsGhost = true });
        var free = BlockSnapshot.From(new CGameCtnBlock { Name = "free", IsFree = true, AbsolutePositionInMap = new(1.5f, 2.5f, 3.5f), YawPitchRoll = new(.1f, .2f, .3f) });
        var report = Empty() with { Blocks = [new(grid, null), new(null, free)], BakedBlocks = [new(null, grid)] };
        foreach (var output in Outputs(report))
        {
            Assert.Contains("Coord", output);
            Assert.Contains("Pos", output);
            Assert.Contains("Direction", output);
            Assert.Contains("Variant", output);
            Assert.Contains("Subvariant", output);
            Assert.Contains("Ghost", output);
            Assert.Contains("Free", output);
            Assert.Contains("48.0, 20.0, 112.0", output);
            Assert.True(output.IndexOf("free", StringComparison.Ordinal) < output.IndexOf("grid", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void EmptyEmbeddedAndMetadata_AreSafeInAllFormats()
    {
        var report = Empty() with
        {
            Embedded = [new(null, new("<script>|[asset]`\u001b\n", "hash", 0, 0))],
            MapName = new("old", "<script>new</script>"),
        };
        foreach (var output in Outputs(report))
        {
            Assert.Contains("Map name", output);
            Assert.DoesNotContain("\u001b", output);
            Assert.DoesNotContain("NaN", output);
            Assert.DoesNotContain("Infinity", output);
        }
        Assert.DoesNotContain("<script>", DiffRenderer.RenderHtml(report, "a", "b"));
        Assert.DoesNotContain("<script>", DiffRenderer.RenderMarkdown(report, "a", "b"));
    }

    [Theory]
    [InlineData(80)]
    [InlineData(120)]
    public void Render_CategoryTablesFitOrdinaryTerminalWidths(int width)
    {
        var report = Empty() with
        {
            Items = [new(null, ItemSnapshot.From(new CGameCtnAnchoredObject { AbsolutePositionInMap = new(.125f, 1, 2), Scale = 2 }))],
            Blocks = [new(null, BlockSnapshot.From(new CGameCtnBlock { Name = "Road", Coord = new(1, 2, 3) }))],
            Embedded = [new(null, new("Folder/asset.Item.Gbx", "hash", 10, 20))],
        };
        var console = new TestConsole().Width(width);
        DiffRenderer.Render(console, report, "a", "b");
        Assert.Contains("Road", console.Output);
        Assert.Contains("asset.Item.Gbx", console.Output);
        Assert.DoesNotContain("\u001b", console.Output);
    }

    [Fact]
    public void SamePositionTies_AreStableAndNamesWithDelimitersAreNotParsed()
    {
        var item = ItemSnapshot.From(new CGameCtnAnchoredObject());
        var changes = new ValueChange<ItemSnapshot>[]
        {
            new(null, item with { Path = "z|position=(999,999,999).Item.Gbx" }),
            new(item with { Path = "a.Item.Gbx" }, null),
            new(null, item with { Path = "a.Item.Gbx" }),
        };
        var report = Empty() with { Items = changes };
        Assert.Equal(DiffRenderer.RenderHtml(report, "a", "b"),
            DiffRenderer.RenderHtml(report with { Items = changes.Reverse().ToArray() }, "a", "b"));
        Assert.Contains("z|position=(999,999,999)", DiffRenderer.RenderHtml(report, "a", "b"));
    }

    [Fact]
    public void Markdown_EscapesStrikethroughInPathsAndMetadata()
    {
        var report = Empty() with
        {
            Embedded = [new(null, new("a~~removed~~.Item.Gbx", "hash", 1, 2))],
            MapName = new("old", "~~new~~"),
        };
        var output = DiffRenderer.RenderMarkdown(report, "~~left~~", "right");
        Assert.Contains(@"a\~\~removed\~\~.Item.Gbx", output);
        Assert.Contains(@"\~\~new\~\~", output);
        Assert.Contains(@"\~\~left\~\~", output);
    }

    [Theory]
    [InlineData(".")]
    [InlineData(",")]
    public void Transforms_UseOneToThreeInvariantDecimalsWithoutChangingJson(string separator)
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            var culture = (System.Globalization.CultureInfo)System.Globalization.CultureInfo.InvariantCulture.Clone();
            culture.NumberFormat.NumberDecimalSeparator = separator;
            System.Globalization.CultureInfo.CurrentCulture = culture;
            var vector = new SpatialPosition(1, 2.123456789, 3.1);
            var item = ItemSnapshot.From(new CGameCtnAnchoredObject()) with
            {
                PhysicalPosition = vector, Rotation = vector, Pivot = vector, Scale = 2.1234567f,
            };
            var free = BlockSnapshot.From(new CGameCtnBlock { IsFree = true }) with
            {
                PhysicalPosition = vector, Rotation = vector,
            };
            var report = Empty() with
            {
                Items = [new(null, item)], Blocks = [new(null, free)], BakedBlocks = [new(null, free)],
            };
            var json = DiffMode.RenderJson(report);
            foreach (var output in Outputs(report))
            {
                Assert.Equal(7, output.Split("1.0, 2.123, 3.1", StringSplitOptions.None).Length - 1);
                Assert.DoesNotContain("2.123456", output);
                Assert.Contains("2.123", output);
            }
            Assert.Equal(json, DiffMode.RenderJson(report));
            Assert.Contains("2.123456789", json);
            Assert.Contains("2.1234567", json);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Embedded_GroupsAddedRemovedBeforeModifiedAndSortsEachByOrdinalPath()
    {
        var report = Empty() with
        {
            Embedded = [
                new(new("bModified", "old", 1, 2), new("bModified", "new", 2, 3)),
                new(null, new("zAdded", "hash", 1, 2)),
                new(new("aModified", "old", 1, 2), new("aModified", "new", 2, 3)),
                new(new("BRemoved", "hash", 1, 2), null),
            ],
            Items = [new(null, ItemSnapshot.From(new CGameCtnAnchoredObject()))],
        };
        foreach (var output in Outputs(report))
        {
            var names = new[] { "Embedded files — added/removed", "BRemoved", "zAdded", "Embedded files — modified", "aModified", "bModified", "Placed items" };
            var offset = 0;
            foreach (var name in names)
            {
                var index = output.IndexOf(name, offset, StringComparison.Ordinal);
                Assert.True(index >= offset, $"Missing or out-of-order group/row: {name}");
                offset = index + name.Length;
            }
        }
        Assert.Equal(DiffRenderer.RenderHtml(report, "left", "right"),
            DiffRenderer.RenderHtml(report with { Embedded = report.Embedded.Reverse().ToArray() }, "left", "right"));
        foreach (var output in Outputs(report with { Embedded = [report.Embedded[0]] }))
        {
            Assert.Contains("Embedded files — modified", output);
            Assert.DoesNotContain("Embedded files — added/removed", output);
        }
    }

    [Fact]
    public void Html_ExposesStableSemanticSelectorsAndScopedHeadersInBothStyleModes()
    {
        var report = Empty() with
        {
            Embedded = [new(null, new("dir/asset", "hash", 1, 2)),
                new(new("dir/changed", "old", 1, 2), new("dir/changed", "new", 2, 3))],
            Items = [new(null, ItemSnapshot.From(new CGameCtnAnchoredObject()))],
            Blocks = [new(null, BlockSnapshot.From(new CGameCtnBlock { Name = "block" }))],
            BakedBlocks = [new(null, BlockSnapshot.From(new CGameCtnBlock { Name = "baked" }))],
            MetadataChanges = [new("display.comments", null, new(Text: "new"))],
            Chunks = [new(null, "1", "body:a")],
            EmbeddedContributions = [new(null, new("dir/asset", 1, 2, 1, null))],
            Warnings = ["careful"],
            MapName = new("before", "after"),
        };

        foreach (var styled in new[] { false, true })
        {
            var html = DiffRenderer.RenderHtml(report, "old", "new", styled: styled);
            Assert.Contains("<main id=\"map-diff\" class=\"diff-report\">", html);
            Assert.Contains("<header id=\"diff-summary\" class=\"summary diff-summary\">", html);
            Assert.Contains("id=\"diff-warnings\" class=\"warnings diff-warnings\"", html);
            Assert.Contains("id=\"warning-0\" class=\"warning\"", html);
            Assert.Contains("<section id=\"category-embedded-added-removed\" class=\"diff-category category-embedded-added-removed\"", html);
            Assert.Contains("id=\"table-embedded-added-removed\" class=\"diff-table category-table\"", html);
            Assert.Contains("id=\"table-embedded-added-removed-head\" class=\"table-header\"", html);
            Assert.Contains("class=\"table-header-cell column-mark\" scope=\"col\"", html);
            Assert.Contains("id=\"table-embedded-added-removed-group-0\" class=\"row-group\"", html);
            Assert.Contains("id=\"table-embedded-added-removed-row-0\" class=\"diff-row added\"", html);
            Assert.Contains("class=\"diff-cell marker-cell column-mark\" headers=\"table-embedded-added-removed-header-0-mark\"", html);
            Assert.Contains("id=\"diff-metadata\" class=\"metadata diff-metadata\"", html);
            Assert.Contains("id=\"metadata-row-0\" class=\"metadata-row changed\"", html);
            foreach (var category in new[] { "embedded-added-removed", "embedded-modified", "embedded-contributions", "placed-items", "blocks", "baked-blocks", "map-metadata", "chunks" })
            {
                Assert.Contains($"id=\"category-{category}\"", html);
                Assert.Contains($"id=\"category-{category}-scroll\" class=\"table-scroll\"", html);
                Assert.Contains($"id=\"table-{category}\"", html);
            }
        }
    }

    [Fact]
    public void Html_StyledCssTargetsEveryCategoryScrollContainer()
    {
        var report = Empty() with
        {
            Embedded = [new(null, new("added", "hash", 1, 2)),
                new(new("modified", "old", 1, 2), new("modified", "new", 2, 3))],
            EmbeddedContributions = [new(null, new("asset", 1, 2, 1, null))],
            Items = [new(null, ItemSnapshot.From(new CGameCtnAnchoredObject()))],
            Blocks = [new(null, BlockSnapshot.From(new CGameCtnBlock()))],
            BakedBlocks = [new(null, BlockSnapshot.From(new CGameCtnBlock()))],
            MetadataChanges = [new("display.comments", null, new(Text: "new"))],
            Chunks = [new(null, "1", "body:a")],
        };
        var html = DiffRenderer.RenderHtml(report, "old", "new");

        Assert.Contains(".table-scroll{overflow-x:auto}", html);
        Assert.Equal(8, System.Text.RegularExpressions.Regex.Matches(html,
            "<div id=\"category-[a-z-]+-scroll\" class=\"table-scroll\"><table").Count);
    }

    [Fact]
    public void Html_UsesControlledUniqueIdsForDuplicateRowsAndUserContent()
    {
        const string hostile = "same id=\"injected\" <script>";
        var item = ItemSnapshot.From(new CGameCtnAnchoredObject()) with { Path = hostile };
        var report = Empty() with { Items = [new(null, item), new(null, item)] };
        var html = DiffRenderer.RenderHtml(report, hostile, hostile, styled: false);
        var ids = System.Text.RegularExpressions.Regex.Matches(html, "\\sid=\\\"([^\\\"]+)\\\"")
            .Select(match => match.Groups[1].Value).ToArray();

        Assert.NotEmpty(ids);
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.Matches("^[a-z][a-z0-9-]*$", id));
        Assert.Contains("id=\"table-placed-items-row-0\"", html);
        Assert.Contains("id=\"table-placed-items-row-1\"", html);
        Assert.DoesNotContain("id=\"injected\"", html);
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public void Html_EmptyStateHasStableContextAndNoCategoryTables()
    {
        var html = DiffRenderer.RenderHtml(Empty() with { RightBytes = 1000 }, "same", "same", styled: false);

        Assert.Contains("<main id=\"map-diff\" class=\"diff-report\">", html);
        Assert.Contains("<p id=\"diff-empty-state\" class=\"empty-state\">No differences in the compared fields.</p>", html);
        Assert.DoesNotContain("class=\"diff-category", html);
        Assert.DoesNotContain("<style", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" style=", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Html_StyledByDefaultAndUnstyledHasNoCssInlineStylesOrChipsWithContentParity()
    {
        var report = ColorReport("Blue") with
        {
            Warnings = ["warning<script>"],
            MapName = new("Before", "After"),
        };
        var styled = DiffRenderer.RenderHtml(report, "old", "new", colorOption: true);
        var plain = DiffRenderer.RenderHtml(report, "old", "new", colorOption: true, styled: false);

        Assert.Contains("<style>", styled);
        Assert.Contains("class=\"color-chip\"", styled);
        Assert.Contains("border-radius:999px", styled);
        Assert.DoesNotContain("<style", plain, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" style=", plain, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("class=\"color-chip\"", plain);
        foreach (var text in new[] { "Map diff", "Size:", "Warning:", "Placed items", "Blocks", "Baked blocks", "Map name", "Before", "After", "Blue" })
        {
            Assert.Contains(text, styled);
            Assert.Contains(text, plain);
        }
        Assert.DoesNotContain("<script>", styled);
        Assert.DoesNotContain("<script>", plain);
    }

    [Fact]
    public void Html_ColorTransitionsUseSeparateBalancedChipsWhileDefaultStaysOrdinary()
    {
        var report = ColorReport("Red");
        var html = DiffRenderer.RenderHtml(report, "left", "right", colorOption: true);

        Assert.Contains("Default → <span class=\"color-chip\"", html);
        Assert.DoesNotContain(">Default</span>", html);
        Assert.Contains(">Red</span>", html);
        Assert.Contains("padding:.18em .65em", html);
        Assert.Contains("line-height:1.25", html);
    }

    [Fact]
    public void UnavailableReasonsAreEscapedAtEveryOutputBoundaryWithoutEncodingTheHelper()
    {
        const string reason = "budget <script>&[red]`|\u001b";
        Assert.Equal($"unavailable: {reason}", new EmbeddedSizePresentation().FormatMarginal(null, reason));
        var report = Empty() with
        {
            EmbeddedContributions = [new(null, new("asset", 10, 20, null, reason))],
        };

        var console = new TestConsole().Width(120);
        DiffRenderer.Render(console, report, "old", "new");
        var html = DiffRenderer.RenderHtml(report, "old", "new");
        var markdown = DiffRenderer.RenderMarkdown(report, "old", "new");
        Assert.DoesNotContain("\u001b", console.Output);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;&amp;", html);
        Assert.DoesNotContain("<script>", markdown);
        Assert.Contains("&lt;script\\>&amp;", markdown);
    }

    [Theory]
    [InlineData(80)]
    [InlineData(120)]
    public void Render_UsesBalancedHeaderAndMetadataFrameAtOrdinaryWidths(int width)
    {
        var report = Empty() with
        {
            MapUid = new("uid-before", "uid-after"),
            MapName = new("name-before", "name-after"),
            AuthorNickname = new("author-before", "author-after"),
            AuthorLogin = new("login-before", "login-after"),
            Password = new("true", "false"),
        };
        var console = new TestConsole().Width(width);

        DiffRenderer.Render(console, report, "/very/long/old/path/map.Map.Gbx", "/very/long/new/path/map.Map.Gbx");

        var output = console.Output;
        Assert.Contains("Map diff", output);
        Assert.Contains("Old  /very/long/old/path/map.Map.Gbx", output);
        Assert.Contains("New  /very/long/new/path/map.Map.Gbx", output);
        Assert.Contains("Size 1,000 → 800 bytes", output);
        Assert.Contains("Metadata", output);
        Assert.Contains("Map UID", output);
        Assert.Contains("Map name", output);
        Assert.Contains("Author name", output);
        Assert.Contains("Author login", output);
        Assert.Contains("Plaintext password", output);
        Assert.DoesNotContain("Password chunk", output);
        Assert.DoesNotContain("┏", output);
        Assert.DoesNotContain("╔", output);
        Assert.DoesNotContain("\u001b", output);
    }

    [Fact]
    public void Colors_KnownPaletteHasPaddedChipsInEverySpatialTable()
    {
        var names = Enum.GetNames(typeof(CGameCtnBlock).GetProperty("Color")!.PropertyType);
        Assert.Equal(new[] { "Default", "White", "Green", "Blue", "Red", "Black" }, names);
        foreach (var name in names.Where(n => n != "Default"))
        {
            var report = ColorReport(name);
            var html = DiffRenderer.RenderHtml(report, "left", "right", colorOption: true);
            Assert.Equal(3, html.Split($">{name}</span>", StringSplitOptions.None).Length - 1);
            Assert.Contains("background-color:", html);
            var console = new TestConsole().Width(500);
            console.Profile.Capabilities.Ansi = true;
            console.Profile.Capabilities.ColorSystem = Spectre.Console.ColorSystem.TrueColor;
            console.EmitAnsiSequences = true;
            DiffRenderer.Render(console, report, "left", "right");
            Assert.Equal(3, console.Output.Split($" {name} \u001b", StringSplitOptions.None).Length - 1);
        }
    }

    [Fact]
    public void Colors_DefaultUnknownAndNoColorRemainOrdinaryAndSafe()
    {
        const string unknown = "Blue → Red [red]<script>\u001b";
        foreach (var name in new[] { "Default", unknown })
        {
            var html = DiffRenderer.RenderHtml(ColorReport(name), "left", "right", colorOption: true);
            Assert.DoesNotContain("<span", html);
            Assert.DoesNotContain("<script>", html);
            Assert.DoesNotContain("\u001b", html);
        }
        var report = ColorReport("Blue");
        var console = new TestConsole().Width(500);
        DiffRenderer.Render(console, report, "left", "right");
        Assert.DoesNotContain("\u001b", console.Output);
        Assert.DoesNotContain("<span", DiffRenderer.RenderMarkdown(report, "left", "right"));
        Assert.DoesNotContain("<span", DiffRenderer.RenderHtml(report, "left", "right", colorOption: false));
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    public void BlockColors_SingleAndHomogeneousNonDefaultKeepColumn(bool baked, int count)
    {
        var block = BlockSnapshot.From(new CGameCtnBlock { Name = "blueBlock", Color = DifficultyColor.Blue });
        var changes = Enumerable.Range(0, count).Select(i => new ValueChange<BlockSnapshot>(null,
            block with { Name = $"blueBlock{i}" })).ToArray();
        var report = baked ? Empty() with { BakedBlocks = changes } : Empty() with { Blocks = changes };
        foreach (var output in Outputs(report))
        {
            Assert.Contains("Color", output);
            Assert.Contains("Blue", output);
        }
        var html = DiffRenderer.RenderHtml(report, "old", "new", colorOption: true);
        Assert.Equal(count, html.Split(">Blue</span>", StringSplitOptions.None).Length - 1);
        var plain = DiffRenderer.RenderHtml(report, "old", "new", colorOption: false);
        Assert.DoesNotContain("<span", plain);
        Assert.Contains(">Blue</td>", plain);
        if (Environment.GetEnvironmentVariable("GBX_RENDER_ARTIFACT_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, $"diff-blue-{baked}-{count}.html"), html);
            File.WriteAllText(Path.Combine(directory, $"diff-blue-{baked}-{count}-plain.html"), plain);
        }
    }

    private static DiffReport ColorReport(string color)
    {
        var item = ItemSnapshot.From(new CGameCtnAnchoredObject());
        var block = BlockSnapshot.From(new CGameCtnBlock());
        return Empty() with
        {
            Items = [new(item with { Color = "Default" }, item with { Color = color })],
            Blocks = [new(block with { Color = "Default" }, block with { Color = color })],
            BakedBlocks = [new(block with { Color = "Default" }, block with { Color = color })],
        };
    }

    [Fact]
    public void BlockPositions_HideOrdinaryAndGhostMidpointsButKeepFreeBakedAndSortOrder()
    {
        var normal = BlockSnapshot.From(new CGameCtnBlock { Name = "normalBlock", Coord = new(2, 2, 2) });
        var ghost = BlockSnapshot.From(new CGameCtnBlock { Name = "ghost", Coord = new(1, 1, 1), IsGhost = true });
        var free = BlockSnapshot.From(new CGameCtnBlock { Name = "free", IsFree = true }) with
        {
            PhysicalPosition = new(1.234567, 2, 3), Rotation = new(.1, .2, .3),
        };
        var report = Empty() with { Blocks = [new(null, normal), new(null, ghost), new(null, free)] };
        foreach (var output in Outputs(report))
        {
            Assert.DoesNotContain("80.0, 20.0, 80.0", output);
            Assert.DoesNotContain("48.0, 12.0, 48.0", output);
            Assert.Contains("1.235, 2.0, 3.0", output);
            Assert.True(output.IndexOf("free", StringComparison.Ordinal) < output.IndexOf("ghost", StringComparison.Ordinal));
            Assert.True(output.IndexOf("ghost", StringComparison.Ordinal) < output.IndexOf("normalBlock", StringComparison.Ordinal));
        }
        Assert.Contains("normalBlock</td><td", DiffRenderer.RenderHtml(report, "a", "b"));
        Assert.Contains(">(2, 2, 2)</td><td", DiffRenderer.RenderHtml(report, "a", "b"));
        Assert.Contains(">--</td>", DiffRenderer.RenderHtml(report, "a", "b"));
        Assert.Contains("ghost</td><td", DiffRenderer.RenderHtml(report, "a", "b"));
        Assert.Contains(">(1, 1, 1)</td><td", DiffRenderer.RenderHtml(report, "a", "b"));
        foreach (var output in Outputs(report with { Blocks = [], BakedBlocks = report.Blocks }))
        {
            Assert.Contains("80.0, 20.0, 80.0", output);
            Assert.Contains("48.0, 12.0, 48.0", output);
            Assert.Contains("1.235, 2.0, 3.0", output);
        }
        Assert.Equal(new SpatialPosition(80, 20, 80), normal.PhysicalPosition);
    }

    [Fact]
    public void SharedTables_BrowserFixtureCoversTransformsColorsAndEscaping()
    {
        var item = ItemSnapshot.From(new CGameCtnAnchoredObject()) with
        {
            PhysicalPosition = new(1.234567, 2, 3), Rotation = new(.123456, 0, 0),
            Pivot = new(0, 1.234567, 0), Scale = 1.234567f,
        };
        var grid = BlockSnapshot.From(new CGameCtnBlock { Coord = new(1, 2, 3) });
        var labels = new[] { "White", "Green", "Blue", "Red", "Black", "Default", "Blue → Red [red]<script>\u001b" };
        var blocks = labels.Select((label, i) => new ValueChange<BlockSnapshot>(null, grid with
        {
            Name = $"grid{i}<img src=x onerror=alert(1)>", Color = label, IsGhost = i == 1,
            PhysicalPosition = i < 4 ? new(i, 0, 0) : new(1000 + i, 0, 0),
        })).Append(new(null, grid with
        {
            Name = "freeBlock", IsFree = true, PhysicalPosition = new(1.234567, 2, 3), Rotation = new(.123456, 0, 0),
        })).ToArray();
        var report = Empty() with
        {
            Embedded = [new(null, new("zAdded<script>", "h", 10, 20)), new(new("BRemoved", "h", 10, 20), null),
                new(new("bModified", "h", 10, 20), new("bModified", "i", 12, 24)),
                new(new("aModified", "h", 10, 20), new("aModified", "i", 12, 24))],
            Items = labels.Select((label, i) => new ValueChange<ItemSnapshot>(null, item with
            {
                Path = $"item{i}[red]<script>.Item.Gbx", Color = label, AnimationPhase = i.ToString(),
                PhysicalPosition = i < 4 ? new(i, 0, 0) : new(1000 + i, 0, 0),
            })).Append(new(item with { Path = "transition", Color = "Blue" }, item with { Path = "transition", Color = "Red" })).ToArray(),
            Blocks = blocks, BakedBlocks = blocks,
            Chunks = [new("10 bytes", "20 bytes", "body:a"), new("20 bytes", "30 bytes", "body:b"), new("10 bytes", "20 bytes", "header:c<script>")],
            MetadataChanges = [new("display.comments", new(Text: "old"), new(Text: "new")),
                new("display.style", null, new(Text: "Race")), new("validation.validated", new(Boolean: false), new(Boolean: true)),
                new("custom<script>", new(Text: "old"), new(Text: "new"))],
            EmbeddedContributions = [new(null, new("dir/a", 10, 20, 5, null)), new(null, new("dir/b", 10, 20, -2, null)),
                new(new("else/c", 20, 30, null, "not measured<script>"), new("else/c", 21, 31, 2, null))],
            LeftContributionBaselineBytes = 1_000,
            RightContributionBaselineBytes = 1_100,
            Warnings = ["warning<script>"],
            MapName = new("old", "<script>alert(1)</script>"),
        };
        var html = DiffRenderer.RenderHtml(report, "old<script>.Map.Gbx", "new[red].Map.Gbx", colorOption: true);
        var plainHtml = DiffRenderer.RenderHtml(report, "old<script>.Map.Gbx", "new[red].Map.Gbx", colorOption: true, styled: false);
        Assert.Equal(8, html.Split("class=\"diff-table category-table\"", StringSplitOptions.None).Length - 1);
        Assert.Equal(17, html.Split("class=\"color-chip\"", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<style", plainHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" style=", plainHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("class=\"color-chip\"", plainHtml);
        foreach (var category in new[] { "Warning:", "Embedded files — added/removed", "Embedded files — modified", "Embedded outer-map contribution", "Placed items", "Blocks", "Baked blocks", "Map metadata", "Chunks", "Map name" })
            Assert.Contains(category, plainHtml);
        if (Environment.GetEnvironmentVariable("GBX_RENDER_ARTIFACT_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "diff.html"), html);
            File.WriteAllText(Path.Combine(directory, "diff-unstyled.html"), plainHtml);
            File.WriteAllText(Path.Combine(directory, "diff-no-color.html"), DiffRenderer.RenderHtml(report, "old", "new", colorOption: false));
            File.WriteAllText(Path.Combine(directory, "diff-environment.html"), DiffRenderer.RenderHtml(report, "old", "new"));
            File.WriteAllText(Path.Combine(directory, "diff.md"), DiffRenderer.RenderMarkdown(report, "old", "new"));
            File.WriteAllText(Path.Combine(directory, "diff.json"), DiffMode.RenderJson(report));
            foreach (var enabled in new[] { false, true })
            foreach (var width in new[] { 80, 120, 240 })
            {
                var console = new TestConsole().Width(width);
                console.Profile.Capabilities.Ansi = enabled;
                console.Profile.Capabilities.ColorSystem = enabled ? Spectre.Console.ColorSystem.TrueColor : Spectre.Console.ColorSystem.NoColors;
                console.EmitAnsiSequences = enabled;
                DiffRenderer.Render(console, report, "old", "new");
                File.WriteAllText(Path.Combine(directory, enabled ? $"diff-tty-{width}.txt" : $"diff-plain-{width}.txt"), console.Output);
            }
        }
    }

    [Fact]
    public void MetadataAndContributions_RenderTypedAbsenceAndSafeNonAdditiveMeasurements()
    {
        var report = Empty() with
        {
            MetadataChanges = [new("display.comments", null, new(Text: "")),
                new("validation.forScriptModes", null, new(Boolean: false)),
                new("medals.authorMs", null, new(Integer: 0)),
                new("custom[red]<script>\u001b", new(Text: "old"), new(Text: "<script>\u001b"))],
            LeftContributionBaselineBytes = 123,
            RightContributionBaselineBytes = 456,
            EmbeddedContributions = [new(new("asset", 10, 20, -5, null), new("asset", 11, 21, null, "budget<script>\u001b"))],
        };
        foreach (var output in Outputs(report))
        {
            Assert.Contains("display.comments", output);
            Assert.Contains("absent", output);
            Assert.Contains("false", output);
            Assert.Contains("non-additive", output);
            Assert.Contains("LZO", output);
            Assert.Contains("123", output);
            Assert.Contains("456", output);
            Assert.Contains("unavailable", output);
            Assert.DoesNotContain("No differences", output);
            Assert.DoesNotContain("\u001b", output);
        }
        var html = DiffRenderer.RenderHtml(report, "a", "b");
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("-5", html);
        Assert.Contains("&quot;&quot;", html);
        if (Environment.GetEnvironmentVariable("GBX_RENDER_ARTIFACT_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "diff-integration.html"), html);
        }
    }

    [Theory]
    [InlineData(80)]
    [InlineData(120)]
    public void MetadataAndContributions_FitOrdinaryTerminalWidths(int width)
    {
        var report = Empty() with
        {
            MetadataChanges = [new("display.comments", null, new(Text: ""))],
            EmbeddedContributions = [new(new("asset.bin", 10, 20, -5, null),
                new("asset.bin", 11, 21, null, "Removal trial budget exhausted."))],
        };
        var console = new TestConsole().Width(width);
        DiffRenderer.Render(console, report, "a", "b");
        Assert.Contains("asset.bin", console.Output);
        Assert.Contains("display.comments", console.Output);
        Assert.DoesNotContain("\u001b", console.Output);
    }

    [Fact]
    public void Warnings_AreEscapedAndDoNotCountAsChanges()
    {
        var report = Empty() with { RightBytes = 1000, Warnings = ["semantic unavailable [red]<script>\u001b"] };
        foreach (var output in Outputs(report))
        {
            Assert.Contains("Warning", output);
            Assert.Contains("No differences in the compared fields.", output);
            Assert.DoesNotContain("~1 changed", output);
            Assert.DoesNotContain("\u001b", output);
        }
        Assert.DoesNotContain("<script>", DiffRenderer.RenderHtml(report, "a", "b"));
        Assert.DoesNotContain("<script>", DiffRenderer.RenderMarkdown(report, "a", "b"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Metadata_OverlappingFieldsAppearOnceAndLegacyOnlyReportsStillRender(bool legacyOnly)
    {
        var report = DiffMode.CompareMaps(
            new() { MapUid = "uidBefore", MapName = "nameBefore", AuthorLogin = "loginBefore", AuthorNickname = "nickBefore" },
            new() { MapUid = "uidAfter", MapName = "nameAfter", AuthorLogin = "loginAfter", AuthorNickname = "nickAfter", Password = "secret" });
        var json = DiffMode.RenderJson(report);
        using (var document = System.Text.Json.JsonDocument.Parse(json))
        {
            Assert.Equal(5, document.RootElement.GetProperty("MetadataChanges").GetArrayLength());
            foreach (var field in new[] { "MapUid", "MapName", "AuthorLogin", "AuthorNickname", "Password" })
                Assert.Equal(System.Text.Json.JsonValueKind.Object, document.RootElement.GetProperty(field).ValueKind);
        }
        if (legacyOnly) report = report with { MetadataChanges = [] };
        foreach (var output in Outputs(report))
        {
            foreach (var label in new[] { "Map UID", "Map name", "Author login", "Author name", "Plaintext password" })
                Assert.Equal(1, output.Split(label, StringSplitOptions.None).Length - 1);
            foreach (var value in new[] { "uidBefore", "uidAfter", "nameBefore", "nameAfter", "loginBefore", "loginAfter", "nickBefore", "nickAfter", "true", "false" })
                Assert.Equal(1, output.ToLowerInvariant().Split(value.ToLowerInvariant(), StringSplitOptions.None).Length - 1);
            foreach (var path in new[] { "map.uid", "map.name", "author.login", "author.nickname", "security.passwordPresent" })
                Assert.DoesNotContain(path, output);
            Assert.DoesNotContain("secret", output);
        }
        if (!legacyOnly) Assert.Equal(json, DiffMode.RenderJson(report));
        if (Environment.GetEnvironmentVariable("GBX_RENDER_ARTIFACT_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, $"diff-metadata-once-{legacyOnly}.html"), DiffRenderer.RenderHtml(report, "old", "new"));
        }
    }

    [Fact]
    public void Metadata_TypedOnlyOverlappingFieldPreservesAbsenceAndEmptyText()
    {
        var report = Empty() with
        {
            RightBytes = 1000,
            MetadataChanges = [new("map.name", null, new(Text: ""))],
        };
        Assert.Null(report.MapName);
        Assert.Single(report.MetadataChanges);
        foreach (var output in Outputs(report))
        {
            Assert.Contains("map.name", output);
            Assert.Contains("absent", output);
            Assert.DoesNotContain("No differences", output);
        }
        Assert.Contains("&quot;&quot;", DiffRenderer.RenderHtml(report, "old", "new"));
    }

    [Fact]
    public void SpatialGroups_ConnectNearbyRegionsAcrossMortonDiscontinuitiesAndKeepTies()
    {
        var item = ItemSnapshot.From(new CGameCtnAnchoredObject());
        var changes = new ValueChange<ItemSnapshot>[]
        {
            new(null, item with { Path = "nearA", PhysicalPosition = new(-1, 0, 0) }),
            new(item with { Path = "nearTie", PhysicalPosition = new(-1, 0, 0) }, null),
            new(null, item with { Path = "nearB", PhysicalPosition = new(1, 0, 0) }),
            new(null, item with { Path = "distant", PhysicalPosition = new(-.5, 0, 1000) }),
            new(null, item with { Path = "chain", PhysicalPosition = new(64, 0, 0) }),
        };
        var report = Empty() with { Items = changes };
        var html = DiffRenderer.RenderHtml(report, "a", "b");
        var groups = HtmlGroups(html);
        Assert.Equal(2, groups.Length);
        Assert.Contains("nearA", groups[0]);
        Assert.Contains("nearTie", groups[0]);
        Assert.Contains("nearB", groups[0]);
        Assert.Contains("chain", groups[0]);
        Assert.DoesNotContain("distant", groups[0]);
        Assert.Contains("distant", groups[1]);
        Assert.Equal(html, DiffRenderer.RenderHtml(report with { Items = changes.Reverse().ToArray() }, "a", "b"));
    }

    [Theory]
    [InlineData(80)]
    [InlineData(120)]
    public void SpatialGroups_OnlySeparateRegionsInPlainTerminalAndMarkdown(int width)
    {
        var item = ItemSnapshot.From(new CGameCtnAnchoredObject());
        var report = Empty() with { Items = [new(null, item with { Path = "nearA" }),
            new(null, item with { Path = "nearB", PhysicalPosition = new(1, 0, 0) }),
            new(null, item with { Path = "farAway", PhysicalPosition = new(1000, 0, 0) })] };
        var console = new TestConsole().Width(width);
        DiffRenderer.Render(console, report, "a", "b");
        var lines = console.Output.Split('\n');
        var a = Array.FindIndex(lines, l => l.Contains("nearA"));
        var b = Array.FindIndex(lines, l => l.Contains("nearB"));
        var far = Array.FindIndex(lines, l => l.Contains("farAway"));
        Assert.True(a >= 0 && b > a && far > b);
        Assert.DoesNotContain(lines[(a + 1)..b], l => string.IsNullOrWhiteSpace(l) || l.Contains('─'));
        Assert.Single(lines[(b + 1)..far], string.IsNullOrWhiteSpace);
        Assert.DoesNotContain(lines[(b + 1)..far], l => l.Contains('─'));
        Assert.DoesNotContain("\u001b", console.Output);
        var markdown = DiffRenderer.RenderMarkdown(report, "a", "b");
        Assert.Single(markdown.Split('\n'), l => l == "|  |  |  |  |  |");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SpatialGroups_BlocksUsePhysicalPositionsAndHandleMissingAndNonFinite(bool baked)
    {
        var block = BlockSnapshot.From(new CGameCtnBlock { IsFree = true });
        var changes = new ValueChange<BlockSnapshot>[]
        {
            new(null, block with { Name = "nearA", X = 1000, PhysicalPosition = new(-1, 0, 0) }),
            new(null, block with { Name = "nearB", X = -1000, PhysicalPosition = new(1, 0, 0) }),
            new(null, block with { Name = "farAway", PhysicalPosition = new(1000, 0, 0) }),
            new(null, block with { Name = "missingA", PhysicalPosition = null }),
            new(null, block with { Name = "missingB", PhysicalPosition = null }),
            new(null, block with { Name = "infinite", PhysicalPosition = new(double.PositiveInfinity, 0, 0) }),
            new(null, block with { Name = "notANumber", PhysicalPosition = new(double.NaN, 0, 0) }),
        };
        var report = baked ? Empty() with { BakedBlocks = changes } : Empty() with { Blocks = changes };
        var html = DiffRenderer.RenderHtml(report, "a", "b");
        var groups = HtmlGroups(html);
        Assert.Equal(5, groups.Length);
        Assert.Single(groups, g => g.Contains("nearA") && g.Contains("nearB"));
        Assert.Single(groups, g => g.Contains("missingA") && g.Contains("missingB"));
        var reversed = baked ? report with { BakedBlocks = changes.Reverse().ToArray() } : report with { Blocks = changes.Reverse().ToArray() };
        Assert.Equal(html, DiffRenderer.RenderHtml(reversed, "a", "b"));
    }

    [Fact]
    public void ContextGroups_UseDirectoriesChangeKindsMetadataDomainsAndChunkSections()
    {
        var report = Empty() with
        {
            Embedded = [new(null, new("dir/a", "h", 1, 2)), new(null, new("dir/b", "h", 1, 2)),
                new(null, new("other/c", "h", 1, 2)), new(new("dir/gone", "h", 1, 2), null)],
            EmbeddedContributions = [new(null, new("dir/a", 1, 2, 1, null)), new(null, new("dir/b", 1, 2, 1, null)),
                new(null, new("other/c", 1, 2, 1, null))],
            MetadataChanges = [new("display.a", null, new(Text: "a")), new("display.b", null, new(Text: "b")),
                new("validation.a", null, new(Boolean: true))],
            Chunks = [new("1", "2", "body:a"), new("1", "2", "body:b"), new("1", "2", "header:a")],
        };
        var groups = HtmlGroups(DiffRenderer.RenderHtml(report, "a", "b"));
        Assert.Equal(9, groups.Length);
        Assert.Contains("gone", groups[0]);
        Assert.Contains("dir/a", groups[1]);
        Assert.Contains("dir/b", groups[1]);
        Assert.Contains("other/c", groups[2]);
        Assert.Contains("display.a", groups[5]);
        Assert.Contains("display.b", groups[5]);
        Assert.Contains("body:a", groups[7]);
        Assert.Contains("body:b", groups[7]);
    }

    private static string[] HtmlGroups(string html) => System.Text.RegularExpressions.Regex.Matches(html, "<tbody[^>]*>(.*?)</tbody>")
        .Select(m => m.Groups[1].Value).ToArray();

    private static IEnumerable<string> Outputs(DiffReport report)
    {
        var console = new TestConsole().Width(500);
        DiffRenderer.Render(console, report, "a", "b");
        yield return console.Output;
        yield return DiffRenderer.RenderMarkdown(report, "a", "b");
        yield return DiffRenderer.RenderHtml(report, "a", "b");
    }

    private static DiffReport Empty() => new(1000, 800, [], [], [], [], [], null, null, null, null, null);
}
