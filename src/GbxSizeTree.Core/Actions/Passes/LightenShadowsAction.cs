using System.Globalization;
using GbxSizeTree.Model;

namespace GbxSizeTree.Actions.Passes;

/// <summary>
/// Applies the DD2-style floor to the first lightmap mapping color-data channel,
/// identified by gbx-py as shadow brightness. WebP image data is not modified.
/// </summary>
public sealed class LightenShadowsAction : IMapAction
{
    public const string FloorSetting = "shadow-brightness-floor";

    public string Id => "lighten-shadows";
    public string Title => "Lighten baked shadows";
    public ActionTier Tier => ActionTier.BenignLossy;
    public string Consequence => "raises dark baked-shadow brightness values and reduces shadow contrast";
    public string HowToManually => string.Empty;
    public bool DefaultOn => false;
    public int Order => 25;
    public bool RecommendByDefault => false;

    public ActionApplicability Detect(ActionDetectContext ctx)
    {
        if (!TryGetFloor(ctx.Settings, out var floor))
        {
            return ActionApplicability.No("no shadow-brightness floor was requested");
        }

        if (floor == 0)
        {
            return ActionApplicability.No("a zero shadow-brightness floor changes nothing");
        }

        if (ctx.Map is null)
        {
            return ActionApplicability.No("a parsed map is required to inspect shadow brightness");
        }

        var shadowBrightness = GetShadowBrightness(ctx.Map);
        if (shadowBrightness is null || shadowBrightness.Length == 0)
        {
            return ActionApplicability.No("map has no readable shadow-brightness channel");
        }

        var affected = shadowBrightness.Count(value => value < floor);
        return affected == 0
            ? ActionApplicability.No($"all shadow-brightness values are already at least {floor}")
            : new ActionApplicability(
                Applies: true,
                EstimatedSavingsBytes: 0,
                Kind: EstimateKind.Computed,
                Reason: $"{affected:N0} shadow-brightness values are below {floor}");
    }

    public ActionResult Apply(ActionApplyContext ctx)
    {
        if (!TryGetFloor(ctx.Settings, out var floor) || floor == 0)
        {
            return ActionResult.NoChange("no non-zero shadow-brightness floor was requested");
        }

        var shadowBrightness = GetShadowBrightness(ctx.Map);
        if (shadowBrightness is null || shadowBrightness.Length == 0)
        {
            return ActionResult.NoChange("map has no readable shadow-brightness channel");
        }

        var changed = 0;
        for (var index = 0; index < shadowBrightness.Length; index++)
        {
            if (shadowBrightness[index] >= floor)
            {
                continue;
            }

            shadowBrightness[index] = floor;
            changed++;
        }

        return changed == 0
            ? ActionResult.NoChange($"all shadow-brightness values were already at least {floor}")
            : new ActionResult(
                Changed: true,
                Summary: $"raised {changed:N0} shadow-brightness values to minimum {floor}",
                Notes: ["only lightmap mapping color-data channel 0 was changed; WebP images were preserved"]);
    }

    private static bool TryGetFloor(IReadOnlyDictionary<string, string> settings, out byte floor)
    {
        floor = 0;
        return settings.TryGetValue(FloorSetting, out var value) &&
            byte.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out floor);
    }

    private static byte[]? GetShadowBrightness(GBX.NET.Engines.Game.CGameCtnChallenge map) =>
        map.LightmapCache?.Mapping?.ZlibData6Decompressed?.ElementAtOrDefault(0);
}
