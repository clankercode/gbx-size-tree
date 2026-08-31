using GbxSizeTree.Abstractions;

namespace GbxSizeTree.Actions.Passes;

/// <summary>
/// Optimizes the JPEG payload stored by header chunk 0x03043007; see docs/FORMAT-NOTES.md §CGameCtnChallenge chunk map.
/// </summary>
public sealed class ThumbnailAction : IMapAction
{
    private readonly IJpegRecoder recoder;
    private CachedRecode? cachedRecode;
    private string consequence = string.Empty;

    public ThumbnailAction(IJpegRecoder? recoder = null)
    {
        this.recoder = recoder ?? new ImageSharpJpegRecoder();
    }

    public string Id => "thumbnail";
    public string Title => "Optimize thumbnail";
    public ActionTier Tier => ActionTier.BenignLossy;
    public string Consequence => consequence;
    public string HowToManually => string.Empty;
    public bool DefaultOn => false;
    public int Order => 40;

    public ActionApplicability Detect(ActionDetectContext ctx)
    {
        var modeText = GetMode(ctx.Settings);
        if (!ThumbnailMode.TryParse(modeText, out var mode))
        {
            consequence = string.Empty;
            return ActionApplicability.No($"unknown thumbnail mode '{modeText}'");
        }

        consequence = mode.Consequence;
        var info = ctx.Analysis.Header.Thumbnail;
        if (mode.Kind == ThumbnailModeKind.Keep)
        {
            return ActionApplicability.No("thumbnail mode is keep");
        }

        if (info is null)
        {
            return ActionApplicability.No("map has no thumbnail");
        }

        if (mode.Kind == ThumbnailModeKind.Strip)
        {
            return new ActionApplicability(true, info.JpegBytes, EstimateKind.Computed,
                "thumbnail JPEG can be removed");
        }

        if (mode.Kind == ThumbnailModeKind.Lossless)
        {
            return info.StrippableMetadataBytes > 0
                ? new ActionApplicability(true, info.StrippableMetadataBytes, EstimateKind.Computed,
                    "JPEG metadata can be removed without decoding pixels")
                : ActionApplicability.No("thumbnail has no strippable JPEG metadata");
        }

        var thumbnail = ctx.Map?.Thumbnail;
        if (thumbnail is null)
        {
            return ActionApplicability.No("thumbnail bytes are unavailable for measured recoding");
        }

        var result = recoder.Recode(thumbnail, mode.MaxDimension, mode.Quality);
        cachedRecode = new CachedRecode(modeText, thumbnail, result);
        return result is null
            ? ActionApplicability.No("re-encoded thumbnail is not smaller")
            : new ActionApplicability(true, thumbnail.LongLength - result.LongLength, EstimateKind.Measured,
                "re-encoded thumbnail was measured smaller");
    }

    public ActionResult Apply(ActionApplyContext ctx)
    {
        var modeText = GetMode(ctx.Settings);
        if (!ThumbnailMode.TryParse(modeText, out var mode))
        {
            consequence = string.Empty;
            return ActionResult.NoChange($"unknown thumbnail mode '{modeText}'");
        }

        consequence = mode.Consequence;
        if (mode.Kind == ThumbnailModeKind.Keep)
        {
            return ActionResult.NoChange("thumbnail mode is keep");
        }

        var thumbnail = ctx.Map.Thumbnail;
        if (thumbnail is null)
        {
            return ActionResult.NoChange("map has no thumbnail");
        }

        if (mode.Kind == ThumbnailModeKind.Strip)
        {
            ctx.Map.Thumbnail = null;
            ctx.Map.RemoveChunk<GBX.NET.Engines.Game.CGameCtnChallenge.HeaderChunk03043007>();
            return new ActionResult(true, "removed thumbnail", []);
        }

        if (mode.Kind == ThumbnailModeKind.Lossless)
        {
            var stripped = StripMetadata(thumbnail);
            if (stripped is null || stripped.Length >= thumbnail.Length)
            {
                return ActionResult.NoChange("thumbnail has no removable JPEG metadata");
            }

            ctx.Map.Thumbnail = stripped;
            return new ActionResult(true, "removed thumbnail JPEG metadata", []);
        }

        var recoded = cachedRecode is { } cached
            && cached.Mode == modeText
            && ReferenceEquals(cached.Source, thumbnail)
                ? cached.Result
                : recoder.Recode(thumbnail, mode.MaxDimension, mode.Quality);
        if (recoded is null)
        {
            return ActionResult.NoChange("re-encoded thumbnail is not smaller");
        }

        ctx.Map.Thumbnail = recoded;
        return new ActionResult(true, "re-encoded thumbnail", []);
    }

