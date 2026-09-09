using System.Text.Json.Serialization;
using GbxSizeTree.Measure;

namespace GbxSizeTree.Cli.Modes;

[JsonSourceGenerationOptions(WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals)]
[JsonSerializable(typeof(DiffJsonReport))]
internal partial class DiffJsonContext : JsonSerializerContext;

internal sealed record DiffJsonReport(
    long LeftBytes, long RightBytes,
    IEnumerable<Change> Blocks, IEnumerable<Change> BakedBlocks, IEnumerable<Change> Items,
    IEnumerable<Change> Embedded, IReadOnlyList<ValueChange<EmbeddedSnapshot>> EmbeddedChanges,
    IReadOnlyList<Change> Chunks,
    IReadOnlyList<BlockSnapshot> LeftBakedSnapshots, IReadOnlyList<BlockSnapshot> RightBakedSnapshots,
    IReadOnlyList<BlockSnapshot> LeftBlockSnapshots, IReadOnlyList<BlockSnapshot> RightBlockSnapshots,
    IReadOnlyList<ItemSnapshot> LeftItemSnapshots, IReadOnlyList<ItemSnapshot> RightItemSnapshots,
    IReadOnlyList<EmbeddedSnapshot> LeftEmbeddedSnapshots, IReadOnlyList<EmbeddedSnapshot> RightEmbeddedSnapshots,
    Change? MapUid, Change? MapName, Change? AuthorLogin, Change? AuthorNickname, Change? Password,
    IReadOnlyList<MapMetadataChange> MetadataChanges,
    IReadOnlyList<ValueChange<EmbeddedFileContribution>> EmbeddedContributions,
    long? LeftContributionBaselineBytes, long? RightContributionBaselineBytes,
    IReadOnlyList<string> Warnings);
