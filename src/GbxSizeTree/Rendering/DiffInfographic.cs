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
    private const int MaxHighlights = 5;
    private const int MaxProperties = 5;
    private const int MaxMetadata = 4;

    public static Image<Rgba32> Render(DiffReport report, string oldPath, string newPath)
    {
        ArgumentNullException.ThrowIfNull(report);
        var scene = BuildScene(report, oldPath, newPath);
        return DiffInfographicPainter.Paint(scene);
    }

    public static DiffInfographicScene BuildScene(DiffReport report, string oldPath, string newPath)
    {
        ArgumentNullException.ThrowIfNull(report);
        var allChanges = AllChanges(report).ToArray();
        var counts = CountChanges(report, allChanges);

        var finiteChanges = allChanges.Where(x => IsFinite(x.Position)).ToArray();
        var rawContext = Context(report).ToArray();
        var ignored = allChanges.Length - finiteChanges.Length + rawContext.Count(x => !IsFinite(x));
        var plotted = Sample(finiteChanges, MaxSpatialChanges).ToArray();
        var context = Sample(rawContext.Where(IsFinite).ToArray(), MaxSpatialContext)
            .Select(x => new DiffInfographicPoint((float)x.X, (float)x.Z, DiffInfographicChangeKind.Changed, "context"))
            .ToArray();
        var spatial = Normalize(plotted, context, finiteChanges.Length - plotted.Length, ignored);

        var sectionContent = new List<(string Id, string Title, IReadOnlyList<string> Lines)>();
        var highlights = Highlights(report).ToArray();
        if (highlights.Length > 0) sectionContent.Add(("embedded-highlights", "Embedded highlights", highlights));
        var properties = Properties(report).ToArray();
        if (properties.Length > 0) sectionContent.Add(("deep-properties", "Deep property changes", properties));
        var metadata = Metadata(report).ToArray();
        if (metadata.Length > 0) sectionContent.Add(("metadata", "Metadata", metadata));
        var warnings = Warnings(report, spatial).ToArray();
        if (warnings.Length > 0) sectionContent.Add(("coverage", "Coverage notes", warnings));

        var sections = new List<DiffInfographicSection>
        {
            new("hero", "Map size", [], 0, 0, Width, 380),
            new("change-counts", "Exact change counts", [], 70, 380, 1330, 550),
            new("spatial-context", "XZ spatial context", [], 70, 570, 1330, 1010),
        };
        var columnTops = new[] { 1040, 1040 };
        for (var i = 0; i < sectionContent.Count; i++)
        {
            var content = sectionContent[i];
            var column = columnTops[0] <= columnTops[1] ? 0 : 1;
            var left = column == 0 ? 70 : 710;
            var requestedBottom = columnTops[column] + 88 + Math.Max(1, content.Lines.Count) * 55;
            sections.Add(new(content.Id, content.Title, content.Lines, left, columnTops[column], left + 620, requestedBottom));
            columnTops[column] = requestedBottom + 22;
        }
        var height = Math.Clamp(Math.Max(columnTops[0], columnTops[1]) + 24, MinimumHeight, MaximumHeight);
        sections = sections.Select(x => x with { Bottom = Math.Min(x.Bottom, height - 36) }).Where(x => x.Top < height - 36).ToList();

        return new DiffInfographicScene(Width, height,
            DiffInfographicText.FileName(oldPath), DiffInfographicText.FileName(newPath),
            report.LeftBytes, report.RightBytes, SaturatingSubtract(report.RightBytes, report.LeftBytes),
            counts, spatial, sections, warnings);
    }

    private static DiffInfographicCounts CountChanges(DiffReport report,
        IReadOnlyList<(SpatialPosition Position, DiffInfographicChangeKind Kind, string Label)> spatial)
    {
        var added = spatial.Count(x => x.Kind == DiffInfographicChangeKind.Added);
        var removed = spatial.Count(x => x.Kind == DiffInfographicChangeKind.Removed);
        var changed = spatial.Count(x => x.Kind == DiffInfographicChangeKind.Changed);
        Count(report.Embedded, ref added, ref removed, ref changed);
        Count(report.Chunks.Select(x => new ValueChange<string>(x.Left, x.Right)), ref added, ref removed, ref changed);
        changed += report.EmbeddedPropertyChanges.Sum(x => x.Properties.Changes.Count);
        changed += report.MetadataChanges.Count(x => !IsLegacyMetadata(x.Path));
        changed += new[] { report.MapUid, report.MapName, report.AuthorLogin, report.AuthorNickname, report.Password }.Count(x => x is not null);
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

    private static IEnumerable<(SpatialPosition Position, DiffInfographicChangeKind Kind, string Label)> AllChanges(DiffReport report)
    {
        foreach (var change in report.Blocks.Concat(report.BakedBlocks))
        {
            var value = change.Right ?? change.Left;
            if (value?.PhysicalPosition is { } position)
                yield return (position, Kind(change.Left, change.Right), value.Name);
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
        IReadOnlyList<(SpatialPosition Position, DiffInfographicChangeKind Kind, string Label)> changes,
        IReadOnlyList<DiffInfographicPoint> context, int omitted, int ignored)
    {
        var points = changes.Select(x => x.Position).Concat(context.Select(x => new SpatialPosition(x.X, 0, x.Z))).ToArray();
        var xs = points.Select(x => x.X).Order().ToArray();
        var zs = points.Select(x => x.Z).Order().ToArray();
        var minX = Quantile(xs, .01);
        var maxX = Quantile(xs, .99);
        var minZ = Quantile(zs, .01);
        var maxZ = Quantile(zs, .99);
        if (maxX <= minX) maxX = minX + 1;
        if (maxZ <= minZ) maxZ = minZ + 1;
        var clipped = points.Count(p => p.X < minX || p.X > maxX || p.Z < minZ || p.Z > maxZ);
        DiffInfographicPoint N(SpatialPosition p, DiffInfographicChangeKind kind, string label) => new(
            (float)Math.Clamp((p.X - minX) / (maxX - minX), 0, 1), (float)Math.Clamp((p.Z - minZ) / (maxZ - minZ), 0, 1), kind, DiffInfographicText.Clean(label));
        var normalizedChanges = changes.Select(x => N(x.Position, x.Kind, x.Label)).ToArray();
        var normalizedContext = context.Select(x => N(new(x.X, 0, x.Z), x.Kind, x.Label)).ToArray();
        var range = FormattableString.Invariant($"X {DiffInfographicText.Position(minX)}–{DiffInfographicText.Position(maxX)} m  ·  Z {DiffInfographicText.Position(minZ)}–{DiffInfographicText.Position(maxZ)} m");
        return new(normalizedContext, normalizedChanges, normalizedChanges.Length, omitted, ignored, clipped, range);

        static double Quantile(double[] values, double fraction)
        {
            if (values.Length == 0) return fraction == 0 ? 0 : 1;
            return values[(int)Math.Round((values.Length - 1) * fraction, MidpointRounding.AwayFromZero)];
        }
    }

    private static IEnumerable<string> Highlights(DiffReport report)
    {
        var ranked = report.Embedded.Select(c =>
        {
            var value = c.Right ?? c.Left!;
            var delta = SaturatingSubtract(c.Right?.Compressed ?? 0, c.Left?.Compressed ?? 0);
            var kind = c.Left is null ? "+" : c.Right is null ? "−" : "~";
            var detail = c.Left is not null && c.Right is not null
                ? $"ZIP {DiffInfographicText.Bytes(c.Left.Compressed)} to {DiffInfographicText.Bytes(c.Right.Compressed)} ({SignedBytes(delta)})"
                : $"ZIP {DiffInfographicText.Bytes(value.Compressed)}";
            return (Rank: Math.Abs(delta), Text: $"{kind} {value.Path}  ·  {detail}");
        }).OrderByDescending(x => x.Rank).ThenBy(x => x.Text, StringComparer.Ordinal).ToArray();
        foreach (var value in ranked.Take(MaxHighlights)) yield return value.Text;
        if (ranked.Length > MaxHighlights) yield return $"+ {ranked.Length - MaxHighlights:N0} more embedded changes omitted";

        var highlightedPaths = new HashSet<string>(report.Embedded.Select(x => (x.Right ?? x.Left)!.Path), StringComparer.Ordinal);
        foreach (var contribution in report.EmbeddedContributions)
        {
            var value = contribution.Right ?? contribution.Left;
            if (value is null || !highlightedPaths.Add(value.Path)) continue;
            var unavailable = value.UnavailableReason;
            var detail = unavailable is null
                ? "Outer-map marginal available (non-additive; not summed)"
                : $"Outer-map marginal unavailable: {unavailable}";
            yield return $"~ {value.Path}  ·  {detail}";
        }
        var contributions = report.EmbeddedContributions.Where(x => (x.Right ?? x.Left)?.UnavailableReason is not null).ToArray();
        if (contributions.Length > 0)
            yield return $"Marginal measurements are non-additive; {contributions.Length:N0} unavailable: {contributions[0].Right?.UnavailableReason ?? contributions[0].Left!.UnavailableReason}";
        else if (report.EmbeddedContributions.Count > 0)
            yield return "Outer-map marginal measurements are non-additive and are never summed.";
    }

    private static IEnumerable<string> Properties(DiffReport report)
    {
        var rows = report.EmbeddedPropertyChanges.SelectMany(entry => entry.Properties.Changes.Select(change =>
            $"{entry.Path} › {change.Path}: {Property(change.Left)} to {Property(change.Right)}")).ToArray();
        foreach (var row in rows.Take(MaxProperties)) yield return row;
        if (rows.Length > MaxProperties) yield return $"+ {rows.Length - MaxProperties:N0} more property changes omitted";
        var issues = report.EmbeddedPropertyChanges.Sum(x => x.Properties.LeftIssues.Count + x.Properties.RightIssues.Count);
        if (issues > 0) yield return $"WARNING · {issues:N0} opaque or partial-coverage diagnostic{(issues == 1 ? "" : "s")}; content hashes still prove the entries changed.";
    }

    private static IEnumerable<string> Metadata(DiffReport report)
    {
        var changes = new List<(string Label, string Left, string Right)>();
        Add("Map UID", report.MapUid); Add("Map name", report.MapName); Add("Author login", report.AuthorLogin);
        Add("Author name", report.AuthorNickname); Add("Plaintext password", report.Password);
        changes.AddRange(report.MetadataChanges.Where(x => !IsLegacyMetadata(x.Path)).Select(x => (x.Path, Meta(x.Left), Meta(x.Right))));
        var unique = changes.Distinct().ToArray();
        foreach (var row in unique.Take(MaxMetadata)) yield return $"{row.Label}: {row.Left} to {row.Right}";
        if (unique.Length > MaxMetadata) yield return $"+ {unique.Length - MaxMetadata:N0} more metadata changes omitted";
        void Add(string label, Change? change)
        {
            if (change is not null) changes.Add((label, change.Left ?? "absent", change.Right ?? "absent"));
        }
    }

    private static IEnumerable<string> Warnings(DiffReport report, DiffInfographicSpatialScene spatial)
    {
        foreach (var warning in report.Warnings.Take(3)) yield return "WARNING · " + warning;
        if (report.Warnings.Count > 3) yield return $"+ {report.Warnings.Count - 3:N0} more warnings omitted";
        if (spatial.OmittedChanges > 0) yield return $"WARNING · {spatial.OmittedChanges:N0} spatial changes omitted from the plot; exact counts above include them.";
        if (spatial.IgnoredNonFinitePositions > 0) yield return $"WARNING · {spatial.IgnoredNonFinitePositions:N0} non-finite spatial position{(spatial.IgnoredNonFinitePositions == 1 ? "" : "s")} could not be plotted.";
        if (spatial.ClippedToPlotEdge > 0) yield return $"NOTE · {spatial.ClippedToPlotEdge:N0} spatial context point{(spatial.ClippedToPlotEdge == 1 ? "" : "s")} outside the 1st–99th percentile range pinned to the plot edge.";
    }

    private static string Property(EmbeddedPropertyValue? value) => value switch
    {
        null => "absent",
        { Boolean: { } boolean } => boolean ? "true" : "false",
        { Integer: { } integer } => integer.ToString(CultureInfo.InvariantCulture),
        { Number: { } number } when double.IsFinite(number) => number.ToString("0.###", CultureInfo.InvariantCulture),
        _ => value.Text,
    };

    private static string Meta(MapMetadataValue? value) => value switch
    {
        null => "absent",
        { Text: { } text } => text,
        { Integer: { } integer } => integer.ToString(CultureInfo.InvariantCulture),
        { Boolean: { } boolean } => boolean ? "true" : "false",
        _ => "absent",
    };

    private static bool IsLegacyMetadata(string path) => path is "map.uid" or "map.name" or "author.login" or "author.nickname" or "security.passwordPresent";
    private static bool IsFinite((SpatialPosition Position, DiffInfographicChangeKind Kind, string Label) x) => IsFinite(x.Position);
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
