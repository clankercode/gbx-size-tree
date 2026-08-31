using GBX.NET;
using GBX.NET.Engines.Game;
using GbxSizeTree.Abstractions;
using GbxSizeTree.Actions;
using GbxSizeTree.Actions.Passes;
using GbxSizeTree.Model;
using GbxSizeTree.Tests.Fixtures;

namespace GbxSizeTree.Tests.Actions;

public sealed class PruneChunksActionTests
{
    private static readonly IReadOnlyDictionary<string, string> EmptySettings =
        new Dictionary<string, string>();

    [Fact]
    public void Detect_NoDeadChunks_DoesNotApply()
    {
        var action = new PruneChunksAction();

        var applicability = action.Detect(new ActionDetectContext(
            FakeAnalysis.Build(), EmptySettings, NullStatusSink.Instance));

        Assert.False(applicability.Applies);
        Assert.Equal("no dead chunks present", applicability.Reason);
    }

    [Fact]
    public void Detect_DeadChunks_ComputesExactByteSum()
    {
        var action = new PruneChunksAction();
        var analysis = FakeAnalysis.Build();
        var body = analysis.Body!;
        var chunks = body.Chunks.Concat(
        [
            new BodyChunkInfo(0x0304305E, "Legacy macroblocks (stub)", SizeCategory.Metadata,
                "stub", 32, SizeConfidence.ExactOnDisk, BodyOffset: 9_450, Skippable: true, Order: 3),
            new BodyChunkInfo(0x03043061, "Write-only snapshot", SizeCategory.Metadata,
                "snapshot", 32, SizeConfidence.ExactOnDisk, BodyOffset: 9_482, Skippable: true, Order: 4),
            new BodyChunkInfo(0x03043064, "Media-clip-group stub", SizeCategory.Metadata,
                "stub", 28, SizeConfidence.ExactOnDisk, BodyOffset: 9_514, Skippable: true, Order: 5),
        ]).ToList();
        analysis = analysis with { Body = body with { Chunks = chunks } };

        var applicability = action.Detect(new ActionDetectContext(
            analysis, EmptySettings, NullStatusSink.Instance));

        Assert.True(applicability.Applies);
        Assert.Equal(92, applicability.EstimatedSavingsBytes);
        Assert.Equal(EstimateKind.Computed, applicability.Kind);
        Assert.Contains("0x0304305E", applicability.Detail);
    }

    [Fact]
    public void Apply_RemovesOnlyDeadChunks()
    {
        var action = new PruneChunksAction();
        var map = new CGameCtnChallenge();
        map.Chunks.Create<CGameCtnChallenge.Chunk0304305E>();
        map.Chunks.Create<CGameCtnChallenge.Chunk03043061>();
        map.Chunks.Create<CGameCtnChallenge.Chunk03043064>();
        var keeper = map.Chunks.Create<CGameCtnChallenge.Chunk03043062>();
        var gbx = new Gbx<CGameCtnChallenge>(map);

        var result = action.Apply(new ActionApplyContext(
            gbx, map, FakeAnalysis.Build(), EmptySettings, NullStatusSink.Instance));

        Assert.True(result.Changed);
        Assert.Equal(3, result.Notes.Count);
        Assert.DoesNotContain(map.Chunks, chunk => chunk.Id is 0x0304305E or 0x03043061 or 0x03043064);
        Assert.Contains(keeper, map.Chunks);
    }

    [Fact]
    public void Apply_NothingToRemove_ReportsNoChange()
    {
        var action = new PruneChunksAction();
        var map = new CGameCtnChallenge();
        var gbx = new Gbx<CGameCtnChallenge>(map);

        var result = action.Apply(new ActionApplyContext(
            gbx, map, FakeAnalysis.Build(), EmptySettings, NullStatusSink.Instance));

        Assert.False(result.Changed);
    }
}
