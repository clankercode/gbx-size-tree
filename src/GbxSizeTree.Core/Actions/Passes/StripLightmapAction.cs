using GBX.NET.Engines.Hms;
using GbxSizeTree.Model;

namespace GbxSizeTree.Actions.Passes;

/// <summary>
/// Removes the baked-shadow payload described by the lightmap chunk in docs/FORMAT-NOTES.md §Lightmap.
/// </summary>
public sealed class StripLightmapAction : IMapAction
{
    public string Id => "strip-lightmap";
    public string Title => "Strip baked lightmap";
    public ActionTier Tier => ActionTier.BenignLossy;
    public string Consequence =>
        "baked shadows removed; the map renders unshadowed until shadows are recalculated in the editor";
    public string HowToManually => string.Empty;
    public bool DefaultOn => false;
    public int Order => 30;

    public ActionApplicability Detect(ActionDetectContext ctx)
    {
        var lightmap = ctx.Analysis.Body?.Lightmap;
        if (lightmap?.HasLightmaps != true)
        {
            return ActionApplicability.No("map has no baked lightmaps");
        }

        return new ActionApplicability(
            Applies: true,
            EstimatedSavingsBytes: Math.Max(0, lightmap.ChunkBytes - 16),
            Kind: EstimateKind.Computed,
            Reason: "baked lightmap payload can be removed");
    }

    public ActionResult Apply(ActionApplyContext ctx)
    {
        if (!ctx.Map.HasLightmaps)
        {
            return ActionResult.NoChange("map has no baked lightmaps");
        }

        // GBX.NET 2.4.4's proven empty-lightmap write pattern; see docs/FORMAT-NOTES.md §Lightmap.
        ctx.Map.HasLightmaps = false;
        ctx.Map.LightmapFrames = [new CHmsLightMapCache.Frame { Version = 6 }];
        ctx.Map.LightmapCacheData = null;

        return new ActionResult(true, "removed baked lightmap", []);
    }

    public IEnumerable<ValidationIssue> ValidateResult(MapFacts before, MapFacts after)
    {
        if (after.HasLightmaps)
        {
            yield return new ValidationIssue(Id, "result still contains baked lightmaps");
        }
    }
}
