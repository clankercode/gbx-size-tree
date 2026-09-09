using Spectre.Console;

namespace GbxSizeTree.Cli.Modes;

public static class DiffRenderer
{
    public static bool ShouldUseColor(bool? requested, bool redirected, string? terminal, bool noColor) =>
        requested ?? (!redirected && !noColor && !string.Equals(terminal, "dumb", StringComparison.OrdinalIgnoreCase));

    public static IAnsiConsole BuildConsole(bool? colorOption) =>
        AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(Console.Out),
            Ansi = colorOption switch
            {
                false => AnsiSupport.No,
                true => AnsiSupport.Yes,
                null => AnsiSupport.Detect,
            },
            ColorSystem = colorOption == false ? ColorSystemSupport.NoColors : ColorSystemSupport.Detect,
        });

    public static void Render(IAnsiConsole console, DiffReport report, string oldPath, string newPath)
    {
        var color = console.Profile.Capabilities.ColorSystem != ColorSystem.NoColors;
        console.MarkupLine($"[bold]Diff[/]: {Escape(oldPath)} [grey]\u2192[/] {Escape(newPath)}");
        var sizeDelta = report.RightBytes - report.LeftBytes;
        console.MarkupLine($"Size: {report.LeftBytes:N0} [grey]\u2192[/] {report.RightBytes:N0} bytes {Delta(sizeDelta, color)}");

        RenderChanges(console, "Embedded files", report.Embedded, color);
        RenderChanges(console, "Placed items", report.Items, color);
        RenderChanges(console, "Blocks", report.Blocks, color);
        RenderChanges(console, "Baked blocks", report.BakedBlocks, color);
        RenderChanges(console, "Chunks", report.Chunks, color);
        RenderMetadata(console, report, color);

        if (report.Embedded.Count == 0 && report.Items.Count == 0 && report.Blocks.Count == 0
            && report.BakedBlocks.Count == 0 && report.Chunks.Count == 0
            && report.MapUid is null && report.MapName is null && report.AuthorLogin is null
            && report.AuthorNickname is null && report.Password is null && sizeDelta == 0)
        {
            console.MarkupLine("[grey]No differences in the compared fields.[/]");
        }
    }

    private static void RenderChanges(IAnsiConsole console, string title, IReadOnlyList<Change> changes, bool color)
    {
        if (changes.Count == 0) return;
        var added = changes.Count(c => c.Left is null);
        var removed = changes.Count(c => c.Right is null);
        var changed = changes.Count(c => c.Left is not null && c.Right is not null);
        var summary = string.Join(" ", new[]
        {
            added > 0 ? Colorize($"+{added} added", "green", color) : null,
            removed > 0 ? Colorize($"-{removed} removed", "red", color) : null,
            changed > 0 ? Colorize($"~{changed} changed", "yellow", color) : null,
        }.Where(static text => text is not null));
        console.MarkupLine($"[bold]{Escape(title)}[/] [grey]({summary})[/]");
        foreach (var change in changes)
        {
            if (change.Left is null)
                console.MarkupLine($"{Colorize("+", "green", color)} {Escape(change.Right!)}");
            else if (change.Right is null)
                console.MarkupLine($"{Colorize("-", "red", color)} {Escape(change.Left)}");
            else
                console.MarkupLine($"{Colorize("~", "yellow", color)} {Escape(change.Key ?? "change")}: {Escape(change.Left)} [grey]\u2192[/] {Escape(change.Right)}");
        }
    }

    private static void RenderMetadata(IAnsiConsole console, DiffReport report, bool color)
    {
        foreach (var (label, change) in new[]
        {
            ("Map UID", report.MapUid), ("Map name", report.MapName),
            ("Author login", report.AuthorLogin), ("Author nickname", report.AuthorNickname),
            ("Password chunk", report.Password),
        })
        {
            if (change is not null)
                console.MarkupLine($"{Colorize("~", "yellow", color)} [bold]{Escape(label)}[/]: {Escape(change.Left ?? "removed")} [grey]\u2192[/] {Escape(change.Right ?? "added")}");
        }
    }

    private static string Delta(long value, bool color) => value switch
    {
        > 0 => Colorize($"(+{value:N0})", "red", color),
        < 0 => Colorize($"({value:N0})", "green", color),
        _ => "",
    };

    private static string Colorize(string text, string color, bool enabled) => enabled ? $"[{color}]{text}[/]" : text;
    private static string Escape(string text) => Markup.Escape(text.Replace("\u001b", "\\u001b").Replace("\r", "\\r").Replace("\n", "\\n"));
}

public sealed record DiffReport(
    long LeftBytes, long RightBytes, IReadOnlyList<Change> Blocks, IReadOnlyList<Change> BakedBlocks,
    IReadOnlyList<Change> Items, IReadOnlyList<Change> Embedded, IReadOnlyList<Change> Chunks,
    Change? MapUid, Change? MapName, Change? AuthorLogin, Change? AuthorNickname, Change? Password);

public sealed record Change(string? Left, string? Right, string? Key = null);
