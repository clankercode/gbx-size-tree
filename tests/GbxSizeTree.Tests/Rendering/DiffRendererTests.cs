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
            Assert.Contains("48, 20, 112", output);
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
        Assert.Contains("z|position=(999,999,999).Item.Gbx", DiffRenderer.RenderHtml(report, "a", "b"));
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
