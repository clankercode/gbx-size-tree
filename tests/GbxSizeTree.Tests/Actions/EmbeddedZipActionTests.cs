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

public sealed class EmbeddedZipActionTests
{
    private const string SyntheticPath = "Items/__SyntheticCompressed.Item.Gbx";

    [Fact]
    public void Detect_WithMap_MeasuresAndProducesIntactUncompressedBodyEntries()
    {
        var (gbx, analysis) = LoadSample();
        var originalBytes = Assert.IsType<byte[]>(gbx.Node.EmbeddedZipData).ToArray();
        var action = new EmbeddedZipAction();

        Assert.Equal("embed-zip", action.Id);
        Assert.Equal(20, action.Order);
        Assert.Equal(ActionTier.Lossless, action.Tier);
        Assert.True(action.DefaultOn);
        Assert.Equal("", action.Consequence);

        var applicability = action.Detect(DetectContext(analysis, gbx.Node));
        Assert.Equal(EstimateKind.Measured, applicability.Kind);
        Console.WriteLine($"embedded ZIP measured savings: {applicability.EstimatedSavingsBytes:N0} bytes");

        if (!applicability.Applies)
        {
            Assert.Equal(0, applicability.EstimatedSavingsBytes);
            Assert.Equal("already optimal", applicability.Reason);
            return;
        }

        var result = action.Apply(ApplyContext(gbx, analysis));
        Assert.True(result.Changed, applicability.Detail);
        var optimizedBytes = Assert.IsType<byte[]>(gbx.Node.EmbeddedZipData);

        using var original = OpenZip(originalBytes);
        using var optimized = OpenZip(optimizedBytes);
        Assert.Equal(original.Entries.Count, optimized.Entries.Count);
        Assert.Equal(
            original.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.Ordinal),
            optimized.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.Ordinal));

        foreach (var entry in optimized.Entries.Where(IsGbx))
        {
            var bytes = ReadEntry(entry);
            Assert.True(bytes.Length >= 12, $"{entry.FullName} has no 12-byte GBX container header");
            Assert.Equal("GBX", System.Text.Encoding.ASCII.GetString(bytes, 0, 3));
            Assert.Equal((byte)'U', bytes[7]);
        }
    }

    [Fact]
    public void Apply_ReopensThroughMapAndShrinksByReportedSavings()
    {
        var (gbx, analysis) = LoadSample();
        var beforeBytes = Assert.IsType<byte[]>(gbx.Node.EmbeddedZipData).LongLength;
        var beforeNames = ReadEntryNames(gbx.Node);
        var action = new EmbeddedZipAction();
        var applicability = action.Detect(DetectContext(analysis, gbx.Node));

        var result = action.Apply(ApplyContext(gbx, analysis));
        if (!result.Changed)
        {
            Assert.False(applicability.Applies);
            Assert.Equal(0, applicability.EstimatedSavingsBytes);
            return;
        }

        Assert.True(applicability.Applies);
        using var optimized = gbx.Node.OpenReadEmbeddedZipData();
        Assert.Equal(beforeNames, optimized.Entries.Select(entry => entry.FullName).ToArray());
        Assert.Equal(
            applicability.EstimatedSavingsBytes,
            beforeBytes - Assert.IsType<byte[]>(gbx.Node.EmbeddedZipData).LongLength);
    }

    [Fact]
    public void DetectAndApply_IndependentRunsAreDeterministic()
    {
        var (firstGbx, firstAnalysis) = LoadSample();
        var (secondGbx, secondAnalysis) = LoadSample();

        Optimize(firstGbx, firstAnalysis, new EmbeddedZipAction());
        Optimize(secondGbx, secondAnalysis, new EmbeddedZipAction());

        Assert.Equal(firstGbx.Node.EmbeddedZipData, secondGbx.Node.EmbeddedZipData);
    }

    [Fact]
    public void SyntheticCompressedBody_IsDecompressedBeforeRezip()
    {
        var (sourceGbx, _) = LoadSample();
        byte[] realEntryBytes;
        using (var zip = sourceGbx.Node.OpenReadEmbeddedZipData())
        {
            realEntryBytes = ReadEntry(zip.Entries.Where(IsGbx).MaxBy(entry => entry.Length)!);
        }

        Assert.Equal((byte)'U', realEntryBytes[7]);
        byte[] compressedBytes;
        using (var input = new MemoryStream(realEntryBytes, writable: false))
        using (var output = new MemoryStream())
        {
            Gbx.Compress(input, output);
            compressedBytes = output.ToArray();
        }
        Assert.Equal((byte)'C', compressedBytes[7]);

        byte[] expectedDecompressed;
        using (var input = new MemoryStream(compressedBytes, writable: false))
        using (var output = new MemoryStream())
        {
            Gbx.Decompress(input, output);
            expectedDecompressed = output.ToArray();
        }

        sourceGbx.Node.UpdateEmbeddedZipData(zip =>
        {
            var synthetic = zip.CreateEntry(SyntheticPath, CompressionLevel.NoCompression);
            using var stream = synthetic.Open();
            stream.Write(compressedBytes);
        });

        using var saved = new MemoryStream();
        sourceGbx.Save(saved);
        var syntheticMapBytes = saved.ToArray();
        var analysis = MapAnalyzer.CreateDefault(NullStatusSink.Instance).Analyze(
            new MapSource.FromBytes(syntheticMapBytes, "synthetic compressed embed"),
            new AnalyzeOptions());
        using var parsedInput = new MemoryStream(syntheticMapBytes, writable: false);
        var gbx = Gbx.Parse<CGameCtnChallenge>(parsedInput);
        var action = new EmbeddedZipAction();

        var applicability = action.Detect(DetectContext(analysis, gbx.Node));
        Assert.Equal(EstimateKind.Measured, applicability.Kind);
        var result = action.Apply(ApplyContext(gbx, analysis));
        Assert.True(result.Changed, applicability.Detail);
        Assert.Contains(result.Notes, note => note == "inner GBX bodies decompressed: 1");

        using var optimized = gbx.Node.OpenReadEmbeddedZipData();
        var syntheticBytes = ReadEntry(Assert.Single(optimized.Entries, entry => entry.FullName == SyntheticPath));
        Assert.Equal((byte)'U', syntheticBytes[7]);
        Assert.Equal(expectedDecompressed, syntheticBytes);
    }

    [Fact]
    public void Detect_WithoutMap_IsHeuristicAndApplicable()
    {
        var (_, analysis) = LoadSample();

        var applicability = new EmbeddedZipAction().Detect(DetectContext(analysis, null));

        Assert.True(applicability.Applies);
        Assert.Equal(EstimateKind.Heuristic, applicability.Kind);
        Assert.Equal(0, applicability.EstimatedSavingsBytes);
        Assert.Contains("parsed map", applicability.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private static void Optimize(Gbx<CGameCtnChallenge> gbx, MapAnalysis analysis, EmbeddedZipAction action)
    {
        _ = action.Detect(DetectContext(analysis, gbx.Node));
        _ = action.Apply(ApplyContext(gbx, analysis));
    }

    private static (Gbx<CGameCtnChallenge> Gbx, MapAnalysis Analysis) LoadSample()
    {
        SampleMap.SkipUnlessAvailable();
        EnsureCodecs();
        var analysis = MapAnalyzer.CreateDefault(NullStatusSink.Instance).Analyze(
            new MapSource.FromFile(SampleMap.Path),
            new AnalyzeOptions());
        return (Gbx.Parse<CGameCtnChallenge>(SampleMap.Path), analysis);
    }

    private static ActionDetectContext DetectContext(MapAnalysis analysis, CGameCtnChallenge? map) =>
        new(analysis, EmptySettings(), NullStatusSink.Instance, map);

    private static ActionApplyContext ApplyContext(Gbx<CGameCtnChallenge> gbx, MapAnalysis analysis) =>
        new(gbx, gbx.Node, analysis, EmptySettings(), NullStatusSink.Instance);

    private static IReadOnlyDictionary<string, string> EmptySettings() =>
        new Dictionary<string, string>();

    private static ZipArchive OpenZip(byte[] bytes) =>
        new(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);

    private static string[] ReadEntryNames(CGameCtnChallenge map)
    {
        using var zip = map.OpenReadEmbeddedZipData();
        return zip.Entries.Select(entry => entry.FullName).ToArray();
    }

    private static bool IsGbx(ZipArchiveEntry entry) =>
        entry.FullName.EndsWith(".gbx", StringComparison.OrdinalIgnoreCase);

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    private static void EnsureCodecs()
    {
        Gbx.LZO = new Lzo();
        Gbx.ZLib = new ZLib();
    }
}
