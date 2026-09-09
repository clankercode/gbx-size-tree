namespace GbxSizeTree.Cli.Rendering;

public sealed record DiffInfographicCounts(int Added, int Removed, int Changed);

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

public sealed record DiffInfographicSection(string Id, string Title, IReadOnlyList<string> Lines, int Left, int Top, int Right, int Bottom);

public sealed record DiffInfographicScene(
    int Width,
    int Height,
    string OldFileName,
    string NewFileName,
    long LeftBytes,
    long RightBytes,
    long DeltaBytes,
    DiffInfographicCounts Counts,
    string CountScopeLabel,
    DiffInfographicDetailCounts DetailCounts,
    DiffInfographicSpatialScene Spatial,
    IReadOnlyList<DiffInfographicSection> Sections,
    IReadOnlyList<string> Warnings);
