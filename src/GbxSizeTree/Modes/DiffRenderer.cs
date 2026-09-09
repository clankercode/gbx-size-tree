using System.Globalization;
using System.Net;
using System.Text;
using GbxSizeTree.Cli.Rendering;
using GbxSizeTree.Measure;
using GbxSizeTree.Semantics;
using Spectre.Console;

namespace GbxSizeTree.Cli.Modes;

public static class DiffRenderer
{
    private static readonly EmbeddedSizePresentation EmbeddedSizes = new();

    private const string HtmlStyles = "body{font:14px system-ui;margin:24px;color:#18212b;background:#fafbfc}h2{overflow-wrap:anywhere}.summary,.metadata{padding:12px 16px;border:1px solid #d5dce3;border-radius:10px;background:#fff}.warning{color:#7a2e0b}.table-scroll{overflow-x:auto}table{border-collapse:collapse;margin:12px 0 28px;font-variant-numeric:tabular-nums}th,td{padding:8px 12px;text-align:left;white-space:nowrap}th{background:#e9eef3;border-bottom:1px solid #d5dce3}tbody+tbody tr:first-child td{border-top:1px solid #aab8c5;padding-top:16px}tbody tr:nth-child(even){background:#f0f4f7}td:first-child{font-weight:bold}.added td:first-child{color:#137333}.removed td:first-child{color:#b3261e}.changed td:first-child{color:#946000}td:nth-child(2){white-space:normal;min-width:240px;overflow-wrap:anywhere}.color-chip{display:inline-block;min-width:3.5em;padding:.18em .65em;border-radius:999px;text-align:center;line-height:1.25;font-weight:600;box-sizing:border-box}@media(max-width:600px){body{margin:12px}.summary,.metadata{padding:10px 12px}}";

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
        var delta = report.RightBytes - report.LeftBytes;
        RenderHeader(console, oldPath, newPath, report.LeftBytes, report.RightBytes, delta, color);
        foreach (var warning in report.Warnings)
            console.MarkupLine($"[bold]Warning:[/] {Escape(warning)}");
        if (report.Warnings.Count > 0) console.WriteLine();

