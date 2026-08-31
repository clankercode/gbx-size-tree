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

        console.WriteLine();
        BreakdownRenderer.Render(console, analysis.Tree);
        if (analysis.Body?.Lightmap is { HasLightmaps: true } lightmap)
        {
            console.WriteLine();
            var lightmapBytes = BreakdownRenderer.OnDiskBytesForCategory(
                analysis.Tree,
                SizeCategory.Lightmap);
            LightmapBreakdownRenderer.Render(console, lightmap, lightmapBytes);
        }

        console.WriteLine();
        RenderOnlineLimitDelta(console, analysis.FileBytes);
        console.WriteLine();

        SizeTreeRenderer.Render(console, analysis.Tree, topN);
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
        console.MarkupLine($"[bold]{label}[/] — {size}");

        if (analysis.Facts is not { } facts)
        {
            return;
        }

        var name = TmText.Deformat(facts.MapName);
        // TM2020's AuthorLogin is an opaque account id; the nickname is the display name.
        var author = TmText.Deformat(
            facts.AuthorNickname.Length > 0 ? facts.AuthorNickname : facts.AuthorLogin);
        var line = (name.Length > 0, author.Length > 0) switch
        {
            (true, true) => $"[bold]{Markup.Escape(name)}[/] [dim]by[/] {Markup.Escape(author)}",
            (true, false) => $"[bold]{Markup.Escape(name)}[/]",
            (false, true) => $"[dim]by[/] {Markup.Escape(author)}",
            _ => null,
        };
        if (line is not null)
        {
            console.MarkupLine(line);
        }
    }

    private static void RenderOnlineLimitDelta(IAnsiConsole console, long fileBytes)
    {
        var limit = Markup.Escape(SizeFormat.ShortBytes(Theme.OnlineLimitBytes));
        console.MarkupLine("[bold]Delta to online limit[/]");

        if (fileBytes > Theme.OnlineLimitBytes)
        {
            var delta = Markup.Escape(SizeFormat.ShortBytes(fileBytes - Theme.OnlineLimitBytes));
            console.MarkupLine($"[red bold]{delta} over[/] [dim]({limit} limit)[/]");
            return;
        }

        if (fileBytes < Theme.OnlineLimitBytes)
        {
            var delta = Markup.Escape(SizeFormat.ShortBytes(Theme.OnlineLimitBytes - fileBytes));
            console.MarkupLine($"[green bold]{delta} headroom[/] [dim]({limit} limit)[/]");
            return;
        }

        console.MarkupLine($"[green bold]Exactly at the {limit} limit[/]");
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
