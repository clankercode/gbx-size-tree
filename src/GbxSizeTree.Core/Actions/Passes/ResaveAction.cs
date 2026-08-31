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
    /// Prefers the background trial resave (a real LZO1x_999 save, started when the file was
    /// read) for a MEASURED number; falls back to a conservative 5%-of-body heuristic
    /// (measured 5.4% on the reference map, docs/DECISIONS.md) so the recommendation ranking
    /// and under-limit verdict always count this guaranteed win.
    /// </summary>
    public ActionApplicability Detect(ActionDetectContext ctx)
    {
        if (ctx.Analysis.Body is not { } body)
        {
            return ActionApplicability.No("map analysis has no compressed body");
        }

        if (ctx.ResaveTrial?.TryGetMeasuredSavings(TimeSpan.FromSeconds(30)) is long measured)
        {
            return new ActionApplicability(
                Applies: true,
                EstimatedSavingsBytes: Math.Max(0, measured),
                Kind: EstimateKind.Measured,
                Reason: measured > 0
                    ? "measured by a background trial LZO1x_999 resave"
                    : "trial resave measured no gain over the existing compression");
        }

        return new ActionApplicability(
            Applies: true,
            EstimatedSavingsBytes: (long)(body.CompressedBytes * 0.05),
            Kind: EstimateKind.Heuristic,
            Reason: "LZO1x_999 typically shrinks the body ~5% vs the game's fast compression; exact savings are measured at save");
    }

    public ActionResult Apply(ActionApplyContext ctx)
    {
        ctx.Gbx.BodyCompression = GbxCompression.Compressed;
        return new ActionResult(
            Changed: true,
            Summary: "body will be recompressed with LZO1x_999 on save",
            Notes: []);
    }
}
