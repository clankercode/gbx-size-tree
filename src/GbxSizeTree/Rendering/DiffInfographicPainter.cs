using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace GbxSizeTree.Cli.Rendering;

internal static class DiffInfographicPainter
{
    private static readonly Color Background = Color.ParseHex("09131D");
    private static readonly Color Panel = Color.ParseHex("112230");
    private static readonly Color PanelEdge = Color.ParseHex("294355");
    private static readonly Color Text = Color.ParseHex("F5F1E8");
    private static readonly Color Muted = Color.ParseHex("9DB0BC");
    private static readonly Color Added = Color.ParseHex("48E5A2");
    private static readonly Color Removed = Color.ParseHex("FF647C");
    private static readonly Color Changed = Color.ParseHex("FFC857");
    private static readonly Color Cyan = Color.ParseHex("4FD8E8");
    private const float HeroDeltaMaxWidth = 598;
    private const float HeroDeltaMaxFontSize = 82;
    private const float HeroDeltaMinFontSize = 24;
    private const float TableMarkerInset = 32;
    private const float TablePathInset = 64;
    private const float TableRightInset = 28;
    private const float TableColumnGap = 18;
    private const string SizeChangeHeader = "SIZE CHANGE";
    internal static Font TableFont(DiffInfographicTableDensity density) =>
        DiffInfographicFonts.Mono(density == DiffInfographicTableDensity.Compact ? 14 : 16);
    private static Font TableHeaderFont(DiffInfographicTableDensity density) =>
        DiffInfographicFonts.Bold(density == DiffInfographicTableDensity.Compact ? 12 : 13);

    public static Image<Rgba32> Paint(DiffInfographicScene scene)
    {
        var image = new Image<Rgba32>(scene.Width, scene.Height, Background);
        image.Mutate(context =>
        {
            DrawBackdrop(context, scene);
            DrawHero(context, scene);
            DrawCounts(context, scene);
            DrawSpatial(context, scene);
            DrawDetails(context, scene);
            DrawFooter(context, scene);
        });
        return image;
    }

    private static void DrawBackdrop(IImageProcessingContext c, DiffInfographicScene scene)
    {
        c.Fill(Color.ParseHex("0D1B27"), new RectangularPolygon(0, 0, scene.Width, 14));
        for (var x = -300; x < scene.Width + 300; x += 70)
            c.Draw(Color.FromRgba(79, 216, 232, 12), 1, new PathBuilder().AddLine(x, 0, x + 420, scene.Height).Build());
        c.Fill(Color.FromRgba(72, 229, 162, 10), new EllipsePolygon(1250, 100, 440));
        c.Fill(Color.FromRgba(255, 100, 124, 8), new EllipsePolygon(80, 680, 350));
    }

    internal static Font FitHeroDeltaFont(string text) => FitFont(text, HeroDeltaMaxFontSize, HeroDeltaMinFontSize, HeroDeltaMaxWidth);

    internal static string SpatialFooterLabel(DiffInfographicScene scene) =>
        scene.Warnings.Count == 0 ? "Coverage notes: none" : $"Coverage notes: {scene.Warnings.Count:N0} below";

    private static Font FitFont(string text, float maxSize, float minSize, float maxWidth)
    {
        var maxFont = DiffInfographicFonts.MonoBold(maxSize);
        var measuredBounds = TextMeasurer.MeasureBounds(text, new TextOptions(maxFont));
        var measuredWidth = measuredBounds.X + measuredBounds.Width;
        if (measuredWidth <= maxWidth) return maxFont;
        var fittedSize = Math.Max(minSize, maxSize * maxWidth / measuredWidth);
        var fittedFont = DiffInfographicFonts.MonoBold(fittedSize);
        var fittedBounds = TextMeasurer.MeasureBounds(text, new TextOptions(fittedFont));
        if (fittedBounds.X + fittedBounds.Width <= maxWidth) return fittedFont;
        return DiffInfographicFonts.MonoBold(Math.Max(minSize, fittedSize * maxWidth / (fittedBounds.X + fittedBounds.Width)));
    }

