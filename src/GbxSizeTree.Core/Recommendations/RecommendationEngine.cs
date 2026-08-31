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
        IReadOnlyDictionary<string, string> settings)
    {
        var recommendations = new List<Recommendation>();
        var includeExperimental = settings.TryGetValue("experimental", out var experimental)
            && experimental == "true";

        foreach (var action in registry.Applyable(includeExperimental))
        {
            ActionApplicability applicability;
            try
            {
                applicability = action.Detect(new ActionDetectContext(analysis, settings, status));
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

            recommendations.Add(new Recommendation(
                ActionId: action.Id,
                Title: action.Title,
                Tier: action.Tier,
                EstimatedSavingsBytes: applicability.EstimatedSavingsBytes,
                Kind: applicability.Kind,
                Consequence: action.Consequence,
                HowTo: HowTo(action.Id)));
        }

        recommendations.AddRange(editorSource.Produce(analysis));

        var ranked = recommendations
            .OrderByDescending(recommendation => recommendation.EstimatedSavingsBytes)
            .ThenBy(recommendation => recommendation.Tier)
            .ToArray();
        var alreadyUnderLimit = analysis.FileBytes <= Limits.OnlineMapSizeBytes;
        var recommendationsToGetUnderLimit = alreadyUnderLimit
            ? 0
            : CountToReachLimit(analysis.FileBytes, ranked);

        return new RecommendationReport(ranked, alreadyUnderLimit, recommendationsToGetUnderLimit);
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
