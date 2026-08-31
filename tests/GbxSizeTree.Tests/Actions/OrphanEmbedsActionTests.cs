using System.IO.Compression;
using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.LZO;
using GBX.NET.ZLib;
using GbxSizeTree.Abstractions;
using GbxSizeTree.Actions;
using GbxSizeTree.Actions.Passes;
using GbxSizeTree.Analysis;
using GbxSizeTree.Model;
using GbxSizeTree.Tests.Fixtures;

namespace GbxSizeTree.Tests.Actions;

public sealed class OrphanEmbedsActionTests
{
    private const string FakeOrphanPath = "Items/FakeOrphan_ZZZ.Item.Gbx";

    [Fact]
    public void Sample_DetectIsConsistentAndApplyPreservesReferencedEntries()
    {
        SampleMap.SkipUnlessAvailable();
        EnsureCodecs();

        var analysis = AnalyzeFile();
        var gbx = Gbx.Parse<CGameCtnChallenge>(SampleMap.Path);
        var action = new OrphanEmbedsAction();
        var entries = Assert.IsType<EmbeddedZipInfo>(analysis.Body?.EmbeddedZip).Entries;
        var orphanPaths = entries.Where(entry => !entry.IsReferenced).Select(entry => entry.Path).ToHashSet(StringComparer.Ordinal);
        var referencedPaths = entries.Where(entry => entry.IsReferenced).Select(entry => entry.Path).ToHashSet(StringComparer.Ordinal);

        var applicability = action.Detect(DetectContext(analysis));
        Assert.Equal(orphanPaths.Count > 0, applicability.Applies);
        Assert.Equal(orphanPaths.Sum(path => entries.Single(entry => entry.Path == path).CompressedBytes),
            applicability.EstimatedSavingsBytes);
        Assert.Equal(orphanPaths.Count > 0 ? EstimateKind.Computed : EstimateKind.Unknown, applicability.Kind);

        if (!applicability.Applies)
        {
            return;
        }

        var result = action.Apply(ApplyContext(gbx, analysis));
        Assert.True(result.Changed);
        Assert.InRange(result.Notes.Count, 0, 20);

        using var zip = gbx.Node.OpenReadEmbeddedZipData();
        var remaining = zip.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.Ordinal);
        Assert.Empty(orphanPaths.Intersect(remaining, StringComparer.Ordinal));
        Assert.Empty(referencedPaths.Except(remaining, StringComparer.Ordinal));
    }

    [Fact]
    public void SyntheticAddedOrphan_DetectsAndRemovesItWithoutRemovingReferencedEntries()
    {
        SampleMap.SkipUnlessAvailable();
        EnsureCodecs();

        var sourceGbx = Gbx.Parse<CGameCtnChallenge>(SampleMap.Path);
        sourceGbx.Node.UpdateEmbeddedZipData(zip =>
        {
            var entry = zip.CreateEntry(FakeOrphanPath, CompressionLevel.NoCompression);
            using var writer = new BinaryWriter(entry.Open());
            writer.Write(new byte[] { 1, 2, 3, 4 });
        });

        using var withFakeStream = new MemoryStream();
        sourceGbx.Save(withFakeStream);
        var withFakeBytes = withFakeStream.ToArray();
        var analysis = MapAnalyzer.CreateDefault(NullStatusSink.Instance).Analyze(
            new MapSource.FromBytes(withFakeBytes, "synthetic orphan map"),
            new AnalyzeOptions());
        using var parsedStream = new MemoryStream(withFakeBytes, writable: false);
        var gbx = Gbx.Parse<CGameCtnChallenge>(parsedStream);
        var entries = Assert.IsType<EmbeddedZipInfo>(analysis.Body?.EmbeddedZip).Entries;
        var referencedPaths = entries.Where(entry => entry.IsReferenced).Select(entry => entry.Path).ToHashSet(StringComparer.Ordinal);
        var orphanPaths = entries.Where(entry => !entry.IsReferenced).Select(entry => entry.Path).ToHashSet(StringComparer.Ordinal);
        Assert.Contains(FakeOrphanPath, orphanPaths);

        var action = new OrphanEmbedsAction();
        var applicability = action.Detect(DetectContext(analysis));
        Assert.True(applicability.Applies);

        var result = action.Apply(ApplyContext(gbx, analysis));
        Assert.True(result.Changed);
        Assert.Contains(FakeOrphanPath, result.Notes);
        Assert.InRange(result.Notes.Count, 0, 20);

        using var zip = gbx.Node.OpenReadEmbeddedZipData();
        var remaining = zip.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(FakeOrphanPath, remaining);
        Assert.Empty(orphanPaths.Intersect(remaining, StringComparer.Ordinal));
        Assert.Empty(referencedPaths.Except(remaining, StringComparer.Ordinal));
        Assert.Equal(entries.Count - orphanPaths.Count, remaining.Count);

        var beforeFacts = Assert.IsType<MapFacts>(analysis.Facts);
        var validAfterFacts = beforeFacts with { EmbeddedEntryCount = entries.Count - orphanPaths.Count };
        Assert.Empty(action.ValidateResult(beforeFacts, validAfterFacts));
        var invalidAfterFacts = validAfterFacts with { EmbeddedEntryCount = validAfterFacts.EmbeddedEntryCount + 1 };
        var issue = Assert.Single(action.ValidateResult(beforeFacts, invalidAfterFacts));
        Assert.Equal("orphan-embeds.entry-count", issue.Rule);
    }

    [Fact]
    public void DetectAndApply_NoOrphans_ReturnsFrozenNoChangeResult()
    {
        var original = FakeAnalysis.Build();
        var body = Assert.IsType<BodyAnalysis>(original.Body);
        var zip = Assert.IsType<EmbeddedZipInfo>(body.EmbeddedZip);
        var analysis = original with
        {
            Body = body with
            {
                EmbeddedZip = zip with
                {
                    Entries = zip.Entries.Select(entry => entry with { IsReferenced = true }).ToArray(),
                },
            },
        };
        var action = new OrphanEmbedsAction();

        var applicability = action.Detect(DetectContext(analysis));
        Assert.False(applicability.Applies);
        Assert.Equal("no orphaned embedded items", applicability.Reason);

        var result = action.Apply(new ActionApplyContext(
            null!, null!, analysis, EmptySettings(), NullStatusSink.Instance));
        Assert.False(result.Changed);
        Assert.Equal("no orphaned embedded items", result.Summary);
        Assert.Empty(result.Notes);
    }

    private static MapAnalysis AnalyzeFile() =>
        MapAnalyzer.CreateDefault(NullStatusSink.Instance).Analyze(
            new MapSource.FromFile(SampleMap.Path),
            new AnalyzeOptions());

    private static ActionDetectContext DetectContext(MapAnalysis analysis) =>
        new(analysis, EmptySettings(), NullStatusSink.Instance);

    private static ActionApplyContext ApplyContext(Gbx<CGameCtnChallenge> gbx, MapAnalysis analysis) =>
        new(gbx, gbx.Node, analysis, EmptySettings(), NullStatusSink.Instance);

    private static IReadOnlyDictionary<string, string> EmptySettings() =>
        new Dictionary<string, string>();

    private static void EnsureCodecs()
    {
        Gbx.LZO = new Lzo();
        Gbx.ZLib = new ZLib();
    }
}
