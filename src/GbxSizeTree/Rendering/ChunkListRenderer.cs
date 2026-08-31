using GbxSizeTree.Model;
using GbxSizeTree.Semantics;
using Spectre.Console;

namespace GbxSizeTree.Cli.Rendering;

/// <summary>
/// Flat chunk listings: the --all-chunks full table (no size cutoff, file order) and the
/// --unknown-chunks debug view for ids missing from <see cref="ChunkCatalog"/>.
/// </summary>
public static class ChunkListRenderer
{
    public static void RenderAll(IAnsiConsole console, MapAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(analysis);

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title("All chunks (file order)")
            .AddColumn("Where")
            .AddColumn("Id")
            .AddColumn("Name")
            .AddColumn(new TableColumn("Bytes").RightAligned())
            .AddColumn("Kind")
            .AddColumn(new TableColumn("Optional").Centered())
            .AddColumn(new TableColumn("Offset").RightAligned());

        foreach (var chunk in analysis.Header.Chunks.OrderBy(chunk => chunk.FileOffset))
        {
            table.AddRow(
                "header",
                Id(chunk.ChunkId),
                Name(chunk.ChunkId, chunk.Name),
                SizeFormat.Bytes(chunk.Bytes),
                chunk.Heavy ? "heavy" : "",
                OptionalGlyph(chunk.ChunkId),
                chunk.FileOffset.ToString("N0", System.Globalization.CultureInfo.InvariantCulture));
        }

        foreach (var chunk in (analysis.Body?.Chunks ?? []).OrderBy(chunk => chunk.Order))
        {
            table.AddRow(
                "body",
                Id(chunk.ChunkId),
                $"{Name(chunk.ChunkId, chunk.Name)} {Theme.ConfidenceGlyph(chunk.Confidence)}",
                SizeFormat.Bytes(chunk.Bytes),
                chunk.Skippable ? "skippable" : "inline",
                OptionalGlyph(chunk.ChunkId),
                chunk.BodyOffset?.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) ?? "");
        }

        console.Write(table);
        console.MarkupLine(
            "[dim]body offsets are into the decompressed body; ● exact  ◐ writer delta  ○ estimated[/]");
        console.MarkupLine(
            "[dim]optional ✓ = verified the game discards it on load (removed by prune-chunks)[/]");
    }

    /// <summary>Plain, greppable listing; returns the number of unknown chunks.</summary>
    public static int RenderUnknown(IAnsiConsole console, MapAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(analysis);

        var unknown = UnknownChunks.Collect(analysis);
        if (unknown.Count == 0)
        {
            console.MarkupLine("[green]no unknown chunks — every chunk id is in the catalog[/]");
            return 0;
        }

        foreach (var chunk in unknown)
        {
            var offset = chunk.Offset is long value
                ? $" @ {value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)}"
                : "";
            console.MarkupLineInterpolated(
                $"[yellow]{chunk.Section}[/] {chunk.ChunkId} {SizeFormat.Bytes(chunk.Bytes)} {(chunk.Skippable ? "skippable" : "inline")}{offset}");
        }

        console.MarkupLineInterpolated(
            $"[bold]{unknown.Count}[/] unknown chunk(s) — candidates for ChunkCatalog + docs/FORMAT-NOTES.md");
        return unknown.Count;
    }

    private static string Id(uint chunkId) => $"0x{chunkId:X8}";

    private static string OptionalGlyph(uint chunkId) =>
        ChunkCatalog.Describe(chunkId).Optional ? "[green]✓[/]" : "[dim]✗[/]";

    private static string Name(uint chunkId, string name) =>
        ChunkCatalog.IsKnown(chunkId) ? Markup.Escape(name) : $"[yellow]{Markup.Escape(name)}[/]";
}