    private static void DrawHero(IImageProcessingContext c, DiffInfographicScene scene)
    {
        c.DrawText("MAP DIFF", DiffInfographicFonts.Bold(24), Cyan, new PointF(70, 52));
        c.DrawText("A visual change brief", DiffInfographicFonts.Regular(20), Muted, new PointF(225, 57));
        var deltaColor = scene.DeltaBytes > 0 ? Removed : scene.DeltaBytes < 0 ? Added : Muted;
        var delta = scene.DeltaBytes == 0 ? "NO SIZE CHANGE" : (scene.DeltaBytes > 0 ? "+" : "−") + DiffInfographicText.Bytes(scene.DeltaBytes == long.MinValue ? long.MaxValue : Math.Abs(scene.DeltaBytes));
        var deltaFont = FitHeroDeltaFont(delta);
        c.DrawText(delta, deltaFont, deltaColor, new PointF(68, 102));
        c.DrawText("FILE SIZE", DiffInfographicFonts.Bold(17), Muted, new PointF(73, 205));

        var x = 710f;
        DrawFile(c, "OLD", scene.OldFileName, DiffInfographicText.Bytes(scene.LeftBytes), x, 92, Removed);
        c.DrawLine(Changed, 4, new PointF(730, 259), new PointF(1300, 259));
        c.Fill(Changed, new Polygon(new LinearLineSegment(new PointF(1300, 252), new PointF(1314, 259), new PointF(1300, 266))));
        DrawFile(c, "NEW", scene.NewFileName, DiffInfographicText.Bytes(scene.RightBytes), x, 280, Added);
    }

    private static void DrawFile(IImageProcessingContext c, string label, string name, string size, float x, float y, Color accent)
    {
        c.Fill(accent, Rounded(x, y, 74, 34, 17));
        c.DrawText(label, DiffInfographicFonts.Bold(16), Background, new PointF(x + 19, y + 7));
        c.DrawText(DiffInfographicText.Fit(name, DiffInfographicFonts.Bold(30), 520), DiffInfographicFonts.Bold(30), Text, new PointF(x + 92, y - 2));
        c.DrawText(size, DiffInfographicFonts.Mono(21), Muted, new PointF(x + 92, y + 40));
    }

    private static void DrawCounts(IImageProcessingContext c, DiffInfographicScene scene)
    {
        c.DrawText("EXACT CHANGE COUNTS", DiffInfographicFonts.Bold(14), Muted, new PointF(70, 378));
        DrawCountGroup(c, 70, scene.Placements);
        DrawCountGroup(c, 720, scene.Embedded);
    }

    private static void DrawCountGroup(IImageProcessingContext c, float x, DiffInfographicCountGroup group)
    {
        c.DrawText(group.Title.ToUpperInvariant(), DiffInfographicFonts.Bold(14), Muted, new PointF(x, 408));
        DrawCount(c, x, 434, "+", group.Counts.Added, "ADDED", Added);
        DrawCount(c, x + 207, 434, "−", group.Counts.Removed, "REMOVED", Removed);
        DrawCount(c, x + 414, 434, "~", group.Counts.Changed, "MODIFIED", Changed);
    }

    private static void DrawCount(IImageProcessingContext c, float x, float y, string mark, int count, string label, Color color)
    {
        c.Fill(Panel, Rounded(x, y, 196, 112, 18));
        c.Draw(PanelEdge, 2, Rounded(x, y, 196, 112, 18));
        c.Fill(color, new EllipsePolygon(x + 44, y + 56, 24));
        c.DrawText(mark, DiffInfographicFonts.Bold(26), Background, new PointF(x + 36, y + 41));
        var countText = count.ToString("N0");
        var countFont = DiffInfographicFonts.MonoBold(34);
        var bounds = TextMeasurer.MeasureBounds(countText, new TextOptions(countFont));
        if (bounds.X + bounds.Width > 108)
        {
            countFont = DiffInfographicFonts.MonoBold(Math.Max(14, 34 * 108 / (bounds.X + bounds.Width)));
        }
        c.DrawText(countText, countFont, Text, new PointF(x + 80, y + 22));
        c.DrawText(label, DiffInfographicFonts.Bold(13), color, new PointF(x + 80, y + 68));
    }

