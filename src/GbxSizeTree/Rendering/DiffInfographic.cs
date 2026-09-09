using System.Globalization;
using GbxSizeTree.Cli.Modes;
using GbxSizeTree.Measure;
using GbxSizeTree.Semantics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace GbxSizeTree.Cli.Rendering;

public static class DiffInfographic
{
    public const int Width = 1400;
    public const int MinimumHeight = 900;
    public const int MaximumHeight = 2200;
    private const int MaxSpatialChanges = 400;
    private const int MaxSpatialContext = 900;
    private const int MaxHighlightLines = 5;
    private const int MaxPropertyLines = 5;
    private const int MaxMetadataLines = 5;
    private const int MaxChunkLines = 4;

    public static Image<Rgba32> Render(DiffReport report, string oldPath, string newPath)
    {
        ArgumentNullException.ThrowIfNull(report);
        var scene = BuildScene(report, oldPath, newPath);
        return DiffInfographicPainter.Paint(scene);
    }

    public static DiffInfographicScene BuildScene(DiffReport report, string oldPath, string newPath)
    {
        ArgumentNullException.ThrowIfNull(report);
        var placementChanges = PlacementChanges(report).ToArray();
        var counts = CountEntities(report, placementChanges);
        var detailCounts = new DiffInfographicDetailCounts(
            MetadataEntries(report).Count,
            report.EmbeddedPropertyChanges.Sum(x => x.Properties.Changes.Count),
            report.Chunks.Count);

        var positionedChanges = placementChanges.Where(x => x.Position is not null).ToArray();
        var finiteChanges = positionedChanges.Where(x => IsFinite(x.Position!.Value)).ToArray();
        var invalidChanges = positionedChanges.Length - finiteChanges.Length;
        var unpositionedChanges = placementChanges.Length - positionedChanges.Length;
        var rawContext = Context(report).ToArray();
        var finiteContext = rawContext.Where(IsFinite).ToArray();
        var invalidContext = rawContext.Length - finiteContext.Length;
        var plotted = Sample(finiteChanges, MaxSpatialChanges).ToArray();
        var sampledContext = Sample(finiteContext, MaxSpatialContext)
            .Select(x => (Position: x, Kind: DiffInfographicChangeKind.Changed, Label: "context"))
            .ToArray();
        var spatial = Normalize(plotted, sampledContext,
            finiteChanges.Length - plotted.Length, invalidChanges, unpositionedChanges,
            finiteContext.Length - sampledContext.Length, invalidContext);

        var sectionContent = new List<(string Id, string Title, IReadOnlyList<string> Lines)>();
        var highlights = Highlights(report);
        if (highlights.Count > 0) sectionContent.Add(("embedded-highlights", "Embedded highlights", highlights));
        var properties = Properties(report);
        if (properties.Count > 0) sectionContent.Add(("deep-properties", $"Deep properties · {detailCounts.DeepProperties:N0}", properties));
        var warnings = Warnings(report, spatial).ToArray();
        if (warnings.Length > 0) sectionContent.Add(("coverage", "Coverage notes", warnings));
        var metadata = Metadata(report);
        if (metadata.Count > 0) sectionContent.Add(("metadata", $"Metadata · {detailCounts.Metadata:N0}", metadata));
        var chunks = Chunks(report);
        if (chunks.Count > 0) sectionContent.Add(("chunks", $"Chunk observations · {detailCounts.Chunks:N0}", chunks));

        var sections = new List<DiffInfographicSection>
        {
            new("hero", "Map size", [], 0, 0, Width, 380),
            new("change-counts", "Exact change counts", [], 70, 380, 1330, 550),
            new("spatial-context", "XZ spatial context", [], 70, 570, 1330, 1010),
        };
        var columnTops = new[] { 1040, 1040 };
        foreach (var content in sectionContent)
        {
            var column = columnTops[0] <= columnTops[1] ? 0 : 1;
            var left = column == 0 ? 70 : 710;
            var requestedBottom = columnTops[column] + 88 + Math.Max(1, content.Lines.Count) * 57;
            sections.Add(new(content.Id, content.Title, content.Lines, left, columnTops[column], left + 620, requestedBottom));
            columnTops[column] = requestedBottom + 22;
        }
        var height = Math.Clamp(Math.Max(columnTops[0], columnTops[1]) + 24, MinimumHeight, MaximumHeight);

        return new DiffInfographicScene(Width, height,
            DiffInfographicText.FileName(oldPath), DiffInfographicText.FileName(newPath),
            report.LeftBytes, report.RightBytes, SaturatingSubtract(report.RightBytes, report.LeftBytes),
            counts, "PLACEMENTS + EMBEDDED", detailCounts, spatial, sections, warnings);
    }

