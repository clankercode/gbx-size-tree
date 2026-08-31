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

    public ActionApplicability Detect(ActionDetectContext ctx) => ctx.Analysis.Body is not null
        ? new ActionApplicability(
            Applies: true,
            EstimatedSavingsBytes: 0,
            Kind: EstimateKind.MeasuredOnSave,
            Reason: "savings are measured at save when LZO1x_999 replaces the game's fast compression level")
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
