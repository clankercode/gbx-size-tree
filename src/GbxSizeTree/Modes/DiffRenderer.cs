using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using GbxSizeTree.Cli.Modes;
using GbxSizeTree.Cli.Output;
using GbxSizeTree.Cli.Rendering;
using Spectre.Console;

namespace GbxSizeTree.Cli.Modes;

public static class DiffRenderer
{
    private static readonly Regex Attribute = new(@"(?:^|\|)(?<name>coord|position|pos)=(?<value>[^|]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Allows callers to replace the embedded-size presentation without changing diff data.</summary>
    public static Func<string, string> EmbeddedSizeFormatter { get; set; } = FormatEmbeddedSize;

    public static bool ShouldUseColor(bool? requested, bool redirected, string? terminal, bool noColor) =>
        requested ?? (!redirected && !noColor && !string.Equals(terminal, "dumb", StringComparison.OrdinalIgnoreCase));

    public static IAnsiConsole BuildConsole(bool? colorOption)
    {
        var enabled = colorOption ?? (!Console.IsOutputRedirected
            && !string.Equals(Environment.GetEnvironmentVariable("TERM"), "dumb", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")));
        return AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(Console.Out),
            Ansi = enabled ? AnsiSupport.Yes : AnsiSupport.No,
            ColorSystem = enabled ? ColorSystemSupport.Detect : ColorSystemSupport.NoColors,
        });
    }

    public static void Render(IAnsiConsole console, DiffReport report, string oldPath, string newPath)
    {
        var color = console.Profile.Capabilities.ColorSystem != ColorSystem.NoColors;
        console.MarkupLine($"[bold]Diff[/]: {Escape(oldPath)} [grey]→[/] {Escape(newPath)}");
        var delta = report.RightBytes - report.LeftBytes;
        console.MarkupLine($"Size: {report.LeftBytes:N0} [grey]→[/] {report.RightBytes:N0} bytes {Delta(delta, color)}");
        RenderChanges(console, "Embedded files", report.Embedded, color, embedded: true);
        RenderChanges(console, "Placed items", report.Items, color, item: true);
        RenderChanges(console, "Blocks", report.Blocks, color, coordinates: true);
        RenderChanges(console, "Baked blocks", report.BakedBlocks, color, coordinates: true);
        RenderChanges(console, "Chunks", report.Chunks, color);
        RenderMetadata(console, report, color);
        if (report.Embedded.Count == 0 && report.Items.Count == 0 && report.Blocks.Count == 0 && report.BakedBlocks.Count == 0
            && report.Chunks.Count == 0 && report.MapUid is null && report.MapName is null && report.AuthorLogin is null
            && report.AuthorNickname is null && report.Password is null && delta == 0)
            console.MarkupLine("[grey]No differences in the compared fields.[/]");
    }

    public static string RenderMarkdown(DiffReport report, string oldPath, string newPath)
    {
        var b = new StringBuilder($"## Diff: `{oldPath}` → `{newPath}`\n\nSize: {report.LeftBytes:N0} → {report.RightBytes:N0} bytes\n");
        AppendMarkdown(b, "Embedded files", report.Embedded, true, false, false);
        AppendMarkdown(b, "Placed items", report.Items, false, true, false);
        AppendMarkdown(b, "Blocks", report.Blocks, false, false, true);
        AppendMarkdown(b, "Baked blocks", report.BakedBlocks, false, false, true);
        AppendMarkdown(b, "Chunks", report.Chunks, false, false, false);
        return b.ToString();
    }

    public static string RenderHtml(DiffReport report, string oldPath, string newPath)
    {
        var b = new StringBuilder($"<h2>Diff: <code>{WebUtility.HtmlEncode(oldPath)}</code> → <code>{WebUtility.HtmlEncode(newPath)}</code></h2><p>Size: {report.LeftBytes:N0} → {report.RightBytes:N0} bytes</p>");
        AppendHtml(b, "Embedded files", report.Embedded, true, false, false);
        AppendHtml(b, "Placed items", report.Items, false, true, false);
        AppendHtml(b, "Blocks", report.Blocks, false, false, true);
        AppendHtml(b, "Baked blocks", report.BakedBlocks, false, false, true);
        AppendHtml(b, "Chunks", report.Chunks, false, false, false);
        return b.ToString();
    }

    private static void RenderChanges(IAnsiConsole console, string title, IReadOnlyList<Change> changes, bool color, bool embedded = false, bool item = false, bool coordinates = false)
    {
        if (changes.Count == 0) return;
        var table = new Table().Border(TableBorder.None).AddColumn(" ").AddColumn("Path");
        if (coordinates) { table.AddColumn("Coord"); table.AddColumn("Pos"); }
        if (embedded) table.AddColumn("Size (map / compressed; raw; ratio)");
        foreach (var change in changes)
        {
            var value = change.Right ?? change.Left ?? change.Key ?? "change";
            var marker = change.Left is null ? "+" : change.Right is null ? "-" : "~";
            var path = Compact(value, embedded, item);
            var coord = coordinates ? Field(value, "coord") : null;
            var pos = coordinates ? Field(value, "position") ?? Field(value, "pos") : null;
            var size = embedded ? EmbeddedSizeFormatter(value) : null;
            var row = new List<string> { Colorize(marker, marker == "+" ? "green" : marker == "-" ? "red" : "yellow", color), Escape(path) };
            if (coordinates) { row.Add(Escape(coord ?? "--")); row.Add(Escape(pos ?? "--")); }
            if (embedded) row.Add(Escape(size!));
            table.AddRow(row.ToArray());
        }
        console.MarkupLine($"[bold]{Escape(title)}[/] [grey]({Summary(changes, color)})[/]");
        console.Write(table);
        console.WriteLine();
    }

    private static string Summary(IReadOnlyList<Change> c, bool color) => string.Join(" ", new[] { Colorize($"+{c.Count(x => x.Left is null)} added", "green", color), Colorize($"-{c.Count(x => x.Right is null)} removed", "red", color), Colorize($"~{c.Count(x => x.Left is not null && x.Right is not null)} changed", "yellow", color) }.Where(x => !x.StartsWith("+0") && !x.StartsWith("-0") && !x.StartsWith("~0")));
    private static string Compact(string value, bool embedded, bool item)
    {
        var s = value;
        var separator = s.IndexOf('|');
        var path = separator >= 0 ? s[..separator] : s;
        var suffix = separator >= 0 ? s[separator..] : string.Empty;
        if (embedded && path.StartsWith("Embedded/items/", StringComparison.OrdinalIgnoreCase))
            path = path[15..];
        if (embedded || item)
        {
            var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length; i++)
            {
                if (i == parts.Length - 1)
                    parts[i] = parts[i].EndsWith(".Item.Gbx", StringComparison.OrdinalIgnoreCase)
                        ? parts[i][..^9] : parts[i];
                else if (parts[i].Length > 4)
                    parts[i] = parts[i][..3] + '…';
            }
            path = string.Join('\\', parts);
        }
        return path + suffix;
    }
    private static string? Field(string value, string name) => Attribute.Matches(value).FirstOrDefault(m => string.Equals(m.Groups["name"].Value, name, StringComparison.OrdinalIgnoreCase))?.Groups["value"].Value;

