using GbxSizeTree.Model;
using Spectre.Console;

namespace GbxSizeTree.Cli.Rendering;

/// <summary>
/// Charts mutually exclusive leaf categories from the GBX attribution tree defined in docs/CONTRACTS.md.
/// </summary>
public static class BreakdownRenderer
{
    public static void Render(IAnsiConsole console, SizeNode root)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(root);

        var groups = LeafNodes(root)
            .GroupBy(node => node.Category)
            .Select(group => new { Category = group.Key, Bytes = group.Sum(node => node.UncompressedBytes) })
            .Where(group => group.Bytes > 0)
            .OrderByDescending(group => group.Bytes)
            .ThenBy(group => group.Category);

        var chart = new BreakdownChart()
            .FullSize()
            .ShowPercentage()
            .UseValueFormatter(value => SizeFormat.ShortBytes((long)value));

        foreach (var group in groups)
        {
            chart.AddItem(CategoryLabel(group.Category), group.Bytes, Theme.CategoryColor(group.Category));
        }

        console.MarkupLine("[bold]Category breakdown[/]");
        console.Write(chart);
    }

    private static IEnumerable<SizeNode> LeafNodes(SizeNode node)
    {
        if (node.Children.Count == 0)
        {
            yield return node;
            yield break;
        }

        foreach (var child in node.Children)
        {
            foreach (var leaf in LeafNodes(child))
            {
                yield return leaf;
            }
        }
    }

    private static string CategoryLabel(SizeCategory category) => category switch
    {
        SizeCategory.EmbeddedItems => "Embedded items",
        SizeCategory.BakedBlocks => "Baked blocks",
        SizeCategory.ScriptMetadata => "Script metadata",
        SizeCategory.MediaTracker => "MediaTracker",
        SizeCategory.PerElementArrays => "Per-element arrays",
        SizeCategory.FreeBlocks => "Free blocks",
        _ => category.ToString(),
    };
}
