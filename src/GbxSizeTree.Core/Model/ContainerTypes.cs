namespace GbxSizeTree.Model;

/// <summary>
/// Exact byte-level layout of a Gbx file, read without GBX.NET node parsing.
/// Offsets are absolute file offsets; body sizes are present only for compressed bodies.
/// </summary>
public sealed record GbxFileLayout(
    short Version,
    char Format,
    bool RefTableCompressed,
    bool BodyCompressed,
    uint ClassId,
    long UserDataOffset,
    int UserDataSize,
    IReadOnlyList<HeaderChunkInfo> HeaderChunks,
    int NumNodes,
    int NumExternalNodes,
    long RefTableOffset,
    long RefTableSize,
    long BodyOffset,
    int? BodyUncompressedSize,
    int? BodyCompressedSize);

public enum RawChunkKind
{
    /// <summary>id + 'SKIP' + size framing; exact on-disk size.</summary>
    Skippable,
    /// <summary>Region between skippables that starts with a chunk id but has no size framing.</summary>
    NonSkippable,
    /// <summary>The 0xFACADE01 terminator.</summary>
    Terminator,
}

/// <summary>A region of the DECOMPRESSED body. Offsets relative to body start.</summary>
public sealed record RawChunkRegion(
    uint ChunkId,
    RawChunkKind Kind,
    long Offset,
    long Length,
    long PayloadOffset,
    long PayloadLength,
    int? EncapsulatedInnerSize);

/// <summary>Result of scanning the decompressed body for chunk regions.</summary>
public sealed record RawBodyScan(
    IReadOnlyList<RawChunkRegion> Regions,
    long ScannedBytes,
    long UnattributedBytes,
    bool Resynchronized,
    IReadOnlyList<AnalysisWarning> Warnings);

/// <summary>Serialized size of one chunk measured via cumulative writer deltas.</summary>
public sealed record ChunkDelta(uint ChunkId, int Index, long Bytes, bool Skippable);

/// <summary>Whole-body writer-delta measurement with reconciliation figures.</summary>
public sealed record WriterMeasurement(
    IReadOnlyList<ChunkDelta> Deltas,
    long TotalWrittenBytes,
    long ReferenceUncompressedBytes,
    long ResidualBytes);

/// <summary>
/// Static metadata about a known chunk id. <paramref name="Optional"/> means the map is
/// verified to load identically without the chunk (the game discards it on read —
/// Ghidra-proven, docs/FORMAT-NOTES.md); these are what `prune-chunks` removes.
/// </summary>
public sealed record ChunkMeta(
    uint Id,
    string Name,
    SizeCategory Category,
    string Description,
    bool Skippable,
    bool Encapsulated,
    bool Optional = false);

/// <summary>Result of the compressed-contribution attribution model for one chunk.</summary>
public sealed record AttributedChunk(uint ChunkId, long EstimatedOnDiskBytes);