    public static string FormatEmbeddedSize(string value)
    {
        var size = Regex.Match(value, @"(?:map|compressed|raw|uncompressed|size)\s*[:=]\s*([0-9][0-9,]*)", RegexOptions.IgnoreCase);
        return size.Success ? size.Groups[1].Value : "--";
    }

    private static string MarkdownCell(string value) => value.Replace("\\", "\\\\").Replace("|", "\\|").Replace("\r", "").Replace("\n", "\\n");

    private static void AppendMarkdown(StringBuilder b, string title, IReadOnlyList<Change> changes, bool embedded, bool item, bool coordinates)
    {
        if (changes.Count == 0) return;
        b.Append($"\n### {title}\n\n| Mark | Path |" + (coordinates ? " Coord | Pos |" : "") + (embedded ? " Size |" : "") + "\n|---|---|" + (coordinates ? "---|---|" : "") + (embedded ? "---|" : "") + "\n");
        foreach (var c in changes)
        {
            var v = c.Right ?? c.Left ?? c.Key ?? "change";
            b.Append($"| {(c.Left is null ? "+" : c.Right is null ? "-" : "~")} | {MarkdownCell(Compact(v, embedded, item))} |");
            if (coordinates) b.Append($" {MarkdownCell(Field(v, "coord") ?? "--")} | {MarkdownCell(Field(v, "position") ?? Field(v, "pos") ?? "--")} |");
            if (embedded) b.Append($" {MarkdownCell(EmbeddedSizeFormatter(v))} |");
            b.AppendLine();
        }
    }

    private static void AppendHtml(StringBuilder b, string title, IReadOnlyList<Change> changes, bool embedded, bool item, bool coordinates) { if (changes.Count == 0) return; b.Append($"<h3>{title}</h3><table><thead><tr><th>Mark</th><th>Path</th>{(coordinates ? "<th>Coord</th><th>Pos</th>" : "")}{(embedded ? "<th>Size</th>" : "")}</tr></thead><tbody>"); foreach (var c in changes) { var v = c.Right ?? c.Left ?? c.Key ?? "change"; b.Append($"<tr><td>{(c.Left is null ? "+" : c.Right is null ? "-" : "~")}</td><td>{WebUtility.HtmlEncode(Compact(v, embedded, item))}</td>"); if (coordinates) b.Append($"<td>{WebUtility.HtmlEncode(Field(v, "coord") ?? "--")}</td><td>{WebUtility.HtmlEncode(Field(v, "position") ?? Field(v, "pos") ?? "--")}</td>"); if (embedded) b.Append($"<td>{WebUtility.HtmlEncode(EmbeddedSizeFormatter(v))}</td>"); b.Append("</tr>"); } b.Append("</tbody></table>"); }
    private static void RenderMetadata(IAnsiConsole c, DiffReport r, bool color) { foreach (var (label, change) in new[] { ("Map UID", r.MapUid), ("Map name", r.MapName), ("Author login", r.AuthorLogin), ("Author nickname", r.AuthorNickname), ("Password chunk", r.Password) }) if (change is not null) c.MarkupLine($"{Colorize("~", "yellow", color)} [bold]{Escape(label)}[/]: {Escape(change.Left ?? "removed")} [grey]→[/] {Escape(change.Right ?? "added")}"); }
    private static string Delta(long v, bool color) => v > 0 ? Colorize($"(+{v:N0})", "red", color) : v < 0 ? Colorize($"({v:N0})", "green", color) : "";
    private static string Colorize(string text, string color, bool enabled) => enabled ? $"[{color}]{text}[/]" : text;
    private static string Escape(string text) => Markup.Escape(text.Replace("\u001b", "\\u001b").Replace("\r", "\\r").Replace("\n", "\\n"));
}

public sealed record DiffReport(long LeftBytes, long RightBytes, IReadOnlyList<Change> Blocks, IReadOnlyList<Change> BakedBlocks, IReadOnlyList<Change> Items, IReadOnlyList<Change> Embedded, IReadOnlyList<Change> Chunks, Change? MapUid, Change? MapName, Change? AuthorLogin, Change? AuthorNickname, Change? Password);
public sealed record Change(string? Left, string? Right, string? Key = null);
