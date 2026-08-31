using GbxSizeTree.Model;
using Spectre.Console;

namespace GbxSizeTree.Cli.Rendering;

/// <summary>
/// Renders the pre-compressed lightmap components against the normalized on-disk
/// contribution used by the category chart.
/// </summary>
internal static class LightmapBreakdownRenderer
{
    public static void Render(IAnsiConsole console, LightmapInfo lightmap, long onDiskBytes)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(lightmap);

        if (!lightmap.HasLightmaps || onDiskBytes <= 0)
        {
            return;
        }

        var webpBytes = Math.Max(0, lightmap.WebpBytesTotal);
        var cacheBytes = Math.Max(0, lightmap.ZlibCompressedBytes);
        var componentBytes = webpBytes + cacheBytes;
        if (componentBytes > onDiskBytes)
        {
            webpBytes = webpBytes * onDiskBytes / componentBytes;
            cacheBytes = onDiskBytes - webpBytes;
        }

        var overheadBytes = onDiskBytes - webpBytes - cacheBytes;
        var table = new Table()
            .Border(TableBorder.Simple)
            .Title("Lightmap breakdown")
            .AddColumn("Component")
            .AddColumn(new TableColumn("On disk").RightAligned())
            .AddColumn(new TableColumn("%").RightAligned())
            .AddColumn("Details");

        AddRow(
            table,
            "WebP shadow images",
            webpBytes,
            onDiskBytes,
            LightmapDetails(lightmap));
        AddRow(
            table,
            "Mapping cache (zlib)",
            cacheBytes,
            onDiskBytes,
            $"{SizeFormat.ShortBytes(lightmap.ZlibUncompressedBytes)} uncompressed");

        if (overheadBytes > 0)
        {
            AddRow(table, "Chunk overhead", overheadBytes, onDiskBytes, "framing and metadata");
        }

        table.Caption = new TableTitle(
            $"Total {SizeFormat.ShortBytes(onDiskBytes)} · matches category contribution");
        console.Write(table);
    }

    private static string LightmapDetails(LightmapInfo lightmap)
    {
        var frameLabel = $"{lightmap.FrameCount} {(lightmap.FrameCount == 1 ? "frame" : "frames")}";
        var nonEmptyBlobs = lightmap.Frames
            .SelectMany(frame => frame.BlobBytes.Select((bytes, index) => new
            {
                Bytes = bytes,
                Dimensions = frame.BlobDimensions?.ElementAtOrDefault(index),
            }))
            .Where(blob => blob.Bytes > 0)
            .ToList();
        if (nonEmptyBlobs.Count == 0)
        {
            return frameLabel;
        }

        var resolutions = nonEmptyBlobs
            .Where(blob => blob.Dimensions is not null)
            .Select(blob => FormatDimensions(blob.Dimensions!))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var hasUnknown = nonEmptyBlobs.Any(blob => blob.Dimensions is null);
        var resolutionLabel = resolutions.Count switch
        {
            0 => "resolution unknown",
            1 => resolutions[0],
            _ => "mixed resolutions",
        };
        return hasUnknown
            ? $"{frameLabel} · {resolutionLabel} · partly unknown"
            : $"{frameLabel} · {resolutionLabel}";
    }

    private static string FormatDimensions(LightmapDimensions dimensions) =>
        $"{dimensions.Width}×{dimensions.Height}";

    private static void AddRow(
        Table table,
        string component,
        long bytes,
        long totalBytes,
        string details)
    {
        if (bytes <= 0)
        {
            return;
        }

        table.AddRow(
            Markup.Escape(component),
            Markup.Escape(SizeFormat.ShortBytes(bytes)),
            Markup.Escape(SizeFormat.Percent(bytes, totalBytes)),
            Markup.Escape(details));
    }
}
