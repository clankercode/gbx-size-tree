using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.LZO;
using GBX.NET.ZLib;
using GbxSizeTree.Abstractions;
using GbxSizeTree.Actions;
using GbxSizeTree.Actions.Passes;
using GbxSizeTree.Analysis;
using GbxSizeTree.Tests.Fixtures;

namespace GbxSizeTree.Tests.Actions;

public sealed class StripLightmapActionTests
{
    [Fact]
    public void Sample_DetectApplySaveAndReparse_RemovesLightmapPayload()
    {
        SampleMap.SkipUnlessAvailable();
        EnsureCodecs();

        var analysis = AnalyzeSample();
        var action = new StripLightmapAction();
        var applicability = action.Detect(new ActionDetectContext(
            analysis, EmptySettings(), NullStatusSink.Instance));

        Assert.Equal("strip-lightmap", action.Id);
        Assert.Equal(30, action.Order);
        Assert.Equal(ActionTier.BenignLossy, action.Tier);
        Assert.False(action.DefaultOn);
        Assert.True(applicability.Applies);
        Assert.Equal(EstimateKind.Computed, applicability.Kind);
        Assert.True(applicability.EstimatedSavingsBytes > 1_500_000);

        var control = Gbx.Parse<CGameCtnChallenge>(SampleMap.Path);
        using var controlSaved = new MemoryStream();
        control.Save(controlSaved);

        var stripped = Gbx.Parse<CGameCtnChallenge>(SampleMap.Path);
        var result = action.Apply(new ActionApplyContext(
            stripped, stripped.Node, analysis, EmptySettings(), NullStatusSink.Instance));
        Assert.True(result.Changed);

        using var strippedSaved = new MemoryStream();
        stripped.Save(strippedSaved);
        Assert.True(controlSaved.Length - strippedSaved.Length > 1_500_000,
            $"saved only {controlSaved.Length - strippedSaved.Length:N0} bytes versus control resave");

        strippedSaved.Position = 0;
        var reparsed = Gbx.Parse<CGameCtnChallenge>(strippedSaved);
        Assert.False(reparsed.Node.HasLightmaps);
    }

    private static GbxSizeTree.Model.MapAnalysis AnalyzeSample() =>
        MapAnalyzer.CreateDefault(NullStatusSink.Instance).Analyze(
            new MapSource.FromFile(SampleMap.Path), new AnalyzeOptions());

    private static void EnsureCodecs()
    {
        Gbx.LZO = new Lzo();
        Gbx.ZLib = new ZLib();
    }

    private static IReadOnlyDictionary<string, string> EmptySettings() =>
        new Dictionary<string, string>();
}
