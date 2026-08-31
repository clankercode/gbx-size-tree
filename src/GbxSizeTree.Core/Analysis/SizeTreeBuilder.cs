using GbxSizeTree.Model;

namespace GbxSizeTree.Analysis;

/// <summary>
/// Assembles the diagnostic SizeNode tree with the frozen ids from docs/CONTRACTS.md.
/// Category nodes aggregate their chunks; drilldowns add children under lightmap/embedded;
/// body.residual is always present.
/// </summary>
public static class SizeTreeBuilder
{
    public static SizeNode Build(
        string label,
        long fileBytes,
        HeaderAnalysis header,
        BodyAnalysis? body,
        IReadOnlyDictionary<uint, long> estimatedOnDisk,
        long residualOnDiskEstimate,
        int topN)
    {
        var children = new List<SizeNode> { BuildHeader(header) };
        if (body is not null)
        {
            children.Add(BuildBody(body, estimatedOnDisk, residualOnDiskEstimate, topN));
        }

        return new SizeNode("file", label, SizeCategory.Other, fileBytes, fileBytes, null,
            SizeConfidence.ExactOnDisk, null, children);
    }

    private static SizeNode BuildHeader(HeaderAnalysis header)
    {
        var children = new List<SizeNode>();
        var other = new List<SizeNode>();
        foreach (var chunk in header.Chunks)
        {
            switch (chunk.ChunkId)
            {
                case 0x03043007:
                    var detail = header.Thumbnail is { } t
                        ? $"JPEG {t.JpegBytes:N0} B{(t.Width is int w && t.Height is int h ? $", {w}x{h}" : "")}"
                        : null;
                    children.Add(SizeNode.Leaf("header.thumbnail", "Thumbnail", SizeCategory.Thumbnail,
                        chunk.Bytes, SizeConfidence.ExactOnDisk, detail, onDisk: chunk.Bytes));
                    break;
                case 0x03043005:
                    children.Add(SizeNode.Leaf("header.xml", "XML metadata", SizeCategory.Metadata,
                        chunk.Bytes, SizeConfidence.ExactOnDisk, onDisk: chunk.Bytes));
                    break;
                default:
                    other.Add(SizeNode.Leaf($"header.chunk.0x{chunk.ChunkId:X8}", chunk.Name,
                        SizeCategory.Metadata, chunk.Bytes, SizeConfidence.ExactOnDisk, onDisk: chunk.Bytes));
                    break;
            }
        }
        if (other.Count > 0)
        {
            children.Add(new SizeNode("header.other", $"{other.Count} other chunks", SizeCategory.Metadata,
                other.Sum(o => o.UncompressedBytes), other.Sum(o => o.UncompressedBytes), null,
                SizeConfidence.ExactOnDisk, null, other));
        }

        return new SizeNode("header", "Header (uncompressed)", SizeCategory.Header,
            header.TotalBytes, header.TotalBytes, null, SizeConfidence.ExactOnDisk, null, children);
    }

    private static SizeNode BuildBody(
        BodyAnalysis body,
        IReadOnlyDictionary<uint, long> estimatedOnDisk,
        long residualOnDiskEstimate,
        int topN)
    {
        var singletons = new Dictionary<SizeCategory, string>
        {
            [SizeCategory.Blocks] = "body.blocks",
            [SizeCategory.Items] = "body.items",
            [SizeCategory.BakedBlocks] = "body.bakedblocks",
            [SizeCategory.MediaTracker] = "body.mediatracker",
            [SizeCategory.ScriptMetadata] = "body.scriptmetadata",
            [SizeCategory.FreeBlocks] = "body.freeblocks",
        };

        var children = new List<SizeNode>();
        var elemArrays = new List<SizeNode>();
        var other = new List<SizeNode>();

        foreach (var chunk in body.Chunks)
        {
            estimatedOnDisk.TryGetValue(chunk.ChunkId, out var est);
            long? estOnDisk = est > 0 ? est : null;
            switch (chunk.Category)
            {
                case SizeCategory.Lightmap:
                    children.Add(BuildLightmap(chunk, body.Lightmap, estOnDisk));
                    break;
                case SizeCategory.EmbeddedItems:
                    children.Add(BuildEmbedded(chunk, body.EmbeddedZip, estOnDisk, topN));
                    break;
                case SizeCategory.PerElementArrays:
                    elemArrays.Add(SizeNode.Leaf(ElemArrayId(chunk.ChunkId), chunk.Name,
                        chunk.Category, chunk.Bytes, chunk.Confidence, chunk.Description, estOnDisk: estOnDisk));
                    break;
                default:
                    if (singletons.TryGetValue(chunk.Category, out var id))
                    {
                        children.Add(SizeNode.Leaf(id, chunk.Name, chunk.Category, chunk.Bytes,
                            chunk.Confidence, chunk.Description, estOnDisk: estOnDisk));
                    }
                    else
                    {
                        other.Add(SizeNode.Leaf($"body.other.0x{chunk.ChunkId:X8}", chunk.Name,
                            SizeCategory.Other, chunk.Bytes, chunk.Confidence, chunk.Description,
                            estOnDisk: estOnDisk));
                    }
                    break;
            }
        }

        if (elemArrays.Count > 0)
        {
            children.Add(new SizeNode("body.elemarrays", "Per-element arrays", SizeCategory.PerElementArrays,
                elemArrays.Sum(n => n.UncompressedBytes), null,
                SumEstimates(elemArrays), SizeConfidence.ExactOnDisk, null, elemArrays));
        }
        if (other.Count > 0)
        {
            children.Add(new SizeNode("body.other", $"{other.Count} other chunks", SizeCategory.Other,
                other.Sum(n => n.UncompressedBytes), null,
                SumEstimates(other), SizeConfidence.WriterDelta, null, other));
        }

        children.Add(SizeNode.Leaf("body.residual", "Unattributed residual", SizeCategory.Residual,
            body.UnattributedBytes, SizeConfidence.Estimated,
            estOnDisk: residualOnDiskEstimate > 0 ? residualOnDiskEstimate : null));

        children.Sort(static (a, b) => b.UncompressedBytes.CompareTo(a.UncompressedBytes));

        return new SizeNode("body", "Body", SizeCategory.Other,
            body.UncompressedBytes, body.CompressedBytes, null, SizeConfidence.ExactOnDisk,
            $"one LZO stream, {body.Ratio:P1} of uncompressed", children);
    }