        var tables = Tables(report).Where(t => t.Rows.Count > 0).ToArray();
        foreach (var source in tables)
        {
            var data = console.Profile.Width < 140 && source.Columns.Contains("What changed")
                ? source with
                {
                    Columns = ["Mark", "Name / path", "Details"],
                    Rows = source.Rows.Select(row => row with { Cells = [row.Cells[0], row.Cells[1],
                        $"ZIP: {row.Cells[2]}; Raw: {row.Cells[3]}; Ratio: {row.Cells[4]}; {row.Cells[5]}"] }).ToArray(),
                } : source;
            var table = new Table().Border(TableBorder.Simple);
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
                if (column is "Compressed" or "Uncompressed" or "Ratio" or "Variant" or "Subvariant") cell.RightAligned();
                table.AddColumn(cell);
            }
            string? previousGroup = null;
            foreach (var row in data.Rows)
            {
                if (previousGroup is not null && previousGroup != row.Group) table.AddEmptyRow();
                previousGroup = row.Group;
                var cells = row.Cells.Select(Escape).ToArray();
                cells[0] = Colorize(cells[0], MarkerColor(cells[0]), color);
                for (var i = 1; i < cells.Length; i++)
                    cells[i] = data.Columns[i] == "Color" && row.Color is { } change
                        ? ColorTransition(change, html: false, color)
                        : Colorize(cells[i], data.Columns[i] is "Pos" or "Position" or "Coord" ? "cyan"
                            : data.Columns[i] is "Rotation" or "Direction" ? "yellow" : "default", color);
                table.AddRow(cells);
            }
            console.MarkupLine($"[bold]{Escape(data.Title)}[/] [grey]({Escape(Summary(data.Rows))})[/]");
            if (data.Note is not null) console.MarkupLine(Escape(data.Note));
            console.Write(table);
            console.WriteLine();
        }

        RenderMetadata(console, Metadata(report).ToArray(), color);
        if (tables.Length == 0 && !Metadata(report).Any() && delta == 0)
            console.MarkupLine("[grey]No differences in the compared fields.[/]");
    }

    private static void RenderHeader(IAnsiConsole console, string oldPath, string newPath,
        long leftBytes, long rightBytes, long delta, bool color)
    {
        var width = Math.Max(20, console.Profile.Width);
        console.MarkupLine("[bold]Map diff[/]");
        if (width < 100)
        {
            console.MarkupLine($"[grey]Old[/]  {Escape(oldPath)}");
            console.MarkupLine($"[grey]New[/]  {Escape(newPath)}");
        }
        else
        {
            console.MarkupLine($"[grey]Old[/]  {Escape(oldPath)}");
            console.MarkupLine($"[grey]  ↓[/]");
            console.MarkupLine($"[grey]New[/]  {Escape(newPath)}");
        }
        console.MarkupLine($"[grey]Size[/] {leftBytes:N0} → {rightBytes:N0} bytes {Delta(delta, color)}");
        console.WriteLine();
    }

    private static void RenderMetadata(IAnsiConsole console, (string Label, Change Change)[] metadata, bool color)
    {
        if (metadata.Length == 0) return;
        console.MarkupLine("[bold]Metadata[/]");
        foreach (var (label, change) in metadata)
            console.MarkupLine($"  {Colorize("~", "yellow", color)} [bold]{Escape(label)}[/]: {Escape(change.Left ?? "removed")} [grey]→[/] {Escape(change.Right ?? "added")}");
        console.WriteLine();
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
            string? previousGroup = null;
            foreach (var row in table.Rows)
            {
                if (previousGroup is not null && previousGroup != row.Group)
                    b.AppendLine("| " + string.Join(" | ", table.Columns.Select(_ => "")) + " |");
                previousGroup = row.Group;
                b.AppendLine("| " + string.Join(" | ", row.Cells.Select(MarkdownCell)) + " |");
            }
        }
        foreach (var (label, change) in Metadata(report))
            b.AppendLine($"\n~ **{label}**: {MarkdownCell(change.Left ?? "removed")} → {MarkdownCell(change.Right ?? "added")}");
        if (IsEmpty(report)) b.AppendLine("\nNo differences in the compared fields.");
        return b.ToString();
    }

    public static string RenderHtml(DiffReport report, string oldPath, string newPath,
        bool? colorOption = null, bool styled = true)
    {
        var color = styled && (colorOption ?? string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")));
        var b = new StringBuilder("<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Map diff</title>");
        if (styled) b.Append($"<style>{HtmlStyles}</style>");
        b.Append("</head><body><main id=\"map-diff\" class=\"diff-report\">");
        b.Append($"<header id=\"diff-summary\" class=\"summary diff-summary\"><h2 id=\"diff-title\" class=\"diff-title\">Diff: <code class=\"old-path\">{Html(oldPath)}</code> → <code class=\"new-path\">{Html(newPath)}</code></h2><p id=\"diff-size\" class=\"diff-size\">Size: {report.LeftBytes:N0} → {report.RightBytes:N0} bytes</p></header>");
        if (report.Warnings.Count > 0)
        {
            b.Append("<aside id=\"diff-warnings\" class=\"warnings diff-warnings\" aria-label=\"Warnings\">");
            for (var i = 0; i < report.Warnings.Count; i++)
                b.Append($"<p id=\"warning-{i}\" class=\"warning\"><strong>Warning:</strong> {Html(report.Warnings[i])}</p>");
            b.Append("</aside>");
        }
        foreach (var table in Tables(report).Where(t => t.Rows.Count > 0))
            AppendHtmlTable(b, table, color);
        var metadata = Metadata(report).ToArray();
        if (metadata.Length > 0)
        {
            b.Append("<section id=\"diff-metadata\" class=\"metadata diff-metadata\" aria-labelledby=\"metadata-heading\"><h3 id=\"metadata-heading\" class=\"metadata-heading\">Metadata</h3>");
            for (var i = 0; i < metadata.Length; i++)
            {
                var (label, change) = metadata[i];
                b.Append($"<p id=\"metadata-row-{i}\" class=\"metadata-row changed\"><span class=\"change-marker\">~</span> <strong id=\"metadata-row-{i}-label\" class=\"metadata-label\">{Html(label)}</strong>: <span id=\"metadata-row-{i}-value\" class=\"metadata-value\">{Html(change.Left ?? "removed")} → {Html(change.Right ?? "added")}</span></p>");
            }
            b.Append("</section>");
        }
        if (IsEmpty(report)) b.Append("<p id=\"diff-empty-state\" class=\"empty-state\">No differences in the compared fields.</p>");
        b.Append("</main></body></html>");
        return b.ToString();
    }

    private static void AppendHtmlTable(StringBuilder b, DiffTable table, bool color)
    {
        var category = HtmlCategoryIdentifier(table.Title);
        var categoryId = $"category-{category}";
        var tableId = $"table-{category}";
        b.Append($"<section id=\"{categoryId}\" class=\"diff-category category-{category}\" aria-labelledby=\"{categoryId}-heading\">");
        b.Append($"<h3 id=\"{categoryId}-heading\" class=\"category-heading\">{Html(table.Title)} ({Html(Summary(table.Rows))})</h3>");
        if (table.Note is not null)
            b.Append($"<p id=\"{categoryId}-note\" class=\"category-note\">{Html(table.Note)}</p>");
        b.Append($"<div id=\"{categoryId}-scroll\" class=\"table-scroll\"><table id=\"{tableId}\" class=\"diff-table category-table\" aria-labelledby=\"{categoryId}-heading\"");
        if (table.Note is not null) b.Append($" aria-describedby=\"{categoryId}-note\"");
        b.Append($"><thead id=\"{tableId}-head\" class=\"table-header\"><tr id=\"{tableId}-header-row\" class=\"table-header-row\">");
        for (var i = 0; i < table.Columns.Length; i++)
        {
            var column = HtmlColumnIdentifier(table.Columns[i]);
            b.Append($"<th id=\"{tableId}-header-{i}-{column}\" class=\"table-header-cell column-{column}\" scope=\"col\">{Html(table.Columns[i])}</th>");
        }
        b.Append("</tr></thead>");
        string? previousGroup = null;
        var groupIndex = -1;
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            if (rowIndex == 0 || previousGroup != row.Group)
            {
                if (rowIndex > 0) b.Append("</tbody>");
                groupIndex++;
                b.Append($"<tbody id=\"{tableId}-group-{groupIndex}\" class=\"row-group\">");
            }
            previousGroup = row.Group;
            var rowClass = row.Cells[0] == "+" ? "added" : row.Cells[0] == "-" ? "removed" : "changed";
            b.Append($"<tr id=\"{tableId}-row-{rowIndex}\" class=\"diff-row {rowClass}\">");
            for (var i = 0; i < row.Cells.Length; i++)
            {
                var column = HtmlColumnIdentifier(table.Columns[i]);
                var cell = table.Columns[i] == "Color" && row.Color is { } change
                    ? ColorTransition(change, html: true, color)
                    : Html(row.Cells[i]);
                var markerClass = i == 0 ? " marker-cell" : "";
                b.Append($"<td id=\"{tableId}-row-{rowIndex}-cell-{i}\" class=\"diff-cell{markerClass} column-{column}\" headers=\"{tableId}-header-{i}-{column}\">{cell}</td>");
            }
            b.Append("</tr>");
        }
        b.Append("</tbody></table></div></section>");
    }

    private static string HtmlCategoryIdentifier(string title) => title switch
    {
        "Embedded files — added/removed" => "embedded-added-removed",
        "Embedded files — modified" => "embedded-modified",
        "Embedded item properties — modified" => "embedded-properties-modified",
        "Embedded outer-map contribution — non-additive LZO marginal bytes" => "embedded-contributions",
        "Placed items" => "placed-items",
        "Blocks" => "blocks",
        "Baked blocks" => "baked-blocks",
        "Map metadata" => "map-metadata",
        "Chunks" => "chunks",
        _ => throw new ArgumentOutOfRangeException(nameof(title), title, "Unknown HTML diff category."),
    };

    private static string HtmlColumnIdentifier(string column) => column switch
    {
        "Mark" => "mark",
        "Name / path" => "name-path",
        "Name" => "name",
        "Entry path" => "entry-path",
        "Property path" => "property-path",
        "Field" => "field",
        "Left" => "left",
        "Right" => "right",
        "Size" => "size",
        "Position" => "position",
        "Pos" => "position",
        "Coord" => "coordinate",
        "Rotation" => "rotation",
        "Direction" => "direction",
        "Variant" => "variant",
        "Subvariant" => "subvariant",
        "Mode" => "mode",
        "Ground" => "ground",
        "Color" => "color",
        "Pivot" => "pivot",
        "Animation" => "animation",
        "Lightmap" => "lightmap",
        "Flags" => "flags",
        "What changed" => "change-details",
        "Left marginal bytes" => "left-marginal-bytes",
        "Right marginal bytes" => "right-marginal-bytes",
        "ZIP bytes" => "zip-bytes",
        "Raw bytes" => "raw-bytes",
        "ZIP / raw" => "zip-raw-ratio",
        _ => throw new ArgumentOutOfRangeException(nameof(column), column, "Unknown HTML diff column."),
    };

    private static IEnumerable<DiffTable> Tables(DiffReport report)
    {
        yield return EmbeddedTable("Embedded files — added/removed", report.Embedded.Where(c => c.Left is null || c.Right is null));
        yield return EmbeddedTable("Embedded files — modified", report.Embedded.Where(c => c.Left is not null && c.Right is not null));
        yield return EmbeddedPropertyTable(report.EmbeddedPropertyChanges);
        yield return new("Embedded outer-map contribution — non-additive LZO marginal bytes",
            ["Mark", "Name / path", EmbeddedSizePresentation.LeftMarginalColumn,
                EmbeddedSizePresentation.RightMarginalColumn, EmbeddedSizePresentation.ZipColumn,
                EmbeddedSizePresentation.RawColumn],
            report.EmbeddedContributions.OrderBy(c => ChangeOrder(c.Left, c.Right))
                .ThenBy(c => DirectoryContext((c.Right ?? c.Left)!.Path), StringComparer.Ordinal)
                .ThenBy(c => (c.Right ?? c.Left)!.Path, StringComparer.Ordinal)
                .Select(c =>
                {
                    var cells = EmbeddedSizes.FormatEntry(ContributionEntry(c.Left), ContributionEntry(c.Right));
                    return new Row([Marker(c.Left, c.Right), (c.Right ?? c.Left)?.Path ?? "--",
                        ContributionMarginal(c.Left), ContributionMarginal(c.Right), cells.ZipBytes, cells.RawBytes],
                        Group: Marker(c.Left, c.Right) + DirectoryContext((c.Right ?? c.Left)!.Path));
                }).ToArray(),
            EmbeddedSizes.FormatOuterNote(report.LeftContributionBaselineBytes, report.RightContributionBaselineBytes) +
            $" Default: at most {new EmbeddedFileContributionOptions().MaxTrials} removal trials per map; remaining entries are unavailable.");
        yield return ItemTable(report.Items);
        yield return BlockTable("Blocks", report.Blocks);
        yield return BlockTable("Baked blocks", report.BakedBlocks, showGridPosition: true);
        yield return new("Map metadata", ["Mark", "Field", "Left", "Right"],
            report.MetadataChanges.Where(c => LegacyMetadata(report, c.Path) is null).OrderBy(c => c.Path, StringComparer.Ordinal)
                .Select(c => new Row([Marker(c.Left, c.Right), c.Path, MetadataValue(c.Left), MetadataValue(c.Right)],
                    Group: SectionContext(c.Path, '.'))).ToArray());
        yield return new("Chunks", ["Mark", "Name", "Size"], report.Chunks.OrderBy(c => c.Key, StringComparer.Ordinal)
            .Select(c => new Row([Marker(c.Left, c.Right), c.Key ?? "chunk", c.Left is not null && c.Right is not null ? $"{c.Left} → {c.Right}" : c.Left ?? c.Right ?? "--"],
                Group: SectionContext(c.Key ?? "chunk", ':'))).ToArray());
    }

    private static string MetadataValue(MapMetadataValue? value) => value switch
    {
        { Text: { } text } => $"\"{text}\"",
        { Integer: { } number } => number.ToString(CultureInfo.InvariantCulture),
        { Boolean: { } flag } => flag ? "true" : "false",
        _ => "absent",
    };

    private static EmbeddedSizePresentation.EntrySize? ContributionEntry(EmbeddedFileContribution? value) => value is null
        ? null
        : new(value.ZipCompressedBytes, value.ZipRawBytes);

    private static string ContributionMarginal(EmbeddedFileContribution? value) => value is null
        ? "absent"
        : EmbeddedSizes.FormatMarginal(value.MarginalCompressedBodyBytes, value.UnavailableReason);

    private static DiffTable EmbeddedTable(string title, IEnumerable<ValueChange<EmbeddedSnapshot>> changes) => new(
        title, ["Mark", "Name / path", EmbeddedSizePresentation.ZipColumn,
            EmbeddedSizePresentation.RawColumn, EmbeddedSizePresentation.RatioColumn, "What changed"],
        changes.OrderBy(c => ChangeOrder(c.Left, c.Right))
            .ThenBy(c => DirectoryContext((c.Right ?? c.Left)!.Path), StringComparer.Ordinal)
            .ThenBy(c => (c.Right ?? c.Left)!.Path, StringComparer.Ordinal).Select(c =>
            {
                var cells = EmbeddedSizes.FormatEntry(
                    c.Left is null ? null : new(c.Left.Compressed, c.Left.Uncompressed),
                    c.Right is null ? null : new(c.Right.Compressed, c.Right.Uncompressed));
                return new Row([Marker(c.Left, c.Right), Transition(c, x => DisplayPath(x.Path), onlyDifferent: true),
                    cells.ZipBytes, cells.RawBytes, cells.ZipToRawRatio, EmbeddedExplanation(c)],
                    Group: Marker(c.Left, c.Right) + DirectoryContext((c.Right ?? c.Left)!.Path));
            }).ToArray(),
        EmbeddedSizes.EntryNote +
        " Archive overhead and compression settings are not measured; a changed ZIP size with changed content does not by itself prove a compression-setting change.");

    private static string EmbeddedExplanation(ValueChange<EmbeddedSnapshot> change)
    {
        if (change.Left is null) return "Entry added";
        if (change.Right is null) return "Entry removed";
        var left = change.Left;
        var right = change.Right;
        var content = left.Sha256 != right.Sha256;
        var raw = left.Uncompressed != right.Uncompressed;
        var zip = left.Compressed != right.Compressed;
        var explanation = content
            ? "Content changed (SHA-256); " + (!raw && !zip ? "raw and ZIP sizes unchanged"
                : $"raw size {(raw ? "changed" : "unchanged")}; ZIP size {(zip ? "changed" : "unchanged")}")
            : raw ? "Hash unchanged but raw size changed (inconsistent snapshot); ZIP size " + (zip ? "changed" : "unchanged")
            : zip ? "Content unchanged; ZIP encoding size changed (compression-only)"
            : "Content and entry sizes unchanged";
        return left.Path == right.Path ? explanation : explanation + "; path changed";
    }

    private static DiffTable EmbeddedPropertyTable(IReadOnlyList<EmbeddedPropertyEntryDiff> entries) => new(
        "Embedded item properties — modified", ["Mark", "Entry path", "Property path", "Left", "Right"],
        entries.OrderBy(entry => entry.Path, StringComparer.Ordinal).SelectMany(entry =>
        {
            var rows = entry.Properties.Changes.Select(change => new Row(
                ["~", entry.Path, change.Path, FormatEmbeddedPropertyValue(change.Left), FormatEmbeddedPropertyValue(change.Right)],
                Group: entry.Path)).ToList();
            if (entry.Properties.Changes.Count == 0)
            {
                rows.Add(new Row(["~", entry.Path, "Content", "changed (SHA-256)",
                    "No supported property difference was available."], Group: entry.Path));
            }
            foreach (var issue in PropertyIssues(entry))
            {
                rows.Add(new Row(["~", entry.Path, $"{issue.Side} coverage: {issue.Issue.Path}",
                    issue.Side == "Left" ? $"{issue.Issue.Code}: {issue.Issue.Message}" : "--",
                    issue.Side == "Right" ? $"{issue.Issue.Code}: {issue.Issue.Message}" : "--"], Group: entry.Path));
            }
            return rows;
        }).ToArray(),
        "Only modified entries with changed SHA-256 are parsed. Coverage diagnostics are explicit; the content hash still proves that the entry changed.");

    private static IEnumerable<(string Side, EmbeddedPropertyIssue Issue)> PropertyIssues(EmbeddedPropertyEntryDiff entry) =>
        entry.Properties.LeftIssues.Select(issue => (Side: "Left", Issue: issue))
            .Concat(entry.Properties.RightIssues.Select(issue => (Side: "Right", Issue: issue)))
            .OrderBy(row => row.Issue.Path, StringComparer.Ordinal)
            .ThenBy(row => row.Issue.Code, StringComparer.Ordinal)
            .ThenBy(row => row.Side, StringComparer.Ordinal)
            .ThenBy(row => row.Issue.Message, StringComparer.Ordinal);

    private static string FormatEmbeddedPropertyValue(EmbeddedPropertyValue? value) => value switch
    {
        null => "absent",
        { Boolean: { } flag } => flag ? "boolean: true" : "boolean: false",
        { Integer: { } integer } => $"integer: {integer.ToString(CultureInfo.InvariantCulture)}",
        { Number: { } number } => $"number: {number.ToString("R", CultureInfo.InvariantCulture)}",
        { NumberBits: { } bits } => $"number: {value.Text} (bits {bits})",
        _ => $"text: \"{value.Text}\"",
    };

    private static DiffTable ItemTable(IReadOnlyList<ValueChange<ItemSnapshot>> changes)
    {
        var columns = new List<Column<ItemSnapshot>>
        {
            new("Name / path", x => CompactPath(x.Path)), new("Position", x => DisplayVector(x.PhysicalPosition)),
            new("Rotation", x => DisplayVector(x.Rotation)), new("Color", x => x.Color),
        };
        var values = changes.SelectMany(c => new[] { c.Left, c.Right }).OfType<ItemSnapshot>().ToArray();
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
        DiffSpatialGroups.Order(changes, position, key).Select(entry =>
        {
            var c = entry.Change;
            return new Row(new[] { Marker(c.Left, c.Right) }.Concat(columns.Select(col => Transition(c, col.Value, onlyDifferent: true))).ToArray(),
                new(c.Left is null ? null : color(c.Left), c.Right is null ? null : color(c.Right)),
                entry.Group.ToString(CultureInfo.InvariantCulture));
        }).ToArray());

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

    private static Change? LegacyMetadata(DiffReport report, string path) => path switch
    {
        "map.uid" => report.MapUid,
        "map.name" => report.MapName,
        "author.login" => report.AuthorLogin,
        "author.nickname" => report.AuthorNickname,
        "security.passwordPresent" => report.Password,
        _ => null,
    };

    private static IEnumerable<(string Label, Change Change)> Metadata(DiffReport r)
    {
        foreach (var (label, change) in new[] { ("Map UID", r.MapUid), ("Map name", r.MapName), ("Author login", r.AuthorLogin), ("Author name", r.AuthorNickname), ("Plaintext password", r.Password) })
            if (change is not null) yield return (label, change);
    }

    private static bool IsEmpty(DiffReport r) => r.LeftBytes == r.RightBytes && r.Embedded.Count == 0 && r.Items.Count == 0
        && r.Blocks.Count == 0 && r.BakedBlocks.Count == 0 && r.Chunks.Count == 0
        && r.MetadataChanges.Count == 0 && r.EmbeddedContributions.Count == 0
        && r.EmbeddedPropertyChanges.Count == 0 && !Metadata(r).Any();

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
            ? $"<span class=\"color-chip\" style=\"background-color:{background};color:{foreground}\">{text}</span>"
            : $"[{foreground} on {background}] {text} [/]";
    }

    private static string DirectoryContext(string path)
    {
        path = path.Replace('\\', '/');
        var slash = path.LastIndexOf('/');
        return slash < 0 ? "" : path[..slash];
    }

    private static string SectionContext(string path, char separator) => path.Split(separator)[0];
    private static int ChangeOrder(object? left, object? right) => right is null ? 0 : left is null ? 1 : 2;

    private sealed record Column<T>(string Name, Func<T, string> Value);
    private sealed record Row(string[] Cells, ValueChange<string>? Color = null, string Group = "");
    private sealed record DiffTable(string Title, string[] Columns, IReadOnlyList<Row> Rows, string? Note = null);
}
