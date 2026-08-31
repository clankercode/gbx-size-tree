using GbxSizeTree.Abstractions;
using GbxSizeTree.Actions;
using GbxSizeTree.Model;

namespace GbxSizeTree.Recommendations;

/// <summary>
/// Ranks applyable actions and editor-only format recommendations against the online map-size limit
/// documented in docs/FORMAT-NOTES.md.
/// </summary>
public sealed class RecommendationEngine : IRecommendationEngine
{
    private readonly ActionRegistry registry;
    private readonly IStatusSink status;
    private readonly EditorRecommendationSource editorSource = new();

    public RecommendationEngine(ActionRegistry registry, IStatusSink status)
    {
        this.registry = registry;
        this.status = status;
    }

    public RecommendationReport Recommend(
        MapAnalysis analysis,
        IReadOnlyDictionary<string, string> settings,
        Measure.ResaveTrial? resaveTrial = null,
        GBX.NET.Engines.Game.CGameCtnChallenge? map = null)
    {
        if (analysis.Body is null)
        {
            // Header-only analysis cannot ground any recommendation; an empty report with a
            // "cannot" verdict would be misleading, so callers should not render this at all.
            return new RecommendationReport(
                [],
                analysis.FileBytes <= Limits.OnlineMapSizeBytes,
                analysis.FileBytes <= Limits.OnlineMapSizeBytes ? 0 : -1);
        }

        var recommendations = new List<Recommendation>();
        var cautions = new List<Recommendation>();
        var includeExperimental = settings.TryGetValue("experimental", out var experimental)
            && experimental == "true";

        foreach (var action in registry.Applyable(includeExperimental))
        {
            ActionApplicability applicability;
            try
            {
                applicability = action.Detect(
                    new ActionDetectContext(analysis, settings, status, map, resaveTrial));
            }
            catch (Exception ex)
            {
                status.Warn($"Recommendation detection for action '{action.Id}' failed: {ex.Message}");
                continue;
            }

            if (!applicability.Applies)
            {
                continue;
            }

            var recommendation = new Recommendation(
                ActionId: action.Id,
                Title: action.Title,
                Tier: action.Tier,
                EstimatedSavingsBytes: applicability.EstimatedSavingsBytes,
                Kind: applicability.Kind,
                Consequence: action.Consequence,
                HowTo: HowTo(action.Id));
            (action.RecommendByDefault ? recommendations : cautions).Add(recommendation);
        }

        AddThumbnailPotential(analysis, settings, map, recommendations);
        recommendations.AddRange(editorSource.Produce(analysis));

        // Tool-applicable lossless actions rank first regardless of size: when they alone
        // cover the overage, the verdict must never send the user to re-bake shadows or
        // shrink the thumbnail. Lossy/editor advice only enters the verdict as spillover.
        var ranked = recommendations
            .OrderByDescending(recommendation =>
                recommendation.ActionId is not null && recommendation.Tier == ActionTier.Lossless)
            .ThenByDescending(recommendation => recommendation.EstimatedSavingsBytes)
            .ThenBy(recommendation => recommendation.Tier)
            .ToArray();
        var alreadyUnderLimit = analysis.FileBytes <= Limits.OnlineMapSizeBytes;
        var recommendationsToGetUnderLimit = alreadyUnderLimit
            ? 0
            : CountToReachLimit(analysis.FileBytes, ranked);

        return new RecommendationReport(
            ranked,
            alreadyUnderLimit,
            recommendationsToGetUnderLimit,
            cautions.Count > 0 ? cautions : null);
    }

    /// <summary>
    /// With the default `--thumbnail keep` the thumbnail action detects as not-applicable and
    /// would never appear in the report, hiding real potential; probe with `strip` (the upper
    /// bound, exact) so the user learns the option exists.
    /// </summary>
    private void AddThumbnailPotential(
        MapAnalysis analysis,
        IReadOnlyDictionary<string, string> settings,
        GBX.NET.Engines.Game.CGameCtnChallenge? map,
        List<Recommendation> recommendations)
    {
        var mode = settings.TryGetValue("mode", out var value) ? value : "keep";
        if (mode != "keep" || registry.Find("thumbnail") is not { } thumbnail)
        {
            return;
        }

        var probeSettings = settings.ToDictionary(pair => pair.Key, pair => pair.Value);
        probeSettings["mode"] = "strip";

        ActionApplicability probe;
        try
        {
            probe = thumbnail.Detect(new ActionDetectContext(analysis, probeSettings, status, map));
        }
        catch (Exception ex)
        {
            status.Warn($"Thumbnail potential probe failed: {ex.Message}");
            return;
        }

        if (!probe.Applies)
        {
            return;
        }

        recommendations.Add(new Recommendation(
            ActionId: thumbnail.Id,
            Title: "Shrink or remove the thumbnail",
            Tier: thumbnail.Tier,
            EstimatedSavingsBytes: probe.EstimatedSavingsBytes,
            Kind: probe.Kind,
            Consequence: "strip removes it entirely (shown estimate); lossless/recompress/downscale keep one.",
            HowTo: "--thumbnail strip|lossless|recompress:Q|downscale:N"));
    }

    private static string HowTo(string actionId) => actionId switch
    {
        "resave" => "--optimize",
        "strip-lightmap" => "--strip-lightmap",
        "thumbnail" => "--thumbnail <mode>",
        _ => $"--action {actionId}",
    };

    private static int CountToReachLimit(long fileBytes, IReadOnlyList<Recommendation> ranked)
    {
        long accumulatedSavings = 0;
        for (var index = 0; index < ranked.Count; index++)
        {
            accumulatedSavings += ranked[index].EstimatedSavingsBytes;
            if (fileBytes - accumulatedSavings <= Limits.OnlineMapSizeBytes)
            {
                return index + 1;
            }
        }

        return -1;
    }
}