    private static void DrawSpatial(IImageProcessingContext c, DiffInfographicScene scene)
    {
        const float x = 70;
        const float y = 570;
        const float w = 1260;
        const float h = 420;
        c.Fill(Panel, Rounded(x, y, w, h, 24));
        c.Draw(PanelEdge, 2, Rounded(x, y, w, h, 24));
        c.DrawText("XZ SPATIAL CONTEXT", DiffInfographicFonts.Bold(23), Text, new PointF(x + 30, y + 25));
        c.DrawText("Where placement changes land", DiffInfographicFonts.Regular(18), Muted, new PointF(x + 287, y + 31));
        Legend(c, x + 880, y + 33, Added, "added"); Legend(c, x + 1000, y + 33, Removed, "removed"); Legend(c, x + 1140, y + 33, Changed, "modified");

        const float px = x + 30;
        const float py = y + 78;
        const float pw = w - 60;
        const float ph = h - 130;
        c.Fill(Color.ParseHex("0B1822"), Rounded(px, py, pw, ph, 14));
        for (var i = 1; i < 6; i++)
        {
            var gx = px + pw * i / 6;
            c.Draw(Color.FromRgba(157, 176, 188, 26), 1, new PathBuilder().AddLine(gx, py, gx, py + ph).Build());
            var gy = py + ph * i / 6;
            c.Draw(Color.FromRgba(157, 176, 188, 26), 1, new PathBuilder().AddLine(px, gy, px + pw, gy).Build());
        }
        foreach (var point in scene.Spatial.Context)
            c.Fill(Color.FromRgba(157, 176, 188, 35), new EllipsePolygon(px + point.X * pw, py + (1 - point.Z) * ph, 2.1f));
        foreach (var point in scene.Spatial.Changes)
        {
            var color = PointColor(point.Kind);
            var cx = px + point.X * pw;
            var cy = py + (1 - point.Z) * ph;
            c.Fill(Color.FromRgba(color.ToPixel<Rgba32>().R, color.ToPixel<Rgba32>().G, color.ToPixel<Rgba32>().B, 35), new EllipsePolygon(cx, cy, 10));
            if (point.Kind == DiffInfographicChangeKind.Removed)
            {
                c.Draw(color, 3, new PathBuilder().AddLine(cx - 5, cy - 5, cx + 5, cy + 5).Build());
                c.Draw(color, 3, new PathBuilder().AddLine(cx + 5, cy - 5, cx - 5, cy + 5).Build());
            }
            else c.Fill(color, new EllipsePolygon(cx, cy, point.Kind == DiffInfographicChangeKind.Added ? 4 : 5));
        }
            DrawInlineSpatialFooter(c, scene, px, py + ph + 17, pw);
    }

    private static void DrawInlineSpatialFooter(IImageProcessingContext c, DiffInfographicScene scene, float x, float y, float width)
    {
        var rangeFont = DiffInfographicFonts.Mono(15);
        var coverageFont = DiffInfographicFonts.MonoBold(15);
        var coverage = SpatialFooterLabel(scene);
        var coverageWidth = TextMeasurer.MeasureAdvance(coverage, new TextOptions(coverageFont)).Width;
        var maxRangeWidth = Math.Max(80, width - coverageWidth - 32);
        var range = DiffInfographicText.Fit(scene.Spatial.RangeLabel, rangeFont, maxRangeWidth);
        c.DrawText(range, rangeFont, Muted, new PointF(x, y));
        c.DrawText(coverage, coverageFont, Changed, new PointF(x + width - coverageWidth, y));
    }

    private static void Legend(IImageProcessingContext c, float x, float y, Color color, string text)
    {
        c.Fill(color, new EllipsePolygon(x, y + 9, 5));
        c.DrawText(text, DiffInfographicFonts.Regular(16), Muted, new PointF(x + 14, y));
    }

    private static void DrawDetails(IImageProcessingContext c, DiffInfographicScene scene)
    {
        var detailSections = scene.Sections.Where(x => x.Top >= 1000).ToArray();
        foreach (var section in detailSections)
        {
            var accent = section.Id switch { "embedded-highlights" or "embedded-changes" => Cyan, "deep-properties" or "chunks" => Changed, "metadata" => Added, _ => Removed };
            c.Fill(Panel, Rounded(section.Left, section.Top, section.Right - section.Left, section.Bottom - section.Top, 22));
            c.Draw(PanelEdge, 2, Rounded(section.Left, section.Top, section.Right - section.Left, section.Bottom - section.Top, 22));
            c.Fill(accent, Rounded(section.Left, section.Top, 8, section.Bottom - section.Top, 4));
            c.DrawText(section.Title.ToUpperInvariant(), DiffInfographicFonts.Bold(20), accent, new PointF(section.Left + 30, section.Top + 24));
            float top = section.Top + DiffInfographicLayout.SectionHeader;
            if (section.Table is { Rows.Count: > 0 } table) top = DrawTable(c, section, table, top);
            foreach (var line in section.Lines)
            {
                if (top + DiffInfographicLayout.LineHeight > section.Bottom) break;
                var markerColor = line.Text.StartsWith('+') ? Added : line.Text.StartsWith('−') ? Removed : line.Text.StartsWith("WARNING", StringComparison.Ordinal) ? Changed : Text;
                var font = line.Mono ? DiffInfographicFonts.Mono(17) : DiffInfographicFonts.Regular(18);
                c.Fill(markerColor, new EllipsePolygon(section.Left + 41, top + 10, 3));
                var wrapped = DiffInfographicText.Wrap(line.Text, font, section.Right - section.Left - 100, 2);
                foreach (var row in wrapped)
                {
                    c.DrawText(row, font, Text, new PointF(section.Left + 60, top));
                    top += 23;
                }
                top += 12;
            }
        }
    }

