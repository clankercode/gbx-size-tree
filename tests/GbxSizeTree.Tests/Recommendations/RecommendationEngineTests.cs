using GbxSizeTree.Abstractions;
using GbxSizeTree.Actions;
using GbxSizeTree.Actions.Passes;
using GbxSizeTree.Model;
using GbxSizeTree.Recommendations;
using GbxSizeTree.Tests.Fixtures;

namespace GbxSizeTree.Tests.Recommendations;

public sealed class RecommendationEngineTests
{
    [Fact]
    public void Recommend_ApplyableActionRanksAheadOfSmallerEditorRecommendation()
    {
        var action = new FakeMapAction("fake-action", ActionTier.Lossless, Savings(5_000));
        var report = CreateEngine(action).Recommend(FakeAnalysis.Build(), EmptySettings);

        var recommendation = Assert.Single(report.Ranked, item => item.ActionId == action.Id);
        Assert.Same(recommendation, report.Ranked[0]);
        Assert.Equal(5_000, recommendation.EstimatedSavingsBytes);
        Assert.NotEmpty(recommendation.HowTo);
    }

    [Fact]
    public void Recommend_ThreeLightmapFramesIncludesStaticDaylightRecommendation()
    {
        var report = CreateEngine().Recommend(FakeAnalysis.Build(), EmptySettings);

        var recommendation = Assert.Single(report.Ranked,
            item => item.Title == "Re-bake shadows with static daylight (single frame)");
        Assert.Null(recommendation.ActionId);
        Assert.Equal(1_200, recommendation.EstimatedSavingsBytes);
        Assert.Equal(ActionTier.EditorOnly, recommendation.Tier);
    }

    [Fact]
    public void Recommend_RanksBySavingsThenTier()
    {
        var smaller = new FakeMapAction("smaller", ActionTier.Lossless, Savings(4_000));
        var benign = new FakeMapAction("benign", ActionTier.BenignLossy, Savings(5_000));
        var lossless = new FakeMapAction("lossless", ActionTier.Lossless, Savings(5_000));

        var report = CreateEngine(smaller, benign, lossless)
            .Recommend(FakeAnalysis.Build(), EmptySettings);

        Assert.Equal(["lossless", "benign", "smaller"],
            report.Ranked.Take(3).Select(item => item.ActionId));
    }

    [Fact]
    public void Recommend_ComputesLimitVerdicts()
    {
        var action = new FakeMapAction("enough", ActionTier.Lossless, Savings(5_000));
        var overByOneThousand = FakeAnalysis.Build() with
        {
            FileBytes = Limits.OnlineMapSizeBytes + 1_000,
        };
        var farOver = FakeAnalysis.Build() with
        {
            FileBytes = Limits.OnlineMapSizeBytes + 100_000,
        };
        var under = FakeAnalysis.Build() with
        {
            FileBytes = Limits.OnlineMapSizeBytes,
        };

        var reachable = CreateEngine(action).Recommend(overByOneThousand, EmptySettings);
        var unreachable = CreateEngine().Recommend(farOver, EmptySettings);
        var alreadyUnder = CreateEngine().Recommend(under, EmptySettings);

        Assert.False(reachable.AlreadyUnderLimit);
        Assert.Equal(1, reachable.RecommendationsToGetUnderLimit);
        Assert.Equal(-1, unreachable.RecommendationsToGetUnderLimit);
        Assert.True(alreadyUnder.AlreadyUnderLimit);
        Assert.Equal(0, alreadyUnder.RecommendationsToGetUnderLimit);
    }

    [Fact]
    public void Recommend_NotRecommendedActionBecomesCautionAndSkipsVerdict()
    {
        var destructive = new FakeMapAction(
            "destructive", ActionTier.BenignLossy, Savings(1_000_000), recommendByDefault: false);

        // Put the map over the limit by more than every RANKED recommendation combined but
        // less than the caution's savings: only counting the caution could reach the limit.
        var rankedSum = CreateEngine(destructive)
            .Recommend(FakeAnalysis.Build(), EmptySettings)
            .Ranked.Sum(item => item.EstimatedSavingsBytes);
        var overLimit = FakeAnalysis.Build() with
        {
            FileBytes = Limits.OnlineMapSizeBytes + rankedSum + 500_000,
        };

        var report = CreateEngine(destructive).Recommend(overLimit, EmptySettings);

        Assert.DoesNotContain(report.Ranked, item => item.ActionId == destructive.Id);
        var caution = Assert.Single(report.Cautions ?? []);
        Assert.Equal(destructive.Id, caution.ActionId);
        Assert.Equal(1_000_000, caution.EstimatedSavingsBytes);
        Assert.Equal(-1, report.RecommendationsToGetUnderLimit);
    }

    [Fact]
    public void Recommend_StripLightmapIsNeverARankedRecommendation()
    {
        var report = CreateEngine(new StripLightmapAction())
            .Recommend(FakeAnalysis.Build(), EmptySettings);

        Assert.DoesNotContain(report.Ranked, item => item.ActionId == "strip-lightmap");
        Assert.Contains(report.Cautions ?? [], item => item.ActionId == "strip-lightmap");
    }

    [Fact]
    public void Recommend_DetectExceptionWarnsAndSkipsAction()
    {
        var status = new CapturingStatusSink();
        var throwing = new FakeMapAction("throwing", ActionTier.Lossless,
            _ => throw new InvalidOperationException("synthetic failure"));
        var engine = new RecommendationEngine(new ActionRegistry([throwing]), status);

        var report = engine.Recommend(FakeAnalysis.Build(), EmptySettings);

        Assert.DoesNotContain(report.Ranked, item => item.ActionId == throwing.Id);
        var warning = Assert.Single(status.Warnings);
        Assert.Contains(throwing.Id, warning);
        Assert.Contains("synthetic failure", warning);
    }

    [Fact]
    public void EditorRecommendations_AreNullSafe()
    {
        var analysis = FakeAnalysis.Build() with { Body = null, Facts = null };

        var report = CreateEngine().Recommend(analysis, EmptySettings);

        Assert.Empty(report.Ranked);
    }

    private static readonly IReadOnlyDictionary<string, string> EmptySettings =
        new Dictionary<string, string>();

    private static RecommendationEngine CreateEngine(params IMapAction[] actions) =>
        new(new ActionRegistry(actions), NullStatusSink.Instance);

    private static Func<ActionDetectContext, ActionApplicability> Savings(long bytes) =>
        _ => new ActionApplicability(true, bytes, EstimateKind.Computed, "synthetic");

    private sealed class FakeMapAction(
        string id,
        ActionTier tier,
        Func<ActionDetectContext, ActionApplicability> detect,
        bool recommendByDefault = true) : IMapAction
    {
        public string Id { get; } = id;
        public string Title => $"Fake {Id}";
        public ActionTier Tier { get; } = tier;
        public string Consequence => "Synthetic consequence";
        public string HowToManually => string.Empty;
        public bool DefaultOn => false;
        public int Order => 10;
        public bool RecommendByDefault { get; } = recommendByDefault;

        public ActionApplicability Detect(ActionDetectContext ctx) => detect(ctx);

        public ActionResult Apply(ActionApplyContext ctx) => ActionResult.NoChange("not used");
    }

    private sealed class CapturingStatusSink : IStatusSink
    {
        public List<string> Warnings { get; } = [];

        public void Info(string message) { }

        public void Warn(string message) => Warnings.Add(message);

        public IDisposable Activity(string label) => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            public void Dispose() { }
        }
    }
}
