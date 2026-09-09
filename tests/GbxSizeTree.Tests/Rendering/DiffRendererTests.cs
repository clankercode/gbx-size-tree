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
            Embedded = [new("old.Item.Gbx", null), new(null, "new.Item.Gbx")],
            Items = [new("old|position=(1,2,3)|rotation=(0,0,0)", null)],
            Blocks = [new(null, "block|coord=(1,2,3)")],
            MapName = new("Before", "After"),
        };
        var console = new TestConsole().Width(120);
        DiffRenderer.Render(console, report, "old.Map.Gbx", "new.Map.Gbx");

        var output = console.Output;
        Assert.True(output.IndexOf("Embedded files", StringComparison.Ordinal)
            < output.IndexOf("Placed items", StringComparison.Ordinal));
        Assert.True(output.IndexOf("Placed items", StringComparison.Ordinal)
            < output.IndexOf("Blocks", StringComparison.Ordinal));
        Assert.Contains("- old.Item.Gbx", output);
        Assert.Contains("+ new.Item.Gbx", output);
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
        var report = Empty() with { Embedded = [new(null, "[red]asset[/]\u001b[2J\nforged")] };
        DiffRenderer.Render(console, report, "[old]", "new");
        Assert.Contains("[red]asset[/]\\u001b[2J\\nforged", console.Output);
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

    private static DiffReport Empty() => new(1000, 800, [], [], [], [], [], null, null, null, null, null);
}
