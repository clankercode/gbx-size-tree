using GbxSizeTree.Model;

namespace GbxSizeTree.Semantics;

/// <summary>
/// Collects chunks the catalog does not recognize — the debug view for discovering chunk ids
/// that docs/FORMAT-NOTES.md and <see cref="ChunkCatalog"/> should learn about.
/// </summary>
public static class UnknownChunks
{
    public static IReadOnlyList<UnknownChunkInfo> Collect(MapAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        var unknown = new List<UnknownChunkInfo>();
        foreach (var chunk in analysis.Header.Chunks)
        {
            if (!ChunkCatalog.IsKnown(chunk.ChunkId))
            {
                unknown.Add(new UnknownChunkInfo(
                    "header", $"0x{chunk.ChunkId:X8}", chunk.Bytes, Skippable: false, chunk.FileOffset));
            }
        }

        foreach (var chunk in analysis.Body?.Chunks ?? [])
        {
            if (!ChunkCatalog.IsKnown(chunk.ChunkId))
            {
                unknown.Add(new UnknownChunkInfo(
                    "body", $"0x{chunk.ChunkId:X8}", chunk.Bytes, chunk.Skippable, chunk.BodyOffset));
            }
        }

        return unknown;
    }
}