    private static DiffInfographicCounts CountEntities(DiffReport report,
        IReadOnlyList<(SpatialPosition? Position, DiffInfographicChangeKind Kind, string Label)> placements)
    {
        var added = placements.Count(x => x.Kind == DiffInfographicChangeKind.Added);
        var removed = placements.Count(x => x.Kind == DiffInfographicChangeKind.Removed);
        var changed = placements.Count(x => x.Kind == DiffInfographicChangeKind.Changed);
        Count(report.Embedded, ref added, ref removed, ref changed);
        return new(added, removed, changed);

        static void Count<T>(IEnumerable<ValueChange<T>> values, ref int added, ref int removed, ref int changed) where T : class
        {
            foreach (var value in values)
            {
                if (value.Left is null) added++;
                else if (value.Right is null) removed++;
                else changed++;
            }
        }
    }

    private static IEnumerable<(SpatialPosition? Position, DiffInfographicChangeKind Kind, string Label)> PlacementChanges(DiffReport report)
    {
        foreach (var change in report.Blocks.Concat(report.BakedBlocks))
        {
            var value = change.Right ?? change.Left;
            if (value is not null)
                yield return (value.PhysicalPosition, Kind(change.Left, change.Right), value.Name);
        }
        foreach (var change in report.Items)
        {
            var value = change.Right ?? change.Left;
            if (value is not null)
                yield return (value.PhysicalPosition, Kind(change.Left, change.Right), value.Path);
        }
    }

    private static IEnumerable<SpatialPosition> Context(DiffReport report) =>
        report.LeftBlockSnapshots.Select(x => x.PhysicalPosition).Concat(report.RightBlockSnapshots.Select(x => x.PhysicalPosition))
            .Concat(report.LeftBakedSnapshots.Select(x => x.PhysicalPosition)).Concat(report.RightBakedSnapshots.Select(x => x.PhysicalPosition))
            .Concat(report.LeftItemSnapshots.Select(x => (SpatialPosition?)x.PhysicalPosition)).Concat(report.RightItemSnapshots.Select(x => (SpatialPosition?)x.PhysicalPosition))
            .OfType<SpatialPosition>();

