using System.Globalization;
using System.Text;
using SixLabors.Fonts;

namespace GbxSizeTree.Cli.Rendering;

internal static class DiffInfographicText
{
    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Untitled";
        var b = new StringBuilder(Math.Min(text.Length, 4096));
        foreach (var rune in text.EnumerateRunes())
        {
            if (b.Length >= 4096) break;
            if (Rune.IsControl(rune)) b.Append(' ');
            else if (rune.Value is >= 0xD800 and <= 0xDFFF) b.Append('\uFFFD');
            else b.Append(rune.ToString());
        }
        return string.Join(' ', b.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public static string CleanPath(string? path) => Clean(path?.Replace('\\', '/'));

    public static string FileName(string path)
    {
        var clean = CleanPath(path);
        var slash = clean.LastIndexOf('/');
        return slash < 0 ? clean : clean[(slash + 1)..];
    }

    public static string Fit(string text, Font font, float maxWidth)
    {
        text = Clean(text);
        if (Width(text, font) <= maxWidth) return text;
        const string ellipsis = "…";
        var runes = text.EnumerateRunes().ToArray();
        var low = 0;
        var high = runes.Length;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            var candidate = string.Concat(runes.AsSpan(0, middle).ToArray()) + ellipsis;
            if (Width(candidate, font) <= maxWidth) low = middle;
            else high = middle - 1;
        }
        return low == 0 ? ellipsis : string.Concat(runes.AsSpan(0, low).ToArray()) + ellipsis;
    }

    public static IReadOnlyList<string> Wrap(string text, Font font, float maxWidth, int maxLines)
    {
        text = Clean(text);
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = new StringBuilder();
        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : $"{current} {word}";
            if (Width(candidate, font) <= maxWidth)
            {
                current.Clear().Append(candidate);
                continue;
            }
            if (current.Length > 0) lines.Add(current.ToString());
            current.Clear().Append(Width(word, font) <= maxWidth ? word : Fit(word, font, maxWidth));
            if (lines.Count == maxLines) break;
        }
        if (lines.Count < maxLines && current.Length > 0) lines.Add(current.ToString());
        if (lines.Count == maxLines && string.Join(' ', lines) != text)
            lines[^1] = Fit(lines[^1] + "…", font, maxWidth);
        return lines;
    }

    public static string Bytes(long value)
    {
        var magnitude = value == long.MinValue ? (double)long.MaxValue + 1 : Math.Abs((double)value);
        var sign = value < 0 ? "−" : "";
        if (magnitude >= 1_000_000_000) return FormattableString.Invariant($"{sign}{magnitude / 1_000_000_000:0.00} GB");
        if (magnitude >= 1_000_000) return FormattableString.Invariant($"{sign}{magnitude / 1_000_000:0.00} MB");
        if (magnitude >= 1_000) return FormattableString.Invariant($"{sign}{magnitude / 1_000:0.0} kB");
        return sign + magnitude.ToString("0", CultureInfo.InvariantCulture) + " B";
    }

    public static string Position(double value) => double.IsFinite(value)
        ? value.ToString("0.0##", CultureInfo.InvariantCulture)
        : "—";

    private static float Width(string text, Font font) => TextMeasurer.MeasureAdvance(text, new TextOptions(font)).Width;
}
