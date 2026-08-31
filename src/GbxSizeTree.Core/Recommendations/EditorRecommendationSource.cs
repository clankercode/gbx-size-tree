using GbxSizeTree.Actions;
using GbxSizeTree.Model;

namespace GbxSizeTree.Recommendations;

/// <summary>
/// Produces editor-only suggestions from the lightmap and embedded-item format facts verified in
/// docs/FORMAT-NOTES.md.
/// </summary>
public sealed class EditorRecommendationSource
{
    private const long HeavyEmbeddedEntryBytes = 100L * 1024;

    public IEnumerable<Recommendation> Produce(MapAnalysis analysis)
    {
        var lightmap = analysis.Body?.Lightmap;
        if (lightmap?.FrameCount == 3)
        {
            yield return new Recommendation(
                ActionId: null,
                Title: "Re-bake shadows with static daylight (single frame)",
                Tier: ActionTier.EditorOnly,
                EstimatedSavingsBytes: Scale(lightmap.WebpBytesTotal, 2.0 / 3.0),
                Kind: EstimateKind.Heuristic,
                Consequence: "Uses a single static-daylight shadow frame instead of three dynamic-daylight frames.",
                HowTo: "In the map editor, switch to static daylight, re-bake shadows, and save the map.");
        }

        var facts = analysis.Facts;
        if (lightmap is not null && facts is not null)
        {
            var elementCount = Math.Max(1L, (long)facts.BlockCount + facts.AnchoredObjectCount);
            var lightmapBytesPerElement =
                ((double)lightmap.WebpBytesTotal + lightmap.ZlibCompressedBytes) / elementCount;

            if (lightmapBytesPerElement > 24)
            {
                yield return new Recommendation(
                    ActionId: null,
                    Title: "Re-bake shadows at a lower quality",
                    Tier: ActionTier.EditorOnly,
                    EstimatedSavingsBytes: Scale(lightmap.ChunkBytes, 0.5),
                    Kind: EstimateKind.Heuristic,
                    Consequence: "Reduces baked-shadow fidelity to shrink the embedded lightmap.",
                    HowTo: "In the map editor, re-bake shadows at a lower quality and save. "
                        + "Ultra2 uses stronger texture compression and can produce a smaller map.");
            }
        }

        var heavyEntries = analysis.Body?.EmbeddedZip?.Entries
            .Where(entry => entry.CompressedBytes > HeavyEmbeddedEntryBytes)
            .OrderByDescending(entry => entry.CompressedBytes)
            .Take(3)
            .ToArray() ?? [];

        if (heavyEntries.Length > 0)
        {
            var names = string.Join(", ", heavyEntries.Select(entry => entry.Path));
            yield return new Recommendation(
                ActionId: null,
                Title: "Replace or simplify heavy embedded items",
                Tier: ActionTier.EditorOnly,
                EstimatedSavingsBytes: Scale(heavyEntries.Sum(entry => entry.CompressedBytes), 0.5),
                Kind: EstimateKind.Heuristic,
                Consequence: $"The largest embedded entries are: {names}.",
                HowTo: $"In the map editor, replace or simplify these embedded items, then save: {names}.");
        }
    }

    private static long Scale(long bytes, double factor) =>
        (long)Math.Round(bytes * factor, MidpointRounding.AwayFromZero);
}
