using System.Globalization;

namespace GbxSizeTree.Cli.Rendering;

/// <summary>Formats the independent ZIP, raw, and outer-map measurements for embedded entries.</summary>
public sealed class EmbeddedSizePresentation
{
    public const string ZipColumn = "ZIP bytes";
    public const string RawColumn = "Raw bytes";
    public const string RatioColumn = "ZIP / raw";
    public const string LeftMarginalColumn = "Left marginal bytes";
    public const string RightMarginalColumn = "Right marginal bytes";

    public string EntryNote =>
        "ZIP and raw are entry bytes, not total ZIP archive or outer-map size. ZIP / raw uses raw bytes as its denominator; " +
        "lower means fewer ZIP bytes per raw byte. Signed byte and percentage-point deltas are right minus left; " +
        "absent entry bytes count as zero, while unmeasured bytes remain unavailable.";

    public EntryCells FormatEntry(EntrySize? left, EntrySize? right)
    {
        Validate(left);
        Validate(right);

        return new(
            FormatByteTransition(left, right, size => size.ZipBytes),
            FormatByteTransition(left, right, size => size.RawBytes),
            FormatRatioTransition(left, right));
    }

    public string FormatMarginal(long? bytes, string? unavailableReason = null) => bytes is { } value
        ? FormatSignedBytes(value)
        : $"unavailable: {NormalizeReason(unavailableReason)}";

    public string FormatOuterNote(long? leftBaselineBytes, long? rightBaselineBytes) =>
        $"Original-body recompressed LZO baseline: left {FormatBaseline(leftBaselineBytes)}, " +
        $"right {FormatBaseline(rightBaselineBytes)}. Marginal values are signed baseline-minus-removal savings; " +
        "they are context-dependent and non-additive, not an allocation of map size. ZIP and raw bytes are independent.";

    private static string FormatByteTransition(
        EntrySize? left,
        EntrySize? right,
        Func<EntrySize, long?> select)
    {
        var leftBytes = left is { } leftSize ? select(leftSize) : null;
        var rightBytes = right is { } rightSize ? select(rightSize) : null;
        var values = FormatTransition(left is not null, leftBytes, right is not null, rightBytes, FormatBytes);

        if ((left is not null && leftBytes is null) || (right is not null && rightBytes is null))
        {
            return values;
        }

        var delta = (rightBytes ?? 0) - (leftBytes ?? 0);
        return $"{values} ({FormatSignedBytes(delta)})";
    }

    private static string FormatRatioTransition(EntrySize? left, EntrySize? right)
    {
        var leftRatio = left is { } leftSize ? Ratio(leftSize) : null;
        var rightRatio = right is { } rightSize ? Ratio(rightSize) : null;
        var values = FormatTransition(left is not null, leftRatio, right is not null, rightRatio, FormatRatio);
        if (leftRatio is null || rightRatio is null)
        {
            return values;
        }

        var delta = rightRatio.Value - leftRatio.Value;
        return $"{values} ({delta.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture)} pp)";
    }

    private static double? Ratio(EntrySize size) => size is { ZipBytes: { } zip, RawBytes: > 0 and var raw }
        ? 100d * zip / raw
        : null;

    private static string FormatRatio(double? ratio) => ratio is { } value
        ? $"{value.ToString("0.00", CultureInfo.InvariantCulture)}% of raw"
        : "unavailable";

    private static string FormatTransition<T>(
        bool hasLeft,
        T? left,
        bool hasRight,
        T? right,
        Func<T?, string> format) where T : struct
    {
        if (!hasLeft)
        {
            return hasRight ? format(right) : "unavailable";
        }

        if (!hasRight)
        {
            return format(left);
        }

        return $"{format(left)} → {format(right)}";
    }

    private static string FormatBytes(long? bytes) => bytes is { } value
        ? value.ToString("N0", CultureInfo.InvariantCulture)
        : "unavailable";

    private static string FormatBaseline(long? bytes) => bytes is { } value
        ? $"{FormatBytes(value)} B"
        : "unavailable";

    private static string FormatSignedBytes(long bytes) =>
        $"{bytes.ToString("+#,0;-#,0;0", CultureInfo.InvariantCulture)} B";

    private static string NormalizeReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? "not measured" : reason.Trim();

    private static void Validate(EntrySize? size)
    {
        if (size is { ZipBytes: < 0 })
        {
            throw new ArgumentOutOfRangeException(nameof(size), "ZIP bytes cannot be negative.");
        }

        if (size is { RawBytes: < 0 })
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Raw bytes cannot be negative.");
        }
    }

    public readonly record struct EntrySize(long? ZipBytes, long? RawBytes);

    public sealed record EntryCells(string ZipBytes, string RawBytes, string ZipToRawRatio);
}
