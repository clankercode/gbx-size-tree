namespace GbxSizeTree.Cli.Rendering;

public sealed record DiffInfographicCounts(int Added, int Removed, int Changed);

public sealed record DiffInfographicCountGroup(string Title, DiffInfographicCounts Counts);

public sealed record DiffInfographicDetailCounts(int Metadata, int DeepProperties, int Chunks);

public sealed record DiffInfographicPoint(float X, float Z, DiffInfographicChangeKind Kind, string Label);

public enum DiffInfographicChangeKind
{
    Added,
    Removed,
    Changed,
}

public sealed record DiffInfographicSpatialScene(
    IReadOnlyList<DiffInfographicPoint> Context,
    IReadOnlyList<DiffInfographicPoint> Changes,
    int PlottedChanges,
    int SampledOutChanges,
    int InvalidChangePositions,
    int UnpositionedChanges,
    int PlottedContext,
    int SampledOutContext,
    int InvalidContextPositions,
    int EdgePinnedChanges,
    int EdgePinnedContext,
    string RangeLabel,
    string CoverageLabel);

public sealed record DiffInfographicSectionLine(string Text, bool Mono = false)
{
    public static implicit operator DiffInfographicSectionLine(string text) => new(text);
}

public sealed record DiffInfographicSection(string Id, string Title, IReadOnlyList<DiffInfographicSectionLine> Lines, int Left, int Top, int Right, int Bottom);

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