    internal static byte[]? StripMetadata(byte[] jpeg)
    {
        // JPEG marker accounting for chunk 0x03043007 is documented in docs/FORMAT-NOTES.md.
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            return null;
        }

        using var output = new MemoryStream(jpeg.Length);
        output.Write(jpeg, 0, 2);
        var offset = 2;
        while (offset < jpeg.Length)
        {
            var markerStart = offset;
            if (jpeg[offset++] != 0xFF)
            {
                return null;
            }

            while (offset < jpeg.Length && jpeg[offset] == 0xFF)
            {
                offset++;
            }

            if (offset >= jpeg.Length)
            {
                return null;
            }

            var marker = jpeg[offset++];
            if (marker == 0xD9)
            {
                output.Write(jpeg, markerStart, offset - markerStart);
                return output.ToArray();
            }

            if (marker is 0x01 or >= 0xD0 and <= 0xD8)
            {
                output.Write(jpeg, markerStart, offset - markerStart);
                continue;
            }

            if (offset + 2 > jpeg.Length)
            {
                return null;
            }

            var segmentLength = (jpeg[offset] << 8) | jpeg[offset + 1];
            if (segmentLength < 2 || offset + segmentLength > jpeg.Length)
            {
                return null;
            }

            var segmentEnd = offset + segmentLength;
            if (marker == 0xDA)
            {
                output.Write(jpeg, markerStart, jpeg.Length - markerStart);
                return output.ToArray();
            }

            var strip = marker is >= 0xE1 and <= 0xEF or 0xFE;
            if (!strip)
            {
                output.Write(jpeg, markerStart, segmentEnd - markerStart);
            }

            offset = segmentEnd;
        }

        return null;
    }

    private static string GetMode(IReadOnlyDictionary<string, string> settings) =>
        settings.TryGetValue("mode", out var mode) ? mode : "keep";

    private sealed record CachedRecode(string Mode, byte[] Source, byte[]? Result);

    private enum ThumbnailModeKind
    {
        Keep,
        Strip,
        Lossless,
        Recompress,
        Downscale,
    }

    private readonly record struct ThumbnailMode(ThumbnailModeKind Kind, int Quality, int? MaxDimension)
    {
        public string Consequence => Kind switch
        {
            ThumbnailModeKind.Strip => "map loses its thumbnail",
            ThumbnailModeKind.Lossless => "metadata stripped (lossless)",
            ThumbnailModeKind.Recompress or ThumbnailModeKind.Downscale => "thumbnail re-encoded (lossy)",
            _ => string.Empty,
        };

        public static bool TryParse(string value, out ThumbnailMode mode)
        {
            mode = value switch
            {
                "keep" => new ThumbnailMode(ThumbnailModeKind.Keep, 0, null),
                "strip" => new ThumbnailMode(ThumbnailModeKind.Strip, 0, null),
                "lossless" => new ThumbnailMode(ThumbnailModeKind.Lossless, 0, null),
                _ => default,
            };

            if (value is "keep" or "strip" or "lossless")
            {
                return true;
            }

            if (TryReadNumber(value, "recompress:", 1, 100, out var quality))
            {
                mode = new ThumbnailMode(ThumbnailModeKind.Recompress, quality, null);
                return true;
            }

            if (TryReadNumber(value, "downscale:", 32, 1024, out var maxDimension))
            {
                mode = new ThumbnailMode(ThumbnailModeKind.Downscale, 85, maxDimension);
                return true;
            }

            return false;
        }

        private static bool TryReadNumber(string value, string prefix, int min, int max, out int number)
        {
            number = 0;
            return value.StartsWith(prefix, StringComparison.Ordinal)
                && int.TryParse(value.AsSpan(prefix.Length), out number)
                && number >= min
                && number <= max;
        }
    }
}
