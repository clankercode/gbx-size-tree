using GbxSizeTree.Model;
using Spectre.Console;

namespace GbxSizeTree.Cli.Rendering;

/// <summary>
/// Renders the frozen GBX attribution tree whose node ids are defined in docs/CONTRACTS.md.
/// </summary>
public static class SizeTreeRenderer
{
    public static void Render(IAnsiConsole console, SizeNode root, int topN)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(root);

        var tree = new Tree(NodeMarkup(root, root.UncompressedBytes));
        AddChildren(tree, root, Math.Max(0, topN));
        console.Write(tree);
        console.MarkupLine("[dim]● exact   ◐ writer delta   ○ estimated[/]");
    }

    private static void AddChildren(IHasTreeNodes parent, SizeNode node, int topN)
    {
        var ordered = node.Children
            .OrderByDescending(child => child.UncompressedBytes)
            .ThenBy(child => child.Label, StringComparer.Ordinal)
            .ToList();
        var visible = ordered.Take(topN).ToList();

        foreach (var child in visible)
        {
            var topLevelOnDisk = node.Id == "file" && node.OnDiskBytes is not null;
            var parentBytes = topLevelOnDisk ? node.OnDiskBytes!.Value : node.UncompressedBytes;
            var childPercentBytes = topLevelOnDisk
                ? child.OnDiskBytes ?? child.EstimatedOnDiskBytes ?? child.UncompressedBytes
                : child.UncompressedBytes;
            var childNode = parent.AddNode(NodeMarkup(child, parentBytes, childPercentBytes));
            AddChildren(childNode, child, topN);
        }

        var hidden = ordered.Count - visible.Count;
        if (hidden > 0)
        {
            parent.AddNode($"[dim]… {hidden} more[/]");
        }
    }

    private static string NodeMarkup(SizeNode node, long parentBytes, long? percentBytes = null)
    {
        var color = Theme.CategoryColor(node.Category).ToMarkup();
        var label = Markup.Escape(node.Label);
        var bytes = SizeFormat.ShortBytes(node.UncompressedBytes).PadLeft(11);
        var percent = SizeFormat.Percent(percentBytes ?? node.UncompressedBytes, parentBytes).PadLeft(6);
        var onDisk = node.OnDiskBytes is long exactOnDisk
            ? $" [dim]on disk {Markup.Escape(SizeFormat.ShortBytes(exactOnDisk))}[/]"
            : node.EstimatedOnDiskBytes is long estimatedOnDisk
                ? $" [dim]on disk ≈ {Markup.Escape(SizeFormat.ShortBytes(estimatedOnDisk))}[/]"
                : string.Empty;
        var detail = string.IsNullOrWhiteSpace(node.Detail)
            ? string.Empty
            : $" [dim]({Markup.Escape(node.Detail)})[/]";

        return $"[{color}]{label}[/]  {Markup.Escape(bytes)}  {Markup.Escape(percent)}  " +
            $"{Theme.ConfidenceGlyph(node.Confidence)}{onDisk}{detail}";
    }
}
