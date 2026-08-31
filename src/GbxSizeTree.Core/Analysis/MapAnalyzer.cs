using GBX.NET;
using GBX.NET.Engines.Game;
using GbxSizeTree.Abstractions;
using GbxSizeTree.Container;
using GbxSizeTree.Measure;
using GbxSizeTree.Model;
using GbxSizeTree.Semantics;

namespace GbxSizeTree.Analysis;

/// <summary>
/// The deep module every frontend consumes: container layout + header table, GBX.NET parse,
/// scanner/writer-delta body measurement, drilldowns, attribution, and the assembled tree.
/// Accepts in-memory bytes so interactive sessions can re-analyze materialized maps.
/// </summary>
public sealed class MapAnalyzer(
    IRawBodyScanner scanner,
    IWriterDeltaMeasurer measurer,
    ICompressionAttribution attribution,
    IEmbeddedZipDrilldown embeddedZip,
    ILightmapDrilldown lightmap,
    IScriptMetadataDrilldown scriptMetadata,
    IMediaTrackerDrilldown mediaTracker,
    IStatusSink status) : IMapAnalyzer
{
    public static MapAnalyzer CreateDefault(IStatusSink status) => new(
        new SkippableChunkScanner(),
        new CumulativeChunkMeasurer(),
        new CompressedAttribution(),
        new Drilldown.EmbeddedZipDrilldown(),
        new Drilldown.LightmapDrilldown(),
        new Drilldown.ScriptMetadataDrilldown(),
        new Drilldown.MediaTrackerDrilldown(),
        status);

    public MapAnalysis Analyze(MapSource source, AnalyzeOptions options) =>
        Analyze(source, options, out _);

    /// <summary>
    /// <paramref name="parsedMap"/> exposes the map parsed during analysis (null for
    /// header-only runs) so callers can hand it to detection for MEASURED estimates
    /// without a second parse.
    /// </summary>
    public MapAnalysis Analyze(MapSource source, AnalyzeOptions options, out CGameCtnChallenge? parsedMap)
    {
        parsedMap = null;
        var fileBytes = source switch
        {
            MapSource.FromFile f => File.ReadAllBytes(f.Path),
            MapSource.FromBytes b => b.Bytes,
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };

        var layout = GbxContainerReader.Read(fileBytes);
        var warnings = new List<AnalysisWarning>();
        var header = AnalyzeHeader(fileBytes, layout);

        if (options.HeaderOnly)
        {
            var headerTree = SizeTreeBuilder.Build(source.Label, fileBytes.Length, header, null,
                new Dictionary<uint, long>(), 0, options.TopN);
            return new MapAnalysis(source.Label, fileBytes.Length, header, null, null, warnings, headerTree);
        }

        EnsureCodecs();

        Gbx<CGameCtnChallenge> gbx;
        using (status.Activity("parsing map"))
        {
            using var ms = new MemoryStream(fileBytes, writable: false);
            gbx = Gbx.Parse<CGameCtnChallenge>(ms);
        }
        var map = gbx.Node;
        parsedMap = map;

        byte[] body;
        RawBodyScan scan;
        WriterMeasurement measurement;
        using (status.Activity("measuring body chunks"))
        {
            body = DecompressedBody.GetBody(fileBytes);
            scan = scanner.Scan(body);
            measurement = measurer.Measure(gbx, map);
        }

        EmbeddedZipInfo? zip;
        LightmapInfo? lm;
        ScriptMetadataInfo? script;
        MediaTrackerInfo? mt;
        using (status.Activity("drilldowns"))
        {
            zip = options.Drilldowns ? embeddedZip.Inspect(map) : null;
            lm = options.Drilldowns
                ? lightmap.Inspect(body, FindSkippable(scan, 0x0304305B), map)
                : null;
            script = options.Drilldowns
                ? scriptMetadata.Inspect(body, FindSkippable(scan, 0x03043044), map)
                : null;
            var mtBytes = measurement.Deltas.FirstOrDefault(d => d.ChunkId == 0x03043049)?.Bytes ?? 0;
            mt = options.Drilldowns ? mediaTracker.Inspect(map, mtBytes) : null;
        }

        var precompressed = new Dictionary<uint, long>();
        if (zip is not null)
        {
            precompressed[0x03043054] = zip.ZipBytes;
        }
        if (lm is { HasLightmaps: true })
        {
            precompressed[0x0304305B] = lm.WebpBytesTotal + lm.ZlibCompressedBytes;
        }

        var merged = BodyMeasurementMerger.Merge(scan, measurement, precompressed);
        warnings.AddRange(merged.Warnings);
        var chunks = EnrichDescriptions(merged.Chunks, map);

        var uncompressed = gbx.Body.UncompressedSize;
        var compressed = gbx.Body.CompressedSize ?? fileBytes.Length;
        var attributed = attribution.Attribute(chunks, uncompressed, compressed);
        var estimates = new Dictionary<uint, long>();
        foreach (var a in attributed)
        {
            estimates.TryAdd(a.ChunkId, a.EstimatedOnDiskBytes);
        }
        var residualEstimate = (attribution as CompressedAttribution)?.ResidualEstimate ?? 0;

        var bodyAnalysis = new BodyAnalysis(
            uncompressed, compressed,
            uncompressed > 0 ? (double)compressed / uncompressed : 0,
            chunks, merged.ResidualBytes, zip, lm, script, mt);

        var counts = ElementCounts.Count(map);
        var facts = new MapFacts(
            map.MapUid ?? "", map.MapName ?? "", map.AuthorLogin ?? "",
            counts.blocks, counts.items, counts.baked, counts.free,
            lm?.HasLightmaps ?? false, lm?.FrameCount ?? 0, lm?.Version ?? 0,
            zip?.Entries.Count ?? 0);

        var tree = SizeTreeBuilder.Build(source.Label, fileBytes.Length, header, bodyAnalysis,
            estimates, residualEstimate, options.TopN);

        return new MapAnalysis(source.Label, fileBytes.Length, header, bodyAnalysis, facts, warnings, tree);
    }

    internal static void EnsureCodecs()
    {
        Gbx.LZO ??= new GBX.NET.LZO.Lzo();
        try
        {
            _ = Gbx.ZLib;
        }
        catch (Exception)
        {
            Gbx.ZLib = new GBX.NET.ZLib.ZLib();
        }
    }

    private static HeaderAnalysis AnalyzeHeader(byte[] fileBytes, GbxFileLayout layout)
    {
        ThumbnailInfo? thumbnail = null;
        int? xmlLength = null;
        foreach (var chunk in layout.HeaderChunks)
        {
            if (chunk.ChunkId == 0x03043007)
            {
                var payload = fileBytes.AsMemory((int)chunk.FileOffset, (int)chunk.Bytes);
                thumbnail = ThumbnailChunkReader.Read(payload);
            }
            else if (chunk.ChunkId == 0x03043005)
            {
                xmlLength = (int)chunk.Bytes;
            }
        }
        return new HeaderAnalysis(layout.UserDataSize, layout.HeaderChunks, thumbnail, xmlLength);
    }

    private static RawChunkRegion? FindSkippable(RawBodyScan scan, uint chunkId) =>
        scan.Regions.FirstOrDefault(r => r.Kind == RawChunkKind.Skippable && r.ChunkId == chunkId);

    private static IReadOnlyList<BodyChunkInfo> EnrichDescriptions(
        IReadOnlyList<BodyChunkInfo> chunks, CGameCtnChallenge map)
    {
        var counts = ElementCounts.Count(map);
        var result = new List<BodyChunkInfo>(chunks.Count);
        foreach (var chunk in chunks)
        {
            var detail = chunk.ChunkId switch
            {
                0x0304301F when counts.blocks > 0 =>
                    ElementCounts.PerElementDetail(chunk.Bytes, counts.blocks, "block"),
                0x03043040 when counts.items > 0 =>
                    ElementCounts.PerElementDetail(chunk.Bytes, counts.items, "item"),
                0x03043048 when counts.baked > 0 =>
                    ElementCounts.PerElementDetail(chunk.Bytes, counts.baked, "baked block"),
                0x0304305F when counts.free > 0 =>
                    ElementCounts.PerElementDetail(chunk.Bytes, counts.free, "free block"),
                _ => null,
            };
            result.Add(detail is null ? chunk : chunk with { Description = detail });
        }
        return result;
    }
}
