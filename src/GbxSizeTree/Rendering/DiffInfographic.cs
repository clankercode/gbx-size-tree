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
    private const int MaxSpatialChanges = 400;
    private const int MaxSpatialContext = 900;
    private const int MaxHighlightLines = 5;
    private const int MaxEmbeddedRowsPerSection = 300;
    private const int MaxPropertyLines = 5;
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
        var placements = new DiffInfographicCountGroup("Placements", CountKinds(placementChanges));
        var embedded = new DiffInfographicCountGroup("Embedded", Count(report.Embedded));
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

        var sectionContent = new List<(string Id, string Title, IReadOnlyList<DiffInfographicSectionLine> Lines, DiffInfographicTable? Table)>();
        var embeddedRows = EmbeddedRows(report);
        var highlights = Highlights(report, embeddedRows);
        if (highlights.Rows.Count > 0 || highlights.Notes.Count > 0)
            sectionContent.Add(("embedded-highlights", "Embedded highlights", highlights.Notes, new(highlights.Rows)));
        if (embeddedRows.Count > 0)
        {
            var pageCount = (embeddedRows.Count + MaxEmbeddedRowsPerSection - 1) / MaxEmbeddedRowsPerSection;
            for (var page = 0; page < pageCount; page++)
            {
                var pageRows = embeddedRows.Skip(page * MaxEmbeddedRowsPerSection)
                    .Take(MaxEmbeddedRowsPerSection)
                    .Select(x => x.Row)
                    .ToArray();
                var id = page == 0 ? "embedded-changes" : $"embedded-changes-{page + 1}";
                var continuation = page == 0 ? "" : $" · continued {page + 1:N0}/{pageCount:N0}";
                sectionContent.Add((id, $"All embedded changes · {embeddedRows.Count:N0}{continuation}", [],
                    new(pageRows, DiffInfographicTableDensity.Compact)));
            }
        }
        var properties = Properties(report);
        if (properties.Count > 0) sectionContent.Add(("deep-properties", $"Deep properties · {detailCounts.DeepProperties:N0}", properties, null));
        var warnings = Warnings(report, spatial).ToArray();
        if (warnings.Length > 0) sectionContent.Add(("coverage", "Coverage notes", warnings.Select(x => new DiffInfographicSectionLine(x)).ToArray(), null));
        var metadata = Metadata(report);
        if (metadata.Count > 0) sectionContent.Add(("metadata", $"Metadata · {detailCounts.Metadata:N0}", metadata, null));
        var chunks = Chunks(report);
        if (chunks.Count > 0) sectionContent.Add(("chunks", $"Chunk observations · {detailCounts.Chunks:N0}", chunks, null));
        var placementSummary = PlacementSummary(placementChanges);
        if (placementSummary.Count > 0) sectionContent.Add(("placement-summary", $"Placement summary · {placementSummary.Count:N0} names", placementSummary, null));

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
            var requestedBottom = columnTops[column]
                + DiffInfographicLayout.SectionHeight(content.Lines.Count, content.Table);
            sections.Add(new(content.Id, content.Title, content.Lines, left, columnTops[column], left + 620, requestedBottom, content.Table));
            columnTops[column] = requestedBottom + 22;
        }
        var height = Math.Max(Math.Max(columnTops[0], columnTops[1]) + 24, MinimumHeight);

        return new DiffInfographicScene(Width, height,
            DiffInfographicText.FileName(oldPath), DiffInfographicText.FileName(newPath),
            report.LeftBytes, report.RightBytes, SaturatingSubtract(report.RightBytes, report.LeftBytes),
            placements, embedded, detailCounts, spatial, sections, warnings);
    }

    private static DiffInfographicCounts CountKinds(
        IReadOnlyList<(SpatialPosition? Position, DiffInfographicChangeKind Kind, string Label)> changes)
    {
        var added = changes.Count(x => x.Kind == DiffInfographicChangeKind.Added);
        var removed = changes.Count(x => x.Kind == DiffInfographicChangeKind.Removed);
        var changed = changes.Count(x => x.Kind == DiffInfographicChangeKind.Changed);
        return new(added, removed, changed);
    }

    private static DiffInfographicCounts Count<T>(IEnumerable<ValueChange<T>> values) where T : class
    {
        var added = 0;
        var removed = 0;
        var changed = 0;
        foreach (var value in values)
        {
            if (value.Left is null) added++;
            else if (value.Right is null) removed++;
            else changed++;
        }
        return new(added, removed, changed);
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

    private sealed record EmbeddedHighlights(
        IReadOnlyList<DiffInfographicTableRow> Rows,
        IReadOnlyList<DiffInfographicSectionLine> Notes);

    private sealed record EmbeddedChangeRow(long SizeDeltaBytes, DiffInfographicTableRow Row);

    private static IReadOnlyList<EmbeddedChangeRow> EmbeddedRows(DiffReport report) =>
        report.Embedded.Select(change =>
        {
            var value = change.Right ?? change.Left!;
            var kind = Kind(change.Left, change.Right);
            var delta = SaturatingSubtract(change.Right?.Compressed ?? 0, change.Left?.Compressed ?? 0);
            return new EmbeddedChangeRow(delta, new(
                KindMarker(kind).ToString(),
                DiffInfographicText.CleanPath(value.Path),
                SignedBytes(delta),
                kind));
        }).OrderBy(x => x.Row.Path, StringComparer.Ordinal).ToArray();

    private static EmbeddedHighlights Highlights(
        DiffReport report,
        IReadOnlyList<EmbeddedChangeRow> embeddedRows)
    {
        var ranked = embeddedRows.Select(row =>
            (Rank: AbsoluteMagnitude(row.SizeDeltaBytes), row.Row))
            .OrderByDescending(x => x.Rank)
            .ThenBy(x => x.Row.Path, StringComparer.Ordinal)
            .Take(MaxHighlightLines)
            .Select(x => x.Row)
            .OrderBy(x => x.Path, StringComparer.Ordinal)
            .ToArray();

        var notes = new List<DiffInfographicSectionLine>();
        var omitted = embeddedRows.Count - ranked.Length;
        if (omitted > 0) notes.Add($"+ {omitted:N0} more embedded change{(omitted == 1 ? "" : "s")} omitted");

        var unavailableSides = report.EmbeddedContributions
            .SelectMany(x => new[] { x.Left, x.Right })
            .OfType<EmbeddedFileContribution>()
            .Where(x => x.UnavailableReason is not null)
            .ToArray();
        if (unavailableSides.Length > 0)
        {
            notes.Add($"Marginal measurements are non-additive; {unavailableSides.Length:N0} unavailable contribution side{(unavailableSides.Length == 1 ? "" : "s")}: {unavailableSides[0].UnavailableReason}");
        }
        else if (report.EmbeddedContributions.Count > 0)
        {
            notes.Add("Outer-map marginal measurements are non-additive and are never summed.");
        }
        return new(ranked, notes);
    }

    private static IReadOnlyList<DiffInfographicSectionLine> Properties(DiffReport report)
    {
        var rows = report.EmbeddedPropertyChanges.SelectMany(entry => entry.Properties.Changes.Select(change =>
            new DiffInfographicSectionLine($"{entry.Path} › {change.Path}: {Property(change.Left)} to {Property(change.Right)}"))).ToList();
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

    private static IReadOnlyList<DiffInfographicSectionLine> Metadata(DiffReport report)
    {
        var rows = new List<DiffInfographicSectionLine>();
        foreach (var row in MetadataEntries(report))
        {
            if (row.Left is not null && row.Right is not null)
            {
                rows.Add($"~ {row.Label}");
                rows.Add(new DiffInfographicSectionLine($"− {row.Left}", Mono: true));
                rows.Add(new DiffInfographicSectionLine($"+ {row.Right}", Mono: true));
            }
            else
            {
                var marker = row.Left is null ? "+" : "−";
                var value = row.Left is null ? row.Right! : row.Left;
                rows.Add(new DiffInfographicSectionLine($"{marker} {row.Label}: {value}", Mono: true));
            }
        }
        return rows;
    }

    private static IReadOnlyList<DiffInfographicSectionLine> PlacementSummary(
        IReadOnlyList<(SpatialPosition? Position, DiffInfographicChangeKind Kind, string Label)> changes)
    {
        return changes.GroupBy(x => (x.Kind, Label: DiffInfographicText.Clean(x.Label)))
            .Select(g => (Count: g.Count(), g.Key.Kind, g.Key.Label))
            .OrderByDescending(x => x.Count).ThenBy(x => x.Label, StringComparer.Ordinal)
            .Select(x => new DiffInfographicSectionLine($"{KindMarker(x.Kind)} {x.Count:N0}x {x.Label}", Mono: true))
            .ToArray();
    }

    private static char KindMarker(DiffInfographicChangeKind kind) => kind switch
    {
        DiffInfographicChangeKind.Added => '+',
        DiffInfographicChangeKind.Removed => '−',
        _ => '~',
    };

    private static IReadOnlyList<DiffInfographicSectionLine> Chunks(DiffReport report)
    {
        var rows = report.Chunks.Select(chunk =>
        {
            var marker = chunk.Left is null ? "+" : chunk.Right is null ? "−" : "~";
            var value = chunk.Left is null ? chunk.Right! : chunk.Right is null ? chunk.Left : $"{chunk.Left} to {chunk.Right}";
            return new DiffInfographicSectionLine($"{marker} {chunk.Key ?? "chunk"}: {value}");
        }).ToArray();
        return LimitLines(rows, MaxChunkLines, "chunk observation");
    }

    private static IReadOnlyList<DiffInfographicSectionLine> LimitLines(IReadOnlyList<DiffInfographicSectionLine> details, int limit, string description, string? finalNote = null)
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
    private static ulong AbsoluteMagnitude(long value) => value >= 0 ? (ulong)value : (ulong)(-(value + 1)) + 1;

    private static long SaturatingSubtract(long right, long left)
    {
        if (left < 0 && right > long.MaxValue + left) return long.MaxValue;
        if (left > 0 && right < long.MinValue + left) return long.MinValue;
        return right - left;
    }

    private static string SignedBytes(long value) => value == 0 ? "0 B" : (value > 0 ? "+" : "−") + DiffInfographicText.Bytes(value == long.MinValue ? long.MaxValue : Math.Abs(value));
}
