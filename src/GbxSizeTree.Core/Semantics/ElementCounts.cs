using System.Globalization;
using GBX.NET.Engines.Game;

namespace GbxSizeTree.Semantics;

/// <summary>
/// Counts the CGameCtnChallenge element collections used by the per-element chunks documented in
/// docs/FORMAT-NOTES.md.
/// </summary>
public static class ElementCounts
{
    /// <summary>Counts blocks, baked blocks, anchored items, and free blocks without forcing lazy data.</summary>
    public static (int blocks, int baked, int items, int free) Count(CGameCtnChallenge map)
    {
        if (map is null)
        {
            return (0, 0, 0, 0);
        }

        var blocks = map.Blocks?.Count ?? 0;
        var baked = map.BakedBlocks?.Count ?? 0;
        var items = map.AnchoredObjects?.Count ?? 0;
        var free = map.Blocks?.Count(static block => block.IsFree) ?? 0;

        return (blocks, baked, items, free);
    }

    /// <summary>Formats a chunk size as an invariant-culture byte count per represented element.</summary>
    public static string PerElementDetail(long chunkBytes, int elements, string unit)
    {
        var elementLabel = elements == 1 ? unit : $"{unit}s";
        if (elements <= 0)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:N0} {1} - n/a", elements, elementLabel);
        }

        var bytesPerElement = chunkBytes / elements;
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:N0} {1} - {2:N0} B/{3}",
            elements,
            elementLabel,
            bytesPerElement,
            unit);
    }
}
