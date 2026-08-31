using GbxSizeTree.Model;
using GbxSizeTree.Semantics;

namespace GbxSizeTree.Analysis;

/// <summary>
/// Merges the two body measurement sources: the raw scanner's exact on-disk sizes for
/// skippable chunks win over writer deltas; non-skippable chunks keep their writer-delta
/// size. Chunk order follows the writer (== file order). Disagreements become warnings,
/// the unattributed remainder becomes an explicit residual — never silently absorbed.
/// </summary>
public static class BodyMeasurementMerger
{
    public sealed record Result(
        IReadOnlyList<BodyChunkInfo> Chunks,
        long ResidualBytes,
        IReadOnlyList<AnalysisWarning> Warnings);

    public static Result Merge(
        RawBodyScan scan,
        WriterMeasurement measurement,
        IReadOnlyDictionary<uint, long> precompressedByChunk)
    {
        var warnings = new List<AnalysisWarning>();
        warnings.AddRange(scan.Warnings);

        var skippableRegions = new Dictionary<uint, Queue<RawChunkRegion>>();
        var nonSkippableRegions = new Dictionary<uint, Queue<RawChunkRegion>>();
        foreach (var region in scan.Regions)
        {
            var target = region.Kind switch
            {
                RawChunkKind.Skippable => skippableRegions,
                RawChunkKind.NonSkippable => nonSkippableRegions,
                _ => null,
            };
            if (target is null)
            {
                continue;
            }
            if (!target.TryGetValue(region.ChunkId, out var queue))
            {
                target[region.ChunkId] = queue = new Queue<RawChunkRegion>();
            }
            queue.Enqueue(region);
        }

        var chunks = new List<BodyChunkInfo>(measurement.Deltas.Count);
        long attributed = 0;
        foreach (var delta in measurement.Deltas)
        {
            var meta = ChunkCatalog.Describe(delta.ChunkId);
            long bytes = delta.Bytes;
            var confidence = SizeConfidence.WriterDelta;
            long? offset = null;

            if (delta.Skippable
                && skippableRegions.TryGetValue(delta.ChunkId, out var regions)
                && regions.Count > 0)
            {
                var region = regions.Dequeue();
                offset = region.Offset;
                if (Math.Abs(region.Length - delta.Bytes) > 16)
                {
                    warnings.Add(new AnalysisWarning(
                        "measure.skippable-disagreement",
                        $"chunk 0x{delta.ChunkId:X8}: on-disk {region.Length:N0} B vs re-serialized {delta.Bytes:N0} B"));
                }
                bytes = region.Length;
                confidence = SizeConfidence.ExactOnDisk;
            }
            else if (nonSkippableRegions.TryGetValue(delta.ChunkId, out var gapRegions)
                && gapRegions.Count > 0)
            {
                offset = gapRegions.Dequeue().Offset;
            }

            precompressedByChunk.TryGetValue(delta.ChunkId, out var precompressed);
            attributed += bytes;
            chunks.Add(new BodyChunkInfo(
                delta.ChunkId, meta.Name, meta.Category, meta.Description,
                bytes, confidence, offset, delta.Skippable, chunks.Count,
                Math.Min(precompressed, bytes)));
        }

        var residual = measurement.ReferenceUncompressedBytes - attributed;
        var residualFraction = measurement.ReferenceUncompressedBytes > 0
            ? Math.Abs((double)residual) / measurement.ReferenceUncompressedBytes
            : 0;
        if (residualFraction > 0.005)
        {
            warnings.Add(new AnalysisWarning(
                "measure.residual",
                $"unattributed residual is {residual:N0} B ({residualFraction:P2} of the body)"));
        }

        return new Result(chunks, residual, warnings);
    }
}
