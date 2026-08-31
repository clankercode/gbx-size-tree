using System.Globalization;

namespace GbxSizeTree.Cli.Rendering;

/// <summary>
/// Invariant byte formatting for the exact and derived GBX sizes described in docs/FORMAT-NOTES.md.
/// </summary>
public static class SizeFormat
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Bytes(long bytes) =>
        $"{bytes.ToString("N0", Invariant)} B ({ShortBytes(bytes)})";

    public static string ShortBytes(long bytes)
    {
        var absolute = Math.Abs((double)bytes);
        if (absolute < 1024)
        {
            return $"{bytes.ToString("N0", Invariant)} B";
        }

        if (absolute < 1024 * 1024)
        {
            return $"{(bytes / 1024d).ToString("N1", Invariant)} KiB";
        }

        if (absolute < 1024d * 1024 * 1024)
        {
            return $"{(bytes / (1024d * 1024)).ToString("N1", Invariant)} MiB";
        }

        return $"{(bytes / (1024d * 1024 * 1024)).ToString("N1", Invariant)} GiB";
    }

    public static string Percent(long part, long whole) =>
        whole == 0 ? "0.0%" : (100d * part / whole).ToString("N1", Invariant) + "%";

    public static string Percent(double fraction) =>
        (fraction * 100d).ToString("N1", Invariant) + "%";
}