    private static DiffInfographicSpatialScene Normalize(
        IReadOnlyList<(SpatialPosition? Position, DiffInfographicChangeKind Kind, string Label)> changes,
        IReadOnlyList<(SpatialPosition Position, DiffInfographicChangeKind Kind, string Label)> context,
        int sampledOutChanges, int invalidChanges, int unpositionedChanges,
        int sampledOutContext, int invalidContext)
    {
        var changePositions = changes.Select(x => x.Position!.Value).ToArray();
        var points = changePositions.Concat(context.Select(x => x.Position)).ToArray();
        var xs = points.Select(x => x.X).Order().ToArray();
        var zs = points.Select(x => x.Z).Order().ToArray();
        var minX = Quantile(xs, .01);
        var maxX = Quantile(xs, .99);
        var minZ = Quantile(zs, .01);
        var maxZ = Quantile(zs, .99);
        ExpandEqualRange(ref minX, ref maxX);
        ExpandEqualRange(ref minZ, ref maxZ);
        var edgePinnedChanges = changePositions.Count(p => p.X < minX || p.X > maxX || p.Z < minZ || p.Z > maxZ);
        var edgePinnedContext = context.Count(x => x.Position.X < minX || x.Position.X > maxX || x.Position.Z < minZ || x.Position.Z > maxZ);
        DiffInfographicPoint N(SpatialPosition p, DiffInfographicChangeKind kind, string label) => new(
            NormalizeAxis(p.X, minX, maxX), NormalizeAxis(p.Z, minZ, maxZ), kind, DiffInfographicText.Clean(label));
        var normalizedChanges = changes.Select(x => N(x.Position!.Value, x.Kind, x.Label)).ToArray();
        var normalizedContext = context.Select(x => N(x.Position, x.Kind, x.Label)).ToArray();
        var range = FormattableString.Invariant($"X {DiffInfographicText.Position(minX)}–{DiffInfographicText.Position(maxX)} m · Z {DiffInfographicText.Position(minZ)}–{DiffInfographicText.Position(maxZ)} m");
        var coverage = $"{normalizedChanges.Length:N0} changes plotted";
        if (sampledOutChanges > 0) coverage += $" · {sampledOutChanges:N0} changes sampled out";
        if (unpositionedChanges > 0) coverage += $" · {unpositionedChanges:N0} unpositioned";
        if (invalidChanges > 0) coverage += $" · {invalidChanges:N0} invalid";
        coverage += $" · {normalizedContext.Length:N0} context";
        if (sampledOutContext > 0) coverage += $" · {sampledOutContext:N0} context sampled out";
        return new(normalizedContext, normalizedChanges, normalizedChanges.Length,
            sampledOutChanges, invalidChanges, unpositionedChanges,
            normalizedContext.Length, sampledOutContext, invalidContext,
            edgePinnedChanges, edgePinnedContext, range, coverage);

        static double Quantile(double[] values, double fraction)
        {
            if (values.Length == 0) return fraction == 0 ? 0 : 1;
            return values[(int)Math.Round((values.Length - 1) * fraction, MidpointRounding.AwayFromZero)];
        }

        static void ExpandEqualRange(ref double minimum, ref double maximum)
        {
            if (maximum > minimum) return;
            if (minimum == 0) { maximum = 1; return; }
            var delta = Math.Abs(minimum) * 1e-12;
            if (!double.IsFinite(delta) || delta == 0) delta = 1;
            minimum = Math.Max(-double.MaxValue, minimum - delta);
            maximum = Math.Min(double.MaxValue, maximum + delta);
            if (maximum <= minimum) minimum = Math.BitDecrement(minimum);
            if (maximum <= minimum) maximum = Math.BitIncrement(maximum);
        }

        static float NormalizeAxis(double value, double minimum, double maximum)
        {
            if (value <= minimum) return 0;
            if (value >= maximum) return 1;
            var midpoint = minimum / 2 + maximum / 2;
            var halfRange = maximum / 2 - minimum / 2;
            var normalized = halfRange > 0 && double.IsFinite(halfRange)
                ? .5 + (value / 2 - midpoint / 2) / halfRange
                : .5;
            return (float)Math.Clamp(double.IsFinite(normalized) ? normalized : .5, 0, 1);
        }
    }

    private static IReadOnlyList<string> Highlights(DiffReport report)
    {
        var lines = new List<string>();
        var ranked = report.Embedded.Select(c =>
        {
            var value = c.Right ?? c.Left!;
            var delta = SaturatingSubtract(c.Right?.Compressed ?? 0, c.Left?.Compressed ?? 0);
            var kind = c.Left is null ? "+" : c.Right is null ? "−" : "~";
            var detail = c.Left is not null && c.Right is not null
                ? $"ZIP {DiffInfographicText.Bytes(c.Left.Compressed)} to {DiffInfographicText.Bytes(c.Right.Compressed)} ({SignedBytes(delta)})"
                : $"ZIP {DiffInfographicText.Bytes(value.Compressed)}";
            return (Rank: Math.Abs((double)delta), Text: $"{kind} {value.Path}  ·  {detail}");
        }).OrderByDescending(x => x.Rank).ThenBy(x => x.Text, StringComparer.Ordinal).ToArray();
        lines.AddRange(ranked.Select(x => x.Text));

        var highlightedPaths = new HashSet<string>(report.Embedded.Select(x => (x.Right ?? x.Left)!.Path), StringComparer.Ordinal);
        foreach (var contribution in report.EmbeddedContributions)
        {
            var value = contribution.Right ?? contribution.Left;
            if (value is null || !highlightedPaths.Add(value.Path)) continue;
            var unavailable = value.UnavailableReason;
            var detail = unavailable is null
                ? "Outer-map marginal available (non-additive; not summed)"
                : $"Outer-map marginal unavailable: {unavailable}";
            lines.Add($"~ {value.Path}  ·  {detail}");
        }
        var unavailableSides = report.EmbeddedContributions
            .SelectMany(x => new[] { x.Left, x.Right })
            .OfType<EmbeddedFileContribution>()
            .Where(x => x.UnavailableReason is not null)
            .ToArray();
        string? note = null;
        if (unavailableSides.Length > 0)
        {
            note = $"Marginal measurements are non-additive; {unavailableSides.Length:N0} unavailable contribution side{(unavailableSides.Length == 1 ? "" : "s")}: {unavailableSides[0].UnavailableReason}";
        }
        else if (report.EmbeddedContributions.Count > 0)
        {
            note = "Outer-map marginal measurements are non-additive and are never summed.";
        }
        return LimitLines(lines, MaxHighlightLines, "embedded highlight", note);
    }

