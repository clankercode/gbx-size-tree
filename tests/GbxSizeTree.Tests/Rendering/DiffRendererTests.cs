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
            Assert.Contains("Compressed", output);
            Assert.Contains("Uncompressed", output);
            Assert.Contains("Ratio", output);
            Assert.Contains("20 → 30", output);
            Assert.Contains("100 → 120", output);
            Assert.Contains("20.00 % → 25.00 %", output);
            Assert.Contains("asset", output);
            Assert.DoesNotContain("secret-hash", output);
            Assert.DoesNotContain("compressed=", output);
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
            Assert.Contains("Scale", output);
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
    public void Colors_KnownPaletteHasPaddedChipsInEverySpatialTable()
    {
        var names = Enum.GetNames(typeof(CGameCtnBlock).GetProperty("Color")!.PropertyType);
        Assert.Equal(new[] { "Default", "White", "Green", "Blue", "Red", "Black" }, names);
        foreach (var name in names.Where(n => n != "Default"))
        {
            var report = ColorReport(name);
            var html = DiffRenderer.RenderHtml(report, "left", "right", colorOption: true);
            Assert.Equal(3, html.Split($"> {name} </span>", StringSplitOptions.None).Length - 1);
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
        Assert.Contains("<td>normalBlock</td><td>(2, 2, 2)</td><td>--</td>", DiffRenderer.RenderHtml(report, "a", "b"));
        Assert.Contains("<td>ghost</td><td>(1, 1, 1)</td><td>--</td>", DiffRenderer.RenderHtml(report, "a", "b"));
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
            })).Append(new(item with { Path = "transition", Color = "Blue" }, item with { Path = "transition", Color = "Red" })).ToArray(),
            Blocks = blocks, BakedBlocks = blocks,
            Chunks = [new("10 bytes", "20 bytes", "chunk<script>" )],
            MapName = new("old", "<script>alert(1)</script>"),
        };
        var html = DiffRenderer.RenderHtml(report, "old<script>.Map.Gbx", "new[red].Map.Gbx", colorOption: true);
        Assert.Equal(6, html.Split("<table>", StringSplitOptions.None).Length - 1);
        Assert.Equal(17, html.Split("<span", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("<script>", html);
        if (Environment.GetEnvironmentVariable("GBX_RENDER_ARTIFACT_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "diff.html"), html);
            File.WriteAllText(Path.Combine(directory, "diff-no-color.html"), DiffRenderer.RenderHtml(report, "old", "new", colorOption: false));
            File.WriteAllText(Path.Combine(directory, "diff-environment.html"), DiffRenderer.RenderHtml(report, "old", "new"));
            File.WriteAllText(Path.Combine(directory, "diff.md"), DiffRenderer.RenderMarkdown(report, "old", "new"));
            File.WriteAllText(Path.Combine(directory, "diff.json"), DiffMode.RenderJson(report));
            foreach (var enabled in new[] { false, true })
            {
                var console = new TestConsole().Width(240);
                console.Profile.Capabilities.Ansi = enabled;
                console.Profile.Capabilities.ColorSystem = enabled ? Spectre.Console.ColorSystem.TrueColor : Spectre.Console.ColorSystem.NoColors;
                console.EmitAnsiSequences = enabled;
                DiffRenderer.Render(console, report, "old", "new");
                File.WriteAllText(Path.Combine(directory, enabled ? "diff-tty.txt" : "diff-plain.txt"), console.Output);
            }
        }
    }

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
