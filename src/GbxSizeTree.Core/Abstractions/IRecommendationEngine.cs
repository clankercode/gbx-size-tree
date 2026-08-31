using GbxSizeTree.Model;

namespace GbxSizeTree.Abstractions;

/// <summary>
/// Ranked recommendations. <c>RecommendationsToGetUnderLimit</c> is how many top-ranked
/// entries are needed to get the map under the online limit; -1 when unreachable.
/// </summary>
public sealed record RecommendationReport(
    IReadOnlyList<Recommendation> Ranked,
    bool AlreadyUnderLimit,
    int RecommendationsToGetUnderLimit);

public interface IRecommendationEngine
{
    RecommendationReport Recommend(MapAnalysis analysis, IReadOnlyDictionary<string, string> settings);
}
