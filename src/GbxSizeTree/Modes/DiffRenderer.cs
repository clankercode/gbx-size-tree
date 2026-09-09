using System.Globalization;
using System.Net;
using System.Text;
using GbxSizeTree.Measure;
using Spectre.Console;

namespace GbxSizeTree.Cli.Modes;

public static class DiffRenderer
{
    public static bool ShouldUseColor(bool? requested, bool redirected, string? terminal, bool noColor) =>
        requested ?? (!redirected && !noColor && !string.Equals(terminal, "dumb", StringComparison.OrdinalIgnoreCase));

    public static IAnsiConsole BuildConsole(bool? colorOption)
    {
        var enabled = ShouldUseColor(colorOption, Console.IsOutputRedirected,
            Environment.GetEnvironmentVariable("TERM"), !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")));
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
        foreach (var warning in report.Warnings)
            console.MarkupLine($"[bold]Warning:[/] {Escape(warning)}");
        var tables = Tables(report).Where(t => t.Rows.Count > 0).ToArray();
        foreach (var data in tables)
        {
            var table = new Table().Border(TableBorder.Simple).ShowRowSeparators();
            foreach (var column in data.Columns)
            {
                var heading = console.Profile.Width < 140 ? column switch
                {
                    "Compressed" => "Comp. B", "Uncompressed" => "Raw B", "Position" => "Pos",
                    "Rotation" => "Rot", "Direction" => "Dir", "Variant" => "Var", "Subvariant" => "Sub",
                    "Animation" => "Anim", "Lightmap" => "Light", "Left marginal bytes" => "Left B",
                    "Right marginal bytes" => "Right B", "ZIP bytes" => "ZIP B", "Raw bytes" => "Raw B", _ => column,
                } : column;
                var cell = new TableColumn(new Markup($"[bold]{Escape(heading)}[/]"));
                if (column is "Compressed" or "Uncompressed" or "Ratio" or "Scale" or "Variant" or "Subvariant") cell.RightAligned();
                table.AddColumn(cell);
            }
            foreach (var row in data.Rows)
            {
                var cells = row.Cells.Select(Escape).ToArray();
                cells[0] = Colorize(cells[0], MarkerColor(cells[0]), color);
                for (var i = 1; i < cells.Length; i++)
                    cells[i] = data.Columns[i] == "Color" && row.Color is { } change
                        ? ColorTransition(change, html: false, color)
                        : Colorize(cells[i], data.Columns[i] is "Pos" or "Position" or "Coord" ? "cyan"
                            : data.Columns[i] is "Rotation" or "Direction" ? "yellow" : "default", color);
                table.AddRow(cells);
            }
            console.MarkupLine($"[bold]{data.Title}[/] [grey]({Summary(data.Rows)})[/]");
            if (data.Note is not null) console.MarkupLine(Escape(data.Note));
            console.Write(table);
            console.WriteLine();
        }
        var metadata = Metadata(report).ToArray();
        foreach (var (label, change) in metadata)
            console.MarkupLine($"{Colorize("~", "yellow", color)} [bold]{label}[/]: {Escape(change.Left ?? "removed")} [grey]→[/] {Escape(change.Right ?? "added")}");
        if (tables.Length == 0 && metadata.Length == 0 && delta == 0)
            console.MarkupLine("[grey]No differences in the compared fields.[/]");
    }

    public static string RenderMarkdown(DiffReport report, string oldPath, string newPath)
    {
        var b = new StringBuilder($"## Diff: {MarkdownCell(oldPath)} → {MarkdownCell(newPath)}\n\nSize: {report.LeftBytes:N0} → {report.RightBytes:N0} bytes\n");
        foreach (var warning in report.Warnings)
            b.AppendLine($"\n**Warning:** {MarkdownCell(warning)}");
        foreach (var table in Tables(report).Where(t => t.Rows.Count > 0))
        {
            b.Append($"\n### {table.Title} ({Summary(table.Rows)})\n\n");
            if (table.Note is not null) b.AppendLine(MarkdownCell(table.Note) + "\n");
            b.AppendLine("| " + string.Join(" | ", table.Columns) + " |");
            b.AppendLine("| " + string.Join(" | ", table.Columns.Select(_ => "---")) + " |");
            foreach (var row in table.Rows)
                b.AppendLine("| " + string.Join(" | ", row.Cells.Select(MarkdownCell)) + " |");
        }
        foreach (var (label, change) in Metadata(report))
            b.AppendLine($"\n~ **{label}**: {MarkdownCell(change.Left ?? "removed")} → {MarkdownCell(change.Right ?? "added")}");
        if (IsEmpty(report)) b.AppendLine("\nNo differences in the compared fields.");
        return b.ToString();
    }

    public static string RenderHtml(DiffReport report, string oldPath, string newPath, bool? colorOption = null)
    {
        var color = colorOption ?? string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
        var b = new StringBuilder("<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Map diff</title><style>body{font:14px system-ui;margin:24px;color:#18212b;background:#fafbfc}h2{overflow-wrap:anywhere}section{overflow-x:auto}table{border-collapse:collapse;margin:12px 0 28px;font-variant-numeric:tabular-nums}th,td{padding:8px 12px;border-bottom:1px solid #d5dce3;text-align:left;white-space:nowrap}th{background:#e9eef3}tbody tr:nth-child(even){background:#f0f4f7}td:first-child{font-weight:bold}.added td:first-child{color:#137333}.removed td:first-child{color:#b3261e}.changed td:first-child{color:#946000}td:nth-child(2){white-space:normal;min-width:240px;overflow-wrap:anywhere}@media(max-width:600px){body{margin:12px}}</style></head><body>");
        b.Append($"<h2>Diff: <code>{Html(oldPath)}</code> → <code>{Html(newPath)}</code></h2><p>Size: {report.LeftBytes:N0} → {report.RightBytes:N0} bytes</p>");
        foreach (var warning in report.Warnings)
            b.Append($"<p class=\"warning\"><strong>Warning:</strong> {Html(warning)}</p>");
        foreach (var table in Tables(report).Where(t => t.Rows.Count > 0))
        {
            b.Append($"<h3>{table.Title} ({Summary(table.Rows)})</h3>");
            if (table.Note is not null) b.Append($"<p>{Html(table.Note)}</p>");
            b.Append("<section><table><thead><tr>");
            foreach (var column in table.Columns) b.Append($"<th>{Html(column)}</th>");
            b.Append("</tr></thead><tbody>");
            foreach (var row in table.Rows)
            {
                b.Append($"<tr class=\"{(row.Cells[0] == "+" ? "added" : row.Cells[0] == "-" ? "removed" : "changed")}\">");
                for (var i = 0; i < row.Cells.Length; i++)
                {
                    var cell = table.Columns[i] == "Color" && row.Color is { } change
                        ? ColorTransition(change, html: true, color)
                        : Html(row.Cells[i]);
                    b.Append($"<td>{cell}</td>");
                }
                b.Append("</tr>");
            }
            b.Append("</tbody></table></section>");
        }
        foreach (var (label, change) in Metadata(report))
            b.Append($"<p>~ <strong>{label}</strong>: {Html(change.Left ?? "removed")} → {Html(change.Right ?? "added")}</p>");
        if (IsEmpty(report)) b.Append("<p>No differences in the compared fields.</p>");
        b.Append("</body></html>");
        return b.ToString();
    }

    private static IEnumerable<DiffTable> Tables(DiffReport report)
    {
        yield return EmbeddedTable("Embedded files — added/removed", report.Embedded.Where(c => c.Left is null || c.Right is null));
        yield return EmbeddedTable("Embedded files — modified", report.Embedded.Where(c => c.Left is not null && c.Right is not null));
        yield return new("Embedded outer-map contribution — non-additive LZO marginal bytes",
            ["Mark", "Name / path", "Left marginal bytes", "Right marginal bytes", "ZIP bytes", "Raw bytes"],
            report.EmbeddedContributions.OrderBy(c => (c.Right ?? c.Left)?.Path, StringComparer.Ordinal)
                .Select(c => new Row([Marker(c.Left, c.Right), (c.Right ?? c.Left)?.Path ?? "--",
                    ContributionValue(c.Left), ContributionValue(c.Right),
                    Transition(c, x => Bytes(x.ZipCompressedBytes)), Transition(c, x => Bytes(x.ZipRawBytes))])).ToArray(),
            $"Original-body recompressed LZO baseline bytes: left {Bytes(report.LeftContributionBaselineBytes)}, right {Bytes(report.RightContributionBaselineBytes)}. " +
            "Marginal bytes are signed baseline-minus-removal savings; context-dependent and non-additive, not an allocation of map size. ZIP/raw bytes are independent. Default: at most 8 removal trials per map; remaining entries are unavailable.");
        yield return ItemTable(report.Items);
        yield return BlockTable("Blocks", report.Blocks);
        yield return BlockTable("Baked blocks", report.BakedBlocks, showGridPosition: true);
        yield return new("Map metadata", ["Mark", "Field", "Left", "Right"],
            report.MetadataChanges.OrderBy(c => c.Path, StringComparer.Ordinal)
                .Select(c => new Row([Marker(c.Left, c.Right), c.Path, MetadataValue(c.Left), MetadataValue(c.Right)])).ToArray());
        yield return new("Chunks", ["Mark", "Name", "Size"], report.Chunks.OrderBy(c => c.Key, StringComparer.Ordinal)
            .Select(c => new Row([Marker(c.Left, c.Right), c.Key ?? "chunk", c.Left is not null && c.Right is not null ? $"{c.Left} → {c.Right}" : c.Left ?? c.Right ?? "--"])).ToArray());
    }

    private static string MetadataValue(MapMetadataValue? value) => value switch
    {
        { Text: { } text } => $"\"{text}\"",
        { Integer: { } number } => number.ToString(CultureInfo.InvariantCulture),
        { Boolean: { } flag } => flag ? "true" : "false",
        _ => "absent",
    };

    private static string Bytes(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "unavailable";
    private static string ContributionValue(EmbeddedFileContribution? value) => value is null ? "absent"
        : value.MarginalCompressedBodyBytes is { } bytes ? Bytes(bytes)
        : $"unavailable: {value.UnavailableReason ?? "not measured"}";

    private static DiffTable EmbeddedTable(string title, IEnumerable<ValueChange<EmbeddedSnapshot>> changes) => new(
        title, ["Mark", "Name / path", "Compressed", "Uncompressed", "Ratio"],
        changes.OrderBy(c => (c.Right ?? c.Left)?.Path, StringComparer.Ordinal).Select(c => new Row(
            [Marker(c.Left, c.Right), Transition(c, x => DisplayPath(x.Path), onlyDifferent: true),
                Transition(c, x => x.Compressed.ToString(CultureInfo.InvariantCulture)),
                Transition(c, x => x.Uncompressed.ToString(CultureInfo.InvariantCulture)),
                Transition(c, x => x.Uncompressed == 0 ? "--" : x.Ratio.ToString("P2", CultureInfo.InvariantCulture))])).ToArray());

    private static DiffTable ItemTable(IReadOnlyList<ValueChange<ItemSnapshot>> changes)
    {
        var columns = new List<Column<ItemSnapshot>>
        {
            new("Name / path", x => CompactPath(x.Path)), new("Position", x => DisplayVector(x.PhysicalPosition)),
            new("Rotation", x => DisplayVector(x.Rotation)), new("Color", x => x.Color),
        };
        var values = changes.SelectMany(c => new[] { c.Left, c.Right }).OfType<ItemSnapshot>().ToArray();
        if (values.Any(x => x.Scale != 1)) columns.Add(new("Scale", x => x.Scale.ToString("0.0##", CultureInfo.InvariantCulture)));
        if (values.Any(x => x.Pivot != default)) columns.Add(new("Pivot", x => DisplayVector(x.Pivot)));
        AddVarying(columns, values, "Animation", x => x.AnimationPhase);
        AddVarying(columns, values, "Lightmap", x => x.LightmapQuality);
        AddVarying(columns, values, "Flags", x => x.Flags.ToString(CultureInfo.InvariantCulture));
        return SpatialTable("Placed items", changes, columns, x => x.PhysicalPosition, x => x.Key, x => x.Color);
    }

    private static DiffTable BlockTable(string title, IReadOnlyList<ValueChange<BlockSnapshot>> changes, bool showGridPosition = false)
    {
        var columns = new List<Column<BlockSnapshot>>
        {
            new("Name", x => x.Name), new("Coord", x => x.IsFree ? "--" : x.Coord),
            new("Pos", x => x.IsFree || showGridPosition ? DisplayVector(x.PhysicalPosition) : "--"),
            new("Direction", x => x.IsFree ? "--" : x.Direction),
            new("Variant", x => x.Variant.ToString(CultureInfo.InvariantCulture)),
            new("Subvariant", x => x.SubVariant.ToString(CultureInfo.InvariantCulture)),
        };
        var values = changes.SelectMany(c => new[] { c.Left, c.Right }).OfType<BlockSnapshot>().ToArray();
        if (values.Any(x => x.IsFree)) columns.Add(new("Rotation", x => DisplayVector(x.Rotation)));
        if (values.Any(x => x.IsFree || x.IsGhost)) columns.Add(new("Mode", x => x.IsFree ? "Free" : x.IsGhost ? "Ghost" : "Normal"));
        AddVarying(columns, values, "Ground", x => x.IsGround.ToString());
        if (values.Any(x => x.Color != "Default")) columns.Add(new("Color", x => x.Color));
        AddVarying(columns, values, "Lightmap", x => x.LightmapQuality);
        AddVarying(columns, values, "Flags", x => x.Flags.ToString(CultureInfo.InvariantCulture));
        return SpatialTable(title, changes, columns, x => x.PhysicalPosition, x => x.Key, x => x.Color);
    }

    private static void AddVarying<T>(List<Column<T>> columns, T[] values, string name, Func<T, string> value) where T : class
    {
        if (values.Select(value).Distinct(StringComparer.Ordinal).Skip(1).Any()) columns.Add(new(name, value));
    }

    private static DiffTable SpatialTable<T>(string title, IReadOnlyList<ValueChange<T>> changes,
        List<Column<T>> columns, Func<T, SpatialPosition?> position, Func<T, string> key, Func<T, string> color) where T : class => new(
        title, new[] { "Mark" }.Concat(columns.Select(c => c.Name)).ToArray(),
        changes.OrderBy(c => position((c.Right ?? c.Left)!))
            .ThenBy(c => key((c.Right ?? c.Left)!), StringComparer.Ordinal)
            .ThenBy(c => c.Left is null ? 1 : 0)
            .Select(c => new Row(new[] { Marker(c.Left, c.Right) }.Concat(columns.Select(col => Transition(c, col.Value, onlyDifferent: true))).ToArray(),
                new(c.Left is null ? null : color(c.Left), c.Right is null ? null : color(c.Right)))).ToArray());

    private static string Transition<T>(ValueChange<T> change, Func<T, string> value, bool onlyDifferent = false) where T : class
    {
        if (change.Left is null) return change.Right is null ? "--" : value(change.Right);
        if (change.Right is null) return value(change.Left);
        var a = value(change.Left);
        var b = value(change.Right);
        return a == b && onlyDifferent ? a : $"{a} → {b}";
    }

    private static string DisplayVector(SpatialPosition? position) => position is { } p
        ? FormattableString.Invariant($"({p.X:0.0##}, {p.Y:0.0##}, {p.Z:0.0##})") : "--";

    public static string CompactPath(string path)
    {
        var separator = path.Contains('\\') ? '\\' : '/';
        var parts = path.Replace('\\', '/').Split('/');
        for (var i = 0; i < parts.Length - 1; i++)
            if (parts[i].Length > 4) parts[i] = parts[i][..3] + "…";
        if (parts[^1].EndsWith(".Item.Gbx", StringComparison.OrdinalIgnoreCase)) parts[^1] = parts[^1][..^9];
        return string.Join(separator, parts);
    }

    private static string DisplayPath(string path) => path.StartsWith("Embedded/items/", StringComparison.OrdinalIgnoreCase)
        ? path["Embedded/items/".Length..] : path;

    private static IEnumerable<(string Label, Change Change)> Metadata(DiffReport r)
    {
        foreach (var (label, change) in new[] { ("Map UID", r.MapUid), ("Map name", r.MapName), ("Author login", r.AuthorLogin), ("Author nickname", r.AuthorNickname), ("Password chunk", r.Password) })
            if (change is not null) yield return (label, change);
    }

    private static bool IsEmpty(DiffReport r) => r.LeftBytes == r.RightBytes && r.Embedded.Count == 0 && r.Items.Count == 0
        && r.Blocks.Count == 0 && r.BakedBlocks.Count == 0 && r.Chunks.Count == 0
        && r.MetadataChanges.Count == 0 && r.EmbeddedContributions.Count == 0 && !Metadata(r).Any();

    private static string Summary(IReadOnlyList<Row> rows) => string.Join(" ", new[] { ("+", "added"), ("-", "removed"), ("~", "changed") }
        .Select(x => (x.Item1, x.Item2, Count: rows.Count(r => r.Cells[0] == x.Item1)))
        .Where(x => x.Count > 0).Select(x => $"{x.Item1}{x.Count} {x.Item2}"));
    private static string Marker(object? left, object? right) => left is null ? "+" : right is null ? "-" : "~";
    private static string MarkerColor(string marker) => marker == "+" ? "green" : marker == "-" ? "red" : "yellow";
    private static string Delta(long v, bool color) => v > 0 ? Colorize($"(+{v:N0})", "red", color) : v < 0 ? Colorize($"({v:N0})", "green", color) : "";
    private static string Colorize(string text, string color, bool enabled) => enabled ? $"[{color}]{text}[/]" : text;
    private static string Safe(string text) => string.Concat(text.Select(c => char.IsControl(c) ? $"\\u{(int)c:x4}" : c.ToString()));
    private static string Escape(string text) => Markup.Escape(Safe(text));
    private static string Html(string text) => WebUtility.HtmlEncode(Safe(text));
    private static string MarkdownCell(string text)
    {
        var b = new StringBuilder();
        foreach (var c in Safe(text))
        {
            if ("\\`*_{}[]()#+-!|>~".Contains(c)) b.Append('\\');
            b.Append(c == '<' ? "&lt;" : c == '&' ? "&amp;" : c.ToString());
        }
        return b.ToString();
    }

    private static string ColorTransition(ValueChange<string> change, bool html, bool enabled)
    {
        if (change.Left is null) return ColorChip(change.Right!, html, enabled);
        if (change.Right is null) return ColorChip(change.Left, html, enabled);
        if (change.Left == change.Right) return ColorChip(change.Left, html, enabled);
        return $"{ColorChip(change.Left, html, enabled)} → {ColorChip(change.Right, html, enabled)}";
    }

    private static string ColorChip(string label, bool html, bool enabled)
    {
        var (background, foreground) = label switch
        {
            "White" => ("#ffffff", "#000000"),
            "Green" => ("#008000", "#ffffff"),
            "Blue" => ("#0000ff", "#ffffff"),
            "Red" => ("#ff0000", "#000000"),
            "Black" => ("#000000", "#ffffff"),
            _ => ((string?)null, (string?)null),
        };
        var text = html ? Html(label) : Escape(label);
        if (!enabled || background is null) return text;
        return html
            ? $"<span style=\"background-color:{background};color:{foreground};white-space:pre\"> {text} </span>"
            : $"[{foreground} on {background}] {text} [/]";
    }

    private sealed record Column<T>(string Name, Func<T, string> Value);
    private sealed record Row(string[] Cells, ValueChange<string>? Color = null);
    private sealed record DiffTable(string Title, string[] Columns, IReadOnlyList<Row> Rows, string? Note = null);
}