    private static IReadOnlyList<string> Properties(DiffReport report)
    {
        var rows = report.EmbeddedPropertyChanges.SelectMany(entry => entry.Properties.Changes.Select(change =>
            $"{entry.Path} › {change.Path}: {Property(change.Left)} to {Property(change.Right)}")).ToList();
        var issues = report.EmbeddedPropertyChanges.Sum(x => x.Properties.LeftIssues.Count + x.Properties.RightIssues.Count);
        var note = issues > 0
            ? $"WARNING · {issues:N0} opaque or partial-coverage diagnostic{(issues == 1 ? "" : "s")}; content hashes still prove the entries changed."
            : null;
        return LimitLines(rows, MaxPropertyLines, "deep-property detail", note);
    }

    private sealed record MetadataEntry(string Label, string? Left, string? Right);

    private static IReadOnlyList<MetadataEntry> MetadataEntries(DiffReport report)
    {
        var changes = new List<MetadataEntry>();
        Add("Map UID", report.MapUid); Add("Map name", report.MapName); Add("Author login", report.AuthorLogin);
        Add("Author name", report.AuthorNickname); Add("Password present", report.Password);
        changes.AddRange(report.MetadataChanges.Where(x => LegacyMetadata(report, x.Path) is null)
            .Select(x => new MetadataEntry(x.Path, MetaOrNull(x.Left), MetaOrNull(x.Right))));
        return changes.Distinct().ToArray();

        void Add(string label, Change? change)
        {
            if (change is not null) changes.Add(new(label, change.Left, change.Right));
        }
    }

    private static IReadOnlyList<string> Metadata(DiffReport report)
    {
        var rows = MetadataEntries(report).Select(row =>
        {
            var marker = row.Left is null ? "+" : row.Right is null ? "−" : "~";
            var value = row.Left is null ? row.Right! : row.Right is null ? row.Left : $"{row.Left} to {row.Right}";
            return $"{marker} {row.Label}: {value}";
        }).ToArray();
        return LimitLines(rows, MaxMetadataLines, "metadata change");
    }

    private static IReadOnlyList<string> Chunks(DiffReport report)
    {
        var rows = report.Chunks.Select(chunk =>
        {
            var marker = chunk.Left is null ? "+" : chunk.Right is null ? "−" : "~";
            var value = chunk.Left is null ? chunk.Right! : chunk.Right is null ? chunk.Left : $"{chunk.Left} to {chunk.Right}";
            return $"{marker} {chunk.Key ?? "chunk"}: {value}";
        }).ToArray();
        return LimitLines(rows, MaxChunkLines, "chunk observation");
    }

    private static IReadOnlyList<string> LimitLines(IReadOnlyList<string> details, int limit, string description, string? finalNote = null)
    {
        var detailLimit = limit - (finalNote is null ? 0 : 1);
        var visible = details.Take(detailLimit).ToList();
        var omitted = details.Count - visible.Count;
        if (omitted > 0)
        {
            visible = details.Take(detailLimit - 1).ToList();
            omitted = details.Count - visible.Count;
            visible.Add($"+ {omitted:N0} more {description}{(omitted == 1 ? "" : "s")} omitted");
        }
        if (finalNote is not null) visible.Add(finalNote);
        return visible;
    }

