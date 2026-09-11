namespace GbxSizeTree.Cli.Rendering;

public sealed record DiffInfographicCounts(int Added, int Removed, int Changed);

public sealed record DiffInfographicCountGroup(string Title, DiffInfographicCounts Counts);

public sealed record DiffInfographicDetailCounts(int Metadata, int DeepProperties, int Chunks);

public sealed record DiffInfographicPoint(
    float X,
    float Z,
    DiffInfographicChangeKind Kind,
    string Label,
    bool EdgePinned = false);

public sealed record DiffInfographicViewport(double MinX, double MaxX, double MinZ, double MaxZ)
{
    private (string Minimum, string Maximum) XLabels => DiffInfographicText.PositionPair(MinX, MaxX);
    private (string Minimum, string Maximum) ZLabels => DiffInfographicText.PositionPair(MinZ, MaxZ);

    public string XMinimumLabel => $"X {XLabels.Minimum} m";
    public string XMaximumLabel => $"X {XLabels.Maximum} m";
    public string ZMinimumLabel => $"Z {ZLabels.Minimum} m";
    public string ZMaximumLabel => $"Z {ZLabels.Maximum} m";
}

public enum DiffInfographicChangeKind
{
    Added,
    Removed,
    Changed,
}

public sealed record DiffInfographicSpatialScene(
    IReadOnlyList<DiffInfographicPoint> Context,
    IReadOnlyList<DiffInfographicPoint> Changes,
    DiffInfographicViewport? Viewport,
    int PlottedChanges,
    int SampledOutChanges,
    int InvalidChangePositions,
    int UnpositionedChanges,
    int PlottedContext,
    int SampledOutContext,
    int InvalidContextPositions,
    int ContextWithoutViewport,
    int EdgePinnedChanges,
    int EdgePinnedContext,
    string RangeLabel,
    string CoverageLabel);

public sealed record DiffInfographicSectionLine(string Text, bool Mono = false)
{
    public static implicit operator DiffInfographicSectionLine(string text) => new(text);
}

public sealed record DiffInfographicTableRow(
    string Marker,
    string Path,
    string Value,
    DiffInfographicChangeKind Kind);

public enum DiffInfographicTableDensity
{
    Standard,
    Compact,
}

public sealed record DiffInfographicTable(
    IReadOnlyList<DiffInfographicTableRow> Rows,
    DiffInfographicTableDensity Density = DiffInfographicTableDensity.Standard);

public sealed record DiffInfographicSection(
    string Id,
    string Title,
    IReadOnlyList<DiffInfographicSectionLine> Lines,
    int Left,
    int Top,
    int Right,
    int Bottom,
    DiffInfographicTable? Table = null);

internal readonly record struct InfographicTableColumns(
    float MarkerX,
    float PathX,
    float PathWidth,
    float ValueLeft,
    float ValueRight);

/// Section geometry shared by the scene builder and the painter, so a panel is sized by what the
/// painter actually consumes: title block, optional table header plus rows, then wrapped lines.
internal static class DiffInfographicLayout
{
    public const int SectionHeader = 66;
    public const int SectionBottomPadding = 22;
    public const int LineHeight = 58;
    public const int TableHeaderHeight = 28;
    public const int TableRowHeight = 30;
    public const int CompactTableRowHeight = 24;
    public const int TableBottomGap = 12;

    public static int TableHeight(DiffInfographicTable? table) => table is null || table.Rows.Count == 0
        ? 0
        : TableHeaderHeight + (table.Rows.Count * RowHeight(table.Density)) + TableBottomGap;

    public static int RowHeight(DiffInfographicTableDensity density) =>
        density == DiffInfographicTableDensity.Compact ? CompactTableRowHeight : TableRowHeight;

    public static int SectionHeight(int lineCount, DiffInfographicTable? table)
    {
        var tableHeight = TableHeight(table);
        var lines = Math.Max(lineCount, tableHeight == 0 ? 1 : 0) * LineHeight;
        return SectionHeader + tableHeight + lines + SectionBottomPadding;
    }
}

public sealed record DiffInfographicScene(
    int Width,
    int Height,
    string OldFileName,
    string NewFileName,
    long LeftBytes,
    long RightBytes,
    long DeltaBytes,
    DiffInfographicCountGroup Placements,
    DiffInfographicCountGroup Embedded,
    DiffInfographicDetailCounts DetailCounts,
    DiffInfographicSpatialScene Spatial,
    IReadOnlyList<DiffInfographicSection> Sections,
    IReadOnlyList<string> Warnings);
