using GbxSizeTree.Model;
using Spectre.Console;

namespace GbxSizeTree.Cli.Rendering;

/// <summary>
/// Charts mutually exclusive on-disk category contributions from the GBX attribution tree.
/// Category estimates are normalized within each file section so the visible total always
/// reconciles to the actual file size.
/// </summary>
public static class BreakdownRenderer
{
    public static void Render(IAnsiConsole console, SizeNode root)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(root);

        var contributions = BuildContributions(root);
        var groups = contributions
            .GroupBy(item => item.Category)
            .Select(group => new { Category = group.Key, Bytes = group.Sum(item => item.Bytes) })
            .Where(group => group.Bytes > 0)
            .OrderByDescending(group => group.Bytes)
            .ThenBy(group => group.Category)
            .ToList();

        var chart = new BreakdownChart()
            .FullSize()
            .ShowPercentage()
            .UseValueFormatter(value => SizeFormat.ShortBytes((long)value));

        foreach (var group in groups)
        {
            chart.AddItem(CategoryLabel(group.Category), group.Bytes, Theme.CategoryColor(group.Category));
        }

        console.MarkupLine("[bold]On-disk contribution by category[/] [dim](estimated split; exact total)[/]");
        console.Write(chart);
        var accounted = groups.Sum(group => group.Bytes);
        console.MarkupLine(
            $"[bold]Accounted on disk:[/] {Markup.Escape(SizeFormat.Bytes(accounted))} / " +
            $"{Markup.Escape(SizeFormat.Bytes(DisplayBytes(root)))}");
    }

    internal static long OnDiskBytesForCategory(SizeNode root, SizeCategory category)
    {
        ArgumentNullException.ThrowIfNull(root);
        return BuildContributions(root)
            .Where(item => item.Category == category)
            .Sum(item => item.Bytes);
    }

    private static IReadOnlyList<Contribution> BuildContributions(SizeNode root)
    {
        var contributions = new List<Contribution>();
        long sectionBytes = 0;
        foreach (var section in root.Children)
        {
            var displayed = DisplayBytes(section);
            sectionBytes += displayed;
            contributions.AddRange(NormalizeSection(section, displayed));
        }

        var rootBytes = DisplayBytes(root);
        if (sectionBytes < rootBytes)
        {
            contributions.Add(new Contribution(SizeCategory.Other, rootBytes - sectionBytes));
        }

        return sectionBytes > rootBytes
            ? ScaleToTotal(contributions, rootBytes)
            : contributions;
    }

    private static IReadOnlyList<Contribution> NormalizeSection(SizeNode section, long sectionBytes)
    {
        if (section.Children.Count == 0)
        {
            return [new Contribution(section.Category, sectionBytes)];
        }

        var contributions = section.Children
            .Select(child => new Contribution(child.Category, DisplayBytes(child)))
            .Where(item => item.Bytes > 0)
            .ToList();
        var childBytes = contributions.Sum(item => item.Bytes);
        if (childBytes < sectionBytes)
        {
            contributions.Add(new Contribution(section.Category, sectionBytes - childBytes));
            return contributions;
        }

        return childBytes > sectionBytes
            ? ScaleToTotal(contributions, sectionBytes)
            : contributions;
    }

    private static IReadOnlyList<Contribution> ScaleToTotal(
        IReadOnlyList<Contribution> contributions,
        long targetBytes)
    {
        var sourceBytes = contributions.Sum(item => item.Bytes);
        if (sourceBytes <= 0 || contributions.Count == 0)
        {
            return [];
        }

        var scaled = new List<Contribution>(contributions.Count);
        long assigned = 0;
        for (var index = 0; index < contributions.Count; index++)
        {
            var item = contributions[index];
            var bytes = index == contributions.Count - 1
                ? targetBytes - assigned
                : item.Bytes * targetBytes / sourceBytes;
            scaled.Add(item with { Bytes = bytes });
            assigned += bytes;
        }
        return scaled;
    }

    private static long DisplayBytes(SizeNode node) =>
        node.OnDiskBytes ?? node.EstimatedOnDiskBytes ?? node.UncompressedBytes;

    private sealed record Contribution(SizeCategory Category, long Bytes);

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