    private static IEnumerable<string> Warnings(DiffReport report, DiffInfographicSpatialScene spatial)
    {
        foreach (var warning in report.Warnings.Take(3)) yield return "WARNING · " + warning;
        if (report.Warnings.Count > 3) yield return $"+ {report.Warnings.Count - 3:N0} more warnings omitted";
        if (spatial.SampledOutChanges > 0) yield return $"WARNING · {spatial.SampledOutChanges:N0} placement changes sampled out of the plot; hero counts include them.";
        if (spatial.InvalidChangePositions > 0) yield return $"WARNING · {spatial.InvalidChangePositions:N0} invalid change position{(spatial.InvalidChangePositions == 1 ? "" : "s")} could not be plotted.";
        if (spatial.UnpositionedChanges > 0) yield return $"NOTE · {spatial.UnpositionedChanges:N0} unpositioned change{(spatial.UnpositionedChanges == 1 ? "" : "s")} included in hero counts but not the plot.";
        if (spatial.SampledOutContext > 0) yield return $"NOTE · {spatial.SampledOutContext:N0} context points sampled out of the plot.";
        if (spatial.InvalidContextPositions > 0) yield return $"NOTE · {spatial.InvalidContextPositions:N0} invalid context position{(spatial.InvalidContextPositions == 1 ? "" : "s")} could not be plotted.";
        if (spatial.EdgePinnedChanges > 0) yield return $"NOTE · {spatial.EdgePinnedChanges:N0} plotted change{(spatial.EdgePinnedChanges == 1 ? "" : "s")} outside the 1st–99th percentile range pinned to the plot edge.";
        if (spatial.EdgePinnedContext > 0) yield return $"NOTE · {spatial.EdgePinnedContext:N0} plotted context point{(spatial.EdgePinnedContext == 1 ? "" : "s")} outside the 1st–99th percentile range pinned to the plot edge.";
    }

    private static string Property(EmbeddedPropertyValue? value) => value switch
    {
        null => "absent",
        { Boolean: { } boolean } => boolean ? "true" : "false",
        { Integer: { } integer } => integer.ToString(CultureInfo.InvariantCulture),
        { Number: { } number } when double.IsFinite(number) => number.ToString("0.###", CultureInfo.InvariantCulture),
        _ => value.Text,
    };

    private static string? MetaOrNull(MapMetadataValue? value) => value switch
    {
        null => null,
        { Text: { } text } => text,
        { Integer: { } integer } => integer.ToString(CultureInfo.InvariantCulture),
        { Boolean: { } boolean } => boolean ? "true" : "false",
        _ => "absent",
    };

    private static Change? LegacyMetadata(DiffReport report, string path) => path switch
    {
        "map.uid" => report.MapUid,
        "map.name" => report.MapName,
        "author.login" => report.AuthorLogin,
        "author.nickname" => report.AuthorNickname,
        "security.passwordPresent" => report.Password,
        _ => null,
    };

    private static bool IsFinite(SpatialPosition x) => double.IsFinite(x.X) && double.IsFinite(x.Z);
    private static DiffInfographicChangeKind Kind(object? left, object? right) => left is null
        ? DiffInfographicChangeKind.Added : right is null ? DiffInfographicChangeKind.Removed : DiffInfographicChangeKind.Changed;
    private static IEnumerable<T> Sample<T>(IReadOnlyList<T> values, int limit)
    {
        if (values.Count <= limit) return values;
        return Enumerable.Range(0, limit).Select(i => values[(int)((long)i * values.Count / limit)]);
    }
    private static long SaturatingSubtract(long right, long left)
    {
        if (left < 0 && right > long.MaxValue + left) return long.MaxValue;
        if (left > 0 && right < long.MinValue + left) return long.MinValue;
        return right - left;
    }

    private static string SignedBytes(long value) => value == 0 ? "0 B" : (value > 0 ? "+" : "−") + DiffInfographicText.Bytes(value == long.MinValue ? long.MaxValue : Math.Abs(value));
}
