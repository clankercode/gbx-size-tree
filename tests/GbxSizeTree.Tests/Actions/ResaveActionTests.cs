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

public sealed class ResaveActionTests
{
    [Fact]
    public void Sample_DetectApplyAndSave_RecompressesToSmallerValidMap()
    {
        SampleMap.SkipUnlessAvailable();
        Gbx.LZO = new Lzo();
        Gbx.ZLib = new ZLib();

        var analysis = MapAnalyzer.CreateDefault(NullStatusSink.Instance).Analyze(
            new MapSource.FromFile(SampleMap.Path),
            new AnalyzeOptions());
        var gbx = Gbx.Parse<CGameCtnChallenge>(SampleMap.Path);
        var action = new ResaveAction();

        Assert.Equal("resave", action.Id);
        Assert.Equal(90, action.Order);
        Assert.Equal(ActionTier.Lossless, action.Tier);
        Assert.True(action.DefaultOn);
        Assert.Equal("", action.Consequence);

        var applicability = action.Detect(new ActionDetectContext(analysis, EmptySettings(), NullStatusSink.Instance));
        Assert.True(applicability.Applies);
        Assert.Equal(0, applicability.EstimatedSavingsBytes);
        Assert.Equal(EstimateKind.MeasuredOnSave, applicability.Kind);
        Assert.Equal(
            "savings are measured at save when LZO1x_999 replaces the game's fast compression level",
            applicability.Reason);

        var result = action.Apply(new ActionApplyContext(
            gbx, gbx.Node, analysis, EmptySettings(), NullStatusSink.Instance));
        Assert.True(result.Changed);
        Assert.Equal(GbxCompression.Compressed, gbx.BodyCompression);
        Assert.Equal("body will be recompressed with LZO1x_999 on save", result.Summary);

        using var saved = new MemoryStream();
        gbx.Save(saved);
        var savings = new FileInfo(SampleMap.Path).Length - saved.Length;
        Assert.True(savings > 0, $"LZO1x_999 savings were {savings:N0} bytes");

        saved.Position = 0;
        var reparsed = Gbx.Parse<CGameCtnChallenge>(saved);
        Assert.NotNull(reparsed.Node);
        Console.WriteLine($"LZO1x_999 resave saved {savings:N0} bytes");
    }

    [Fact]
    public void Detect_AnalysisWithoutBody_DoesNotApply()
    {
        var analysis = FakeAnalysis.Build() with { Body = null };

        var applicability = new ResaveAction().Detect(
            new ActionDetectContext(analysis, EmptySettings(), NullStatusSink.Instance));

        Assert.False(applicability.Applies);
        Assert.Equal("map analysis has no compressed body", applicability.Reason);
    }

    private static IReadOnlyDictionary<string, string> EmptySettings() =>
        new Dictionary<string, string>();
}
