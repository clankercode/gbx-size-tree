namespace GbxSizeTree.Model;

/// <summary>How a size figure was obtained. Rendered next to every number.</summary>
public enum SizeConfidence
{
    /// <summary>Read directly from on-disk bytes (header table, skippable chunk framing).</summary>
    ExactOnDisk,
    /// <summary>Measured by re-serializing through GBX.NET with shared writer state.</summary>
    WriterDelta,
    /// <summary>Derived from a model (compression attribution, heuristics).</summary>
    Estimated,
    Unknown,
}

public enum SizeCategory
{
    Header,
    Thumbnail,
    Metadata,
    Blocks,
    Items,
    BakedBlocks,
    Lightmap,
    EmbeddedItems,
    ScriptMetadata,
    MediaTracker,
    PerElementArrays,
    FreeBlocks,
    Other,
    Residual,
}

/// <summary>
/// One node of the diagnostic size tree. <paramref name="Id"/> is a stable dotted path
/// (e.g. "body.lightmap.webp") frozen in docs/CONTRACTS.md — JSON consumers and golden
/// tests key on it.
/// </summary>
public sealed record SizeNode(
    string Id,
    string Label,
    SizeCategory Category,
    long UncompressedBytes,
    long? OnDiskBytes,
    long? EstimatedOnDiskBytes,
    SizeConfidence Confidence,
    string? Detail,
    IReadOnlyList<SizeNode> Children)
{
    public static SizeNode Leaf(string id, string label, SizeCategory category, long bytes,
        SizeConfidence confidence, string? detail = null, long? onDisk = null, long? estOnDisk = null) =>
        new(id, label, category, bytes, onDisk, estOnDisk, confidence, detail, []);
}
