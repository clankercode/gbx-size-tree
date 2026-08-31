namespace GbxSizeTree.Model;

public sealed record EmbeddedZipInfo(
    long ZipBytes,
    long EntriesUncompressedBytes,
    IReadOnlyList<EmbeddedEntryInfo> Entries,
    IReadOnlyList<string> ReferencedIdents);

public sealed record EmbeddedEntryInfo(
    string Path,
    long CompressedBytes,
    long UncompressedBytes,
    string Method,
    bool IsGbx,
    bool HasCompressedGbxBody,
    long? RecompressibleSavingsEstimate,
    bool IsReferenced);

public sealed record LightmapInfo(
    bool HasLightmaps,
    int Version,
    int FrameCount,
    IReadOnlyList<LightmapFrameInfo> Frames,
    long WebpBytesTotal,
    long ZlibCompressedBytes,
    long ZlibUncompressedBytes,
    long ChunkBytes);

/// <summary>One baked frame; up to three webp blobs (base/bump/night variants).</summary>
public sealed record LightmapFrameInfo(int Index, IReadOnlyList<long> BlobBytes);

public sealed record ThumbnailInfo(
    long ChunkBytes,
    long JpegBytes,
    int? Width,
    int? Height,
    int CommentLength,
    long StrippableMetadataBytes);

public sealed record ScriptMetadataInfo(long ChunkBytes, int? EntryCount, IReadOnlyList<string> TraitNames);

public sealed record MediaTrackerInfo(long ChunkBytes, int ClipCount, string? Summary);
