namespace GbxSizeTree.Model;

/// <summary>Complete analysis of one map file (or in-memory materialization).</summary>
public sealed record MapAnalysis(
    string SourceLabel,
    long FileBytes,
    HeaderAnalysis Header,
    BodyAnalysis? Body,
    MapFacts? Facts,
    IReadOnlyList<AnalysisWarning> Warnings,
    SizeNode Tree);

public sealed record HeaderAnalysis(
    long TotalBytes,
    IReadOnlyList<HeaderChunkInfo> Chunks,
    ThumbnailInfo? Thumbnail,
    int? XmlLength);

public sealed record BodyAnalysis(
    long UncompressedBytes,
    long CompressedBytes,
    double Ratio,
    IReadOnlyList<BodyChunkInfo> Chunks,
    long UnattributedBytes,
    EmbeddedZipInfo? EmbeddedZip,
    LightmapInfo? Lightmap,
    ScriptMetadataInfo? ScriptMetadata,
    MediaTrackerInfo? MediaTracker);

/// <summary>
/// Identity + counts used for validation invariants and per-element rates.
/// <paramref name="AuthorNickname"/> is the human-readable display name (TM2020's
/// AuthorLogin is an opaque account id); both keep their raw $-format codes here.
/// </summary>
public sealed record MapFacts(
    string MapUid,
    string MapName,
    string AuthorLogin,
    string AuthorNickname,
    int BlockCount,
    int AnchoredObjectCount,
    int BakedBlockCount,
    int FreeBlockCount,
    bool HasLightmaps,
    int LightmapFrameCount,
    int LightmapVersion,
    int EmbeddedEntryCount);

public sealed record HeaderChunkInfo(uint ChunkId, string Name, long Bytes, bool Heavy, long FileOffset);

/// <summary>
/// One body chunk with its measured size. <paramref name="PrecompressedPayloadBytes"/> counts
/// already-compressed payload inside the chunk (webp/jpeg/zip/zlib) for the attribution model.
/// </summary>
public sealed record BodyChunkInfo(
    uint ChunkId,
    string Name,
    SizeCategory Category,
    string Description,
    long Bytes,
    SizeConfidence Confidence,
    long? BodyOffset,
    bool Skippable,
    int Order,
    long PrecompressedPayloadBytes = 0);

public sealed record AnalysisWarning(string Code, string Message);

/// <summary>One chunk the catalog does not recognize (the --unknown-chunks debug view).</summary>
public sealed record UnknownChunkInfo(
    string Section,
    string ChunkId,
    long Bytes,
    bool Skippable,
    long? Offset);

/// <summary>The --unknown-chunks --json envelope payload.</summary>
public sealed record UnknownChunksReport(
    string SourceLabel,
    IReadOnlyList<UnknownChunkInfo> UnknownChunks);
