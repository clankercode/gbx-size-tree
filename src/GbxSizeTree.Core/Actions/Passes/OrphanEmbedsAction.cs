using GbxSizeTree.Model;

namespace GbxSizeTree.Actions.Passes;

/// <summary>
/// Removes unreferenced files from the embedded-items ZIP described in
/// <c>docs/FORMAT-NOTES.md</c>.
/// </summary>
public sealed class OrphanEmbedsAction : IMapAction
{
    private int removedCount;

    public string Id => "orphan-embeds";
    public string Title => "Remove orphaned embedded items";
    public ActionTier Tier => ActionTier.Lossless;
    public string Consequence => "removes embedded item files that are not placed on the map";
    public string HowToManually => "";
    public bool DefaultOn => true;
    public int Order => 10;

    public ActionApplicability Detect(ActionDetectContext ctx)
    {
        var orphans = Orphans(ctx.Analysis);
        if (orphans.Count == 0)
        {
            return ActionApplicability.No("no orphaned embedded items");
        }

        return new ActionApplicability(
            Applies: true,
            EstimatedSavingsBytes: orphans.Sum(entry => entry.CompressedBytes),
            Kind: EstimateKind.Computed,
            Reason: $"{orphans.Count} unreferenced embedded item file(s)");
    }

    public ActionResult Apply(ActionApplyContext ctx)
    {
        var orphanPaths = Orphans(ctx.Analysis)
            .Select(entry => entry.Path)
            .ToHashSet(StringComparer.Ordinal);
        if (orphanPaths.Count == 0)
        {
            removedCount = 0;
            return ActionResult.NoChange("no orphaned embedded items");
        }

        var removedPaths = new List<string>(orphanPaths.Count);
        ctx.Map.UpdateEmbeddedZipData(zip =>
        {
            foreach (var entry in zip.Entries.Where(entry => orphanPaths.Contains(entry.FullName)).ToArray())
            {
                removedPaths.Add(entry.FullName);
                entry.Delete();
            }
        });

        removedCount = removedPaths.Count;
        return new ActionResult(
            Changed: removedCount > 0,
            Summary: $"removed {removedCount} orphaned embedded item file(s)",
            Notes: removedPaths.Take(20).ToArray());
    }

    public IEnumerable<ValidationIssue> ValidateResult(MapFacts before, MapFacts after)
    {
        var expected = before.EmbeddedEntryCount - removedCount;
        if (after.EmbeddedEntryCount != expected)
        {
            yield return new ValidationIssue(
                "orphan-embeds.entry-count",
                $"expected {expected} embedded entries after removing {removedCount}, but found {after.EmbeddedEntryCount}");
        }
    }

    private static IReadOnlyList<EmbeddedEntryInfo> Orphans(MapAnalysis analysis) =>
        analysis.Body?.EmbeddedZip?.Entries.Where(entry => !entry.IsReferenced).ToArray() ?? [];
}
