using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.LZO;
using GBX.NET.ZLib;
using GbxSizeTree.Abstractions;
using GbxSizeTree.Actions;
using GbxSizeTree.Actions.Passes;
using GbxSizeTree.Analysis;
using GbxSizeTree.Tests.Fixtures;
using SixLabors.ImageSharp;

namespace GbxSizeTree.Tests.Actions;

public sealed class ThumbnailActionTests
{
    [Fact]
    public void KeepAndUnknownModes_AreNotApplicable()
    {
        var action = new ThumbnailAction();
        var analysis = FakeAnalysis.Build();

        var keep = action.Detect(DetectContext(analysis, "keep"));
        var unknown = action.Detect(DetectContext(analysis, "turn-it-purple"));

        Assert.False(keep.Applies);
        Assert.Contains("keep", keep.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(unknown.Applies);
        Assert.Contains("unknown", unknown.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("turn-it-purple", unknown.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Sample_StripSaveAndReparse_RemovesThumbnail()
    {
        SampleMap.SkipUnlessAvailable();
        EnsureCodecs();
        var analysis = AnalyzeSample();
        var originalJpegBytes = Assert.IsType<GbxSizeTree.Model.ThumbnailInfo>(analysis.Header.Thumbnail).JpegBytes;
        var control = Gbx.Parse<CGameCtnChallenge>(SampleMap.Path);
        using var controlSaved = new MemoryStream();
        control.Save(controlSaved);
        var gbx = Gbx.Parse<CGameCtnChallenge>(SampleMap.Path);
        var action = new ThumbnailAction();

        var applicability = action.Detect(DetectContext(analysis, "strip", gbx.Node));
        Assert.True(applicability.Applies);
        Assert.Equal(originalJpegBytes, applicability.EstimatedSavingsBytes);
        Assert.Equal("map loses its thumbnail", action.Consequence);

        var result = action.Apply(ApplyContext(gbx, analysis, "strip"));
        Assert.True(result.Changed);
        using var saved = new MemoryStream();
        gbx.Save(saved);

        saved.Position = 0;
        var reparsed = Gbx.Parse<CGameCtnChallenge>(saved);
        Assert.Null(reparsed.Node.Thumbnail);
        Assert.InRange(controlSaved.Length - saved.Length,
            originalJpegBytes - 1_024, originalJpegBytes + 1_024);
    }

    [Fact]
    public void Sample_Recompress60_IsMeasuredSmallerAndRemainsValidJpeg()
    {
        SampleMap.SkipUnlessAvailable();
        EnsureCodecs();
        var analysis = AnalyzeSample();
        var gbx = Gbx.Parse<CGameCtnChallenge>(SampleMap.Path);
        var originalLength = Assert.IsType<byte[]>(gbx.Node.Thumbnail).Length;
        var action = new ThumbnailAction();

        var applicability = action.Detect(DetectContext(analysis, "recompress:60", gbx.Node));
        Assert.True(applicability.Applies);
        Assert.Equal(EstimateKind.Measured, applicability.Kind);
        Assert.True(applicability.EstimatedSavingsBytes > 0);
        Assert.Equal("thumbnail re-encoded (lossy)", action.Consequence);

        Assert.True(action.Apply(ApplyContext(gbx, analysis, "recompress:60")).Changed);
        using var saved = new MemoryStream();
        gbx.Save(saved);
        saved.Position = 0;
        var reparsed = Gbx.Parse<CGameCtnChallenge>(saved);
        var jpeg = Assert.IsType<byte[]>(reparsed.Node.Thumbnail);
        Assert.True(jpeg.Length < originalLength);
        Assert.Equal(0xFF, jpeg[0]);
        Assert.Equal(0xD8, jpeg[1]);
        Assert.Equal(0xFF, jpeg[^2]);
        Assert.Equal(0xD9, jpeg[^1]);
    }

    [Fact]
    public void Sample_Downscale256_ReparsedThumbnailDecodesAtRequestedMaximumDimension()
    {
        SampleMap.SkipUnlessAvailable();
        EnsureCodecs();
        var analysis = AnalyzeSample();
        var gbx = Gbx.Parse<CGameCtnChallenge>(SampleMap.Path);
        var action = new ThumbnailAction();

        var applicability = action.Detect(DetectContext(analysis, "downscale:256", gbx.Node));
        Assert.True(applicability.Applies);
        Assert.True(action.Apply(ApplyContext(gbx, analysis, "downscale:256")).Changed);
        using var saved = new MemoryStream();
        gbx.Save(saved);

        saved.Position = 0;
        var reparsed = Gbx.Parse<CGameCtnChallenge>(saved);
        using var image = Image.Load(Assert.IsType<byte[]>(reparsed.Node.Thumbnail));
        Assert.Equal(256, Math.Max(image.Width, image.Height));
    }

    private static ActionDetectContext DetectContext(
        GbxSizeTree.Model.MapAnalysis analysis,
        string mode,
        CGameCtnChallenge? map = null) =>
        new(analysis, Settings(mode), NullStatusSink.Instance, map);

    private static ActionApplyContext ApplyContext(
        Gbx<CGameCtnChallenge> gbx,
        GbxSizeTree.Model.MapAnalysis analysis,
        string mode) =>
        new(gbx, gbx.Node, analysis, Settings(mode), NullStatusSink.Instance);

    private static IReadOnlyDictionary<string, string> Settings(string mode) =>
        new Dictionary<string, string> { ["mode"] = mode };

    private static GbxSizeTree.Model.MapAnalysis AnalyzeSample() =>
        MapAnalyzer.CreateDefault(NullStatusSink.Instance).Analyze(
            new MapSource.FromFile(SampleMap.Path), new AnalyzeOptions());

    private static void EnsureCodecs()
    {
        Gbx.LZO = new Lzo();
        Gbx.ZLib = new ZLib();
    }
}
