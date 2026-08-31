using GBX.NET;

namespace GbxSizeTree.Actions.Passes;

/// <summary>
/// Recompresses a parsed compressed body with the LZO1x_999 save path documented in
/// <c>docs/FORMAT-NOTES.md</c>.
/// </summary>
public sealed class ResaveAction : IMapAction
{
    public string Id => "resave";
    public string Title => "Resave";
    public ActionTier Tier => ActionTier.Lossless;
    public string Consequence => "";
    public string HowToManually => "";
    public bool DefaultOn => true;
    public int Order => 90;

    /// <summary>
    /// LZO1x_999 vs the game's fast level measured 5.4% on the reference map (docs/DECISIONS.md);
    /// 5% of the compressed body is a deliberately conservative heuristic so the recommendation
    /// ranking and under-limit verdict can count this guaranteed win before a save happens.
    /// </summary>
    public ActionApplicability Detect(ActionDetectContext ctx) => ctx.Analysis.Body is { } body
        ? new ActionApplicability(
            Applies: true,
            EstimatedSavingsBytes: (long)(body.CompressedBytes * 0.05),
            Kind: EstimateKind.Heuristic,
            Reason: "LZO1x_999 typically shrinks the body ~5% vs the game's fast compression; exact savings are measured at save")
        : ActionApplicability.No("map analysis has no compressed body");

    public ActionResult Apply(ActionApplyContext ctx)
    {
        ctx.Gbx.BodyCompression = GbxCompression.Compressed;
        return new ActionResult(
            Changed: true,
            Summary: "body will be recompressed with LZO1x_999 on save",
            Notes: []);
    }
}
