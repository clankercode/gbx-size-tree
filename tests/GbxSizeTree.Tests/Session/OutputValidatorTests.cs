using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.LZO;
using GBX.NET.ZLib;
using GbxSizeTree.Abstractions;
using GbxSizeTree.Analysis;
using GbxSizeTree.Model;
using GbxSizeTree.Session;
using GbxSizeTree.Tests.Fixtures;

namespace GbxSizeTree.Tests.Session;

public sealed class OutputValidatorTests
{
    [Fact]
    public void Validate_RoundTrippedSample_PreservesIdentityAndCounts()
    {
        SampleMap.SkipUnlessAvailable();
        Gbx.LZO = new Lzo();
        Gbx.ZLib = new ZLib();
        var before = AnalyzeFacts();
        var gbx = Gbx.Parse<CGameCtnChallenge>(SampleMap.Path);
        using var stream = new MemoryStream();
        gbx.Save(stream);

        var report = OutputValidator.Validate(before, stream.ToArray(), [],
            new Dictionary<string, string>(), out var after);

        Assert.True(report.Ok);
        Assert.Empty(report.Issues);
        Assert.Equal(before.MapUid, after.MapUid);
        Assert.Equal(before.MapName, after.MapName);
        Assert.Equal(before.AuthorLogin, after.AuthorLogin);
        Assert.Equal(before.BlockCount, after.BlockCount);
        Assert.Equal(before.AnchoredObjectCount, after.AnchoredObjectCount);
        Assert.Equal(before.BakedBlockCount, after.BakedBlockCount);
        Assert.Equal(before.FreeBlockCount, after.FreeBlockCount);
        Assert.Equal(before.HasLightmaps, after.HasLightmaps);
        Assert.Equal(before.LightmapFrameCount, after.LightmapFrameCount);
        Assert.Equal(before.LightmapVersion, after.LightmapVersion);
        Assert.Equal(before.EmbeddedEntryCount, after.EmbeddedEntryCount);
    }

    [Fact]
    public void Validate_TruncatedBytes_ReturnsParseFailure()
    {
        SampleMap.SkipUnlessAvailable();
        Gbx.LZO = new Lzo();
        Gbx.ZLib = new ZLib();
        var before = AnalyzeFacts();
        var gbx = Gbx.Parse<CGameCtnChallenge>(SampleMap.Path);
        using var stream = new MemoryStream();
        gbx.Save(stream);
        var truncated = stream.ToArray()[..1000];

        var report = OutputValidator.Validate(before, truncated, [],
            new Dictionary<string, string>(), out _);

        Assert.False(report.Ok);
        var issue = Assert.Single(report.Issues);
        Assert.Equal("parse-produced-map", issue.Rule);
        Assert.Contains("Failed to parse produced map", issue.Message, StringComparison.Ordinal);
    }

    private static MapFacts AnalyzeFacts() =>
        Assert.IsType<MapFacts>(MapAnalyzer.CreateDefault(NullStatusSink.Instance).Analyze(
            new MapSource.FromFile(SampleMap.Path),
            new AnalyzeOptions()).Facts);
}