    /// Path column budget, measured from the widest signed size change actually present so an extreme
    /// value (up to ±9223372036.85 GB) reserves its own room instead of overlapping the path.
    internal static InfographicTableColumns MeasureTableColumns(
        IReadOnlyList<DiffInfographicTableRow> rows,
        float left,
        float right,
        DiffInfographicTableDensity density = DiffInfographicTableDensity.Standard)
    {
        var font = TableFont(density);
        var headerFont = TableHeaderFont(density);
        var valueRight = right - TableRightInset;
        var pathX = left + TablePathInset;
        var valueWidth = Math.Max(
            TextWidth(SizeChangeHeader, headerFont),
            rows.Count == 0 ? 0 : rows.Max(row => TextWidth(row.Value, font)));
        var valueLeft = valueRight - valueWidth;
        return new(left + TableMarkerInset, pathX, Math.Max(0, valueLeft - TableColumnGap - pathX), valueLeft, valueRight);
    }

    private static float DrawTable(
        IImageProcessingContext c,
        DiffInfographicSection section,
        DiffInfographicTable table,
        float top)
    {
        var font = TableFont(table.Density);
        var headerFont = TableHeaderFont(table.Density);
        var columns = MeasureTableColumns(table.Rows, section.Left, section.Right, table.Density);
        c.DrawText("PATH", headerFont, Muted, new PointF(columns.PathX, top));
        c.DrawText(SizeChangeHeader, headerFont, Muted, new PointF(columns.ValueLeft, top));
        top += DiffInfographicLayout.TableHeaderHeight;
        var rowHeight = DiffInfographicLayout.RowHeight(table.Density);
        foreach (var row in table.Rows)
        {
            var color = PointColor(row.Kind);
            c.DrawText(row.Marker, font, color, new PointF(columns.MarkerX, top));
            if (columns.PathWidth >= 1)
                c.DrawText(ElideTablePath(row.Path, font, columns.PathWidth), font, Text, new PointF(columns.PathX, top));
            c.DrawText(row.Value, font, Faint(color), new PointF(columns.ValueRight - TextWidth(row.Value, font), top));
            top += rowHeight;
        }
        return top + DiffInfographicLayout.TableBottomGap;
    }

    /// Elides folders first, then the middle of the file name, so a row always keeps a recognizable
    /// name; truncating a long path from the front alone would leave only shared parent folders.
    internal static string ElideTablePath(string path, Font font, float maxWidth)
    {
        if (TextWidth(path, font) <= maxWidth) return path;
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var name = parts.Length == 0 ? path : parts[^1];
        if (parts.Length > 2)
        {
            var elided = $"{parts[0]}/.../{name}";
            if (TextWidth(elided, font) <= maxWidth) return elided;
        }
        if (parts.Length > 1 && TextWidth($".../{name}", font) <= maxWidth) return $".../{name}";
        if (TextWidth(name, font) <= maxWidth) return name;
        return ElideMiddle(name, font, maxWidth);
    }

    private static string ElideMiddle(string text, Font font, float maxWidth)
    {
        var runes = text.EnumerateRunes().ToArray();
        var low = 0;
        var high = runes.Length / 2;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (TextWidth(Compose(middle), font) <= maxWidth) low = middle;
            else high = middle - 1;
        }
        return low == 0 ? DiffInfographicText.Fit(text, font, maxWidth) : Compose(low);

        string Compose(int keep) => string.Concat(runes.Take(keep)) + "…" + string.Concat(runes.TakeLast(keep));
    }

    private static float TextWidth(string text, Font font) => TextMeasurer.MeasureAdvance(text, new TextOptions(font)).Width;

    private static Color Faint(Color color)
    {
        var pixel = color.ToPixel<Rgba32>();
        return Color.FromRgba(pixel.R, pixel.G, pixel.B, 190);
    }

    private static void DrawFooter(IImageProcessingContext c, DiffInfographicScene scene)
    {
        c.DrawText("gbx-size-tree  ·  exact counts, bounded visual detail", DiffInfographicFonts.Regular(15), Muted, new PointF(70, scene.Height - 26));
        c.DrawText("Positions in metres", DiffInfographicFonts.Regular(15), Muted, new PointF(1190, scene.Height - 26));
    }

    private static IPath Rounded(float x, float y, float width, float height, float radius)
    {
        radius = Math.Min(radius, Math.Min(width, height) / 2);
        return new Polygon(new LinearLineSegment(
            new PointF(x + radius, y), new PointF(x + width - radius, y),
            new PointF(x + width, y + radius), new PointF(x + width, y + height - radius),
            new PointF(x + width - radius, y + height), new PointF(x + radius, y + height),
            new PointF(x, y + height - radius), new PointF(x, y + radius)));
    }

    private static Color PointColor(DiffInfographicChangeKind kind) => kind switch
    {
        DiffInfographicChangeKind.Added => Added,
        DiffInfographicChangeKind.Removed => Removed,
        _ => Changed,
    };
}
