using GbxSizeTree.Model;
using Spectre.Console;

namespace GbxSizeTree.Cli.Rendering;

/// <summary>
/// Renders drilldowns for the GBX header table, body chunks, embedded ZIP, and lightmap layout documented in docs/FORMAT-NOTES.md.
/// </summary>
public static class DrilldownTables
{
    private const long MinimumBodyChunkRowBytes = 420;

    public static void Render(IAnsiConsole console, MapAnalysis analysis, int topN)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(analysis);

        RenderHeaderChunks(console, analysis.Header.Chunks);
        if (analysis.Body is not { } body)
        {
            return;
        }

        RenderBodyChunks(console, body);
        RenderEmbeddedEntries(console, body.EmbeddedZip, Math.Max(0, topN));
        RenderLightmapFrames(console, body.Lightmap);
    }

    private static void RenderHeaderChunks(IAnsiConsole console, IReadOnlyList<HeaderChunkInfo> chunks)
    {
        if (chunks.Count == 0)
        {
            return;
        }

        var total = chunks.Sum(chunk => chunk.Bytes);
        var table = NewTable("Header chunks", "Name", "Id", "Bytes", "%", "Heavy");
        foreach (var chunk in chunks.OrderByDescending(chunk => chunk.Bytes).ThenBy(chunk => chunk.ChunkId))
        {
            table.AddRow(
                Markup.Escape(chunk.Name),
                Hex(chunk.ChunkId),
                SizeFormat.ShortBytes(chunk.Bytes),
                SizeFormat.Percent(chunk.Bytes, total),
                chunk.Heavy ? "yes" : string.Empty);
        }

        console.Write(table);
    }

    private static void RenderBodyChunks(IAnsiConsole console, BodyAnalysis body)
    {
        if (body.Chunks.Count == 0)
        {
            return;
        }

        var table = NewTable("Body chunks", "Name", "Id", "Bytes", "%", "Confidence");
        var ordered = body.Chunks
            .OrderByDescending(chunk => chunk.Bytes)
            .ThenBy(chunk => chunk.Order)
            .ToList();
        foreach (var chunk in ordered.Where(chunk => chunk.Bytes >= MinimumBodyChunkRowBytes))
        {
            table.AddRow(
                Markup.Escape(chunk.Name),
                Hex(chunk.ChunkId),
                SizeFormat.ShortBytes(chunk.Bytes),
                SizeFormat.Percent(chunk.Bytes, body.UncompressedBytes),
                Theme.ConfidenceGlyph(chunk.Confidence));
        }

        var smaller = ordered.Where(chunk => chunk.Bytes < MinimumBodyChunkRowBytes).ToList();
        if (smaller.Count > 0)
        {
            var combinedBytes = smaller.Sum(chunk => chunk.Bytes);
            table.AddRow(
                $"and {smaller.Count} more",
                "—",
                SizeFormat.ShortBytes(combinedBytes),
                SizeFormat.Percent(combinedBytes, body.UncompressedBytes),
                "—");
        }

        console.Write(table);
    }

    private static void RenderEmbeddedEntries(IAnsiConsole console, EmbeddedZipInfo? zip, int topN)
    {
        if (zip is null || zip.Entries.Count == 0)
        {
            return;
        }

        var table = NewTable("Embedded ZIP entries", "Path", "Compressed", "Uncompressed", "Method", "Referenced");
        var entries = zip.Entries
            .OrderByDescending(entry => entry.CompressedBytes)
            .ThenBy(entry => entry.Path, StringComparer.Ordinal)
            .Take(topN);
        foreach (var entry in entries)
        {
            table.AddRow(
                Markup.Escape(entry.Path),
                SizeFormat.ShortBytes(entry.CompressedBytes),
                SizeFormat.ShortBytes(entry.UncompressedBytes),
                Markup.Escape(entry.Method),
                entry.IsReferenced ? "yes" : "no");
        }

        var hidden = zip.Entries.Count - Math.Min(topN, zip.Entries.Count);
        if (hidden > 0)
        {
            table.Caption = new TableTitle($"… {hidden} more");
        }

        console.Write(table);
    }

    private static void RenderLightmapFrames(IAnsiConsole console, LightmapInfo? lightmap)
    {
        if (lightmap is null || lightmap.Frames.Count == 0)
        {
            return;
        }

        var table = NewTable(
            "Lightmap frames",
            "Frame",
            "Resolution",
            "Blob 1",
            "Blob 2",
            "Blob 3",
            "Total");
        foreach (var frame in lightmap.Frames.OrderBy(frame => frame.Index))
        {
            var blobs = frame.BlobBytes.Select(SizeFormat.ShortBytes).ToList();
            table.AddRow(
                frame.Index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                FrameResolution(frame),
                blobs.ElementAtOrDefault(0) ?? "—",
                blobs.ElementAtOrDefault(1) ?? "—",
                blobs.ElementAtOrDefault(2) ?? "—",
                SizeFormat.ShortBytes(frame.BlobBytes.Sum()));
        }

        console.Write(table);
    }

    private static string FrameResolution(LightmapFrameInfo frame)
    {
        var resolutions = frame.BlobBytes
            .Select((bytes, index) => new
            {
                Bytes = bytes,
                Dimensions = frame.BlobDimensions?.ElementAtOrDefault(index),
            })
            .Where(blob => blob.Bytes > 0)
            .Select(blob => blob.Dimensions is { } dimensions
                ? $"{dimensions.Width}×{dimensions.Height}"
                : "—")
            .ToList();
        if (resolutions.Count == 0)
        {
            return "—";
        }

        return resolutions.All(resolution => resolution == resolutions[0])
            ? resolutions[0]
            : string.Join(" / ", resolutions);
    }

    private static Table NewTable(string title, params string[] columns)
    {
        var table = new Table().Border(TableBorder.Rounded).Title(title);
        foreach (var column in columns)
        {
            table.AddColumn(column);
        }

        return table;
    }

    private static string Hex(uint chunkId) => $"0x{chunkId:X8}";
}
