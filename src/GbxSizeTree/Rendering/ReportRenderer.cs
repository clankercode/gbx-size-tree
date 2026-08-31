using System.Globalization;
using GbxSizeTree.Model;
using Spectre.Console;

namespace GbxSizeTree.Cli.Rendering;

/// <summary>
/// Renders a complete diagnostic view while preserving the one-LZO-stream caveat from docs/FORMAT-NOTES.md.
/// </summary>
public static class ReportRenderer
{
    public static void Render(IAnsiConsole console, MapAnalysis analysis, int topN)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(analysis);

        RenderTitle(console, analysis);
        if (analysis.Body is { } body)
        {
            RenderCompressionCallout(console, body);
        }

        SizeTreeRenderer.Render(console, analysis.Tree, topN);
        BreakdownRenderer.Render(console, analysis.Tree);
        DrilldownTables.Render(console, analysis, topN);

        if (analysis.Body is { } reconciledBody)
        {
            RenderReconciliation(console, reconciledBody);
        }
    }

    private static void RenderTitle(IAnsiConsole console, MapAnalysis analysis)
    {
        var label = Markup.Escape(analysis.SourceLabel);
        var size = Markup.Escape(SizeFormat.Bytes(analysis.FileBytes));
        if (analysis.FileBytes > Theme.OnlineLimitBytes)
        {
            var overKib = (analysis.FileBytes - Theme.OnlineLimitBytes + 1023) / 1024;
            console.MarkupLine($"[bold]{label}[/] — {size}  [red bold]{overKib.ToString("N0", CultureInfo.InvariantCulture)} KiB OVER the online limit[/]");
            return;
        }

        console.MarkupLine($"[bold]{label}[/] — {size}  [green]under limit[/]");
    }

    private static void RenderCompressionCallout(IAnsiConsole console, BodyAnalysis body)
    {
        // GBX body layout and the honest attribution language come from docs/FORMAT-NOTES.md.
        var text = "body is one LZO stream: " +
            $"{Markup.Escape(SizeFormat.ShortBytes(body.UncompressedBytes))} → " +
            $"{Markup.Escape(SizeFormat.ShortBytes(body.CompressedBytes))} ({Markup.Escape(SizeFormat.Percent(body.Ratio))}); " +
            "tree sizes below are uncompressed; 'on disk ≈' estimates count pre-compressed data " +
            "(webp/jpeg/zip) ~1:1 and scale the rest to match the real total. The category " +
            "chart uses those on-disk contributions and reconciles to the exact file size.";
        console.Write(new Panel(new Markup(text)).Header("[yellow]Honest compression[/]").Border(BoxBorder.Rounded));
    }

    private static void RenderReconciliation(IAnsiConsole console, BodyAnalysis body)
    {
        var attributed = body.Chunks.Sum(chunk => chunk.Bytes);
        console.MarkupLine(
            $"[bold]Reconciliation:[/] attributed {Markup.Escape(SizeFormat.ShortBytes(attributed))} / " +
            $"{Markup.Escape(SizeFormat.ShortBytes(body.UncompressedBytes))}; " +
            $"Unattributed residual {Markup.Escape(SizeFormat.ShortBytes(body.UnattributedBytes))} " +
            $"({Markup.Escape(SizeFormat.Percent(body.UnattributedBytes, body.UncompressedBytes))})");
    }
}
