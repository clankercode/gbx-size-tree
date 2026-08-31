using GbxSizeTree.Model;

namespace GbxSizeTree.Abstractions;

/// <summary>
/// Ranked recommendations. <c>RecommendationsToGetUnderLimit</c> is how many top-ranked
/// entries are needed to get the map under the online limit; -1 when unreachable.
/// <c>Cautions</c> are applicable-but-not-recommended actions (e.g. strip-lightmap):
/// shown as warnings, excluded from ranking and the verdict.
/// </summary>
public sealed record RecommendationReport(
    IReadOnlyList<Recommendation> Ranked,
    bool AlreadyUnderLimit,
    int RecommendationsToGetUnderLimit,
    IReadOnlyList<Recommendation>? Cautions = null);

public interface IRecommendationEngine
{
    RecommendationReport Recommend(
        MapAnalysis analysis,
        IReadOnlyDictionary<string, string> settings,
        Measure.ResaveTrial? resaveTrial = null);
}