    private static SizeNode BuildLightmap(BodyChunkInfo chunk, LightmapInfo? lm, long? estOnDisk)
    {
        var children = new List<SizeNode>();
        if (lm is { HasLightmaps: true })
        {
            var frames = lm.Frames
                .Select(f => SizeNode.Leaf($"body.lightmap.webp.frame{f.Index}", $"Frame {f.Index}",
                    SizeCategory.Lightmap, f.BlobBytes.Sum(), SizeConfidence.ExactOnDisk,
                    WebpImageCount(f.BlobBytes.Count(b => b > 0))))
                .ToList();
            children.Add(new SizeNode("body.lightmap.webp", "WebP shadow frames", SizeCategory.Lightmap,
                lm.WebpBytesTotal, null, lm.WebpBytesTotal, SizeConfidence.ExactOnDisk,
                lm.FrameCount == 3 ? "3 frames = dynamic daylight" : null, frames));
            children.Add(SizeNode.Leaf("body.lightmap.cache", "Mapping cache (zlib)", SizeCategory.Lightmap,
                lm.ZlibCompressedBytes, SizeConfidence.ExactOnDisk,
                $"{lm.ZlibUncompressedBytes:N0} B uncompressed", estOnDisk: lm.ZlibCompressedBytes));
        }

        return new SizeNode("body.lightmap", chunk.Name, SizeCategory.Lightmap,
            chunk.Bytes, null, estOnDisk, chunk.Confidence, chunk.Description, children);
    }

    private static string WebpImageCount(int count) =>
        $"{count} WebP {(count == 1 ? "image" : "images")}";

    private static SizeNode BuildEmbedded(BodyChunkInfo chunk, EmbeddedZipInfo? zip, long? estOnDisk, int topN)
    {
        var children = new List<SizeNode>();
        if (zip is not null && zip.Entries.Count > 0)
        {
            var bySize = zip.Entries.OrderByDescending(e => e.CompressedBytes).ToList();
            foreach (var entry in bySize.Take(topN))
            {
                children.Add(SizeNode.Leaf($"body.embedded.entry:{entry.Path}", entry.Path,
                    SizeCategory.EmbeddedItems, entry.CompressedBytes, SizeConfidence.ExactOnDisk,
                    $"{entry.UncompressedBytes:N0} B unzipped{(entry.IsReferenced ? "" : ", ORPHAN")}"));
            }
            if (bySize.Count > topN)
            {
                var rest = bySize.Skip(topN).Sum(e => e.CompressedBytes);
                children.Add(SizeNode.Leaf("body.embedded.other", $"… {bySize.Count - topN} more entries",
                    SizeCategory.EmbeddedItems, rest, SizeConfidence.ExactOnDisk));
            }
        }

        return new SizeNode("body.embedded", chunk.Name, SizeCategory.EmbeddedItems,
            chunk.Bytes, null, estOnDisk, chunk.Confidence, chunk.Description, children);
    }

    private static string ElemArrayId(uint chunkId) => chunkId switch
    {
        0x03043062 => "body.elemarrays.colors",
        0x03043068 => "body.elemarrays.lmquality",
        0x03043069 => "body.elemarrays.macroblock",
        _ => $"body.elemarrays.0x{chunkId:X8}",
    };

    private static long? SumEstimates(IReadOnlyList<SizeNode> nodes)
    {
        long sum = 0;
        var any = false;
        foreach (var node in nodes)
        {
            if (node.EstimatedOnDiskBytes is { } est)
            {
                sum += est;
                any = true;
            }
        }
        return any ? sum : null;
    }
}
