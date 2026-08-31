using GbxSizeTree.Model;

namespace GbxSizeTree.Abstractions;

/// <summary>
/// Ranked recommendations. Tool-applicable LOSSLESS actions always rank ahead of lossy and
/// editor-only advice, so <c>RecommendationsToGetUnderLimit</c> — how many top-ranked entries
/// are needed to get the map under the online limit (-1 when unreachable) — never counts a
/// lossy/editor step the lossless set alone could cover.
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
    /// <summary><paramref name="map"/>, when available, lets Detect produce measured estimates.</summary>
    RecommendationReport Recommend(
        MapAnalysis analysis,
        IReadOnlyDictionary<string, string> settings,
        Measure.ResaveTrial? resaveTrial = null,
        GBX.NET.Engines.Game.CGameCtnChallenge? map = null);
}
