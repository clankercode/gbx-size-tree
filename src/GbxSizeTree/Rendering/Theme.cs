using GbxSizeTree.Actions;
using GbxSizeTree.Model;
using Spectre.Console;

namespace GbxSizeTree.Cli.Rendering;

/// <summary>
/// Stable terminal colors and glyphs for the GBX size facts documented in docs/FORMAT-NOTES.md.
/// </summary>
public static class Theme
{
    public const long OnlineLimitBytes = 7168L * 1024;

    public static Color CategoryColor(SizeCategory category) => category switch
    {
        SizeCategory.Header => Color.Grey,
        SizeCategory.Thumbnail => Color.Yellow,
        SizeCategory.Metadata => Color.Teal,
        SizeCategory.Blocks => Color.Blue,
        SizeCategory.Items => Color.DeepSkyBlue1,
        SizeCategory.BakedBlocks => Color.CornflowerBlue,
        SizeCategory.Lightmap => Color.MediumPurple,
        SizeCategory.EmbeddedItems => Color.DarkOrange,
        SizeCategory.ScriptMetadata => Color.Aqua,
        SizeCategory.MediaTracker => Color.SpringGreen2,
        SizeCategory.PerElementArrays => Color.SlateBlue1,
        SizeCategory.FreeBlocks => Color.CadetBlue,
        SizeCategory.Other => Color.Grey50,
        SizeCategory.Residual => Color.Red,
        _ => Color.Default,
    };

    public static string ConfidenceGlyph(SizeConfidence confidence) => confidence switch
    {
        SizeConfidence.ExactOnDisk => "●",
        SizeConfidence.WriterDelta => "◐",
        SizeConfidence.Estimated => "○",
        _ => "?",
    };

    public static string TierBadge(ActionTier tier) => tier switch
    {
        ActionTier.Lossless => "[green]T1 lossless[/]",
        ActionTier.BenignLossy => "[yellow]T2 lossy-benign[/]",
        ActionTier.Experimental => "[red]T3 experimental[/]",
        ActionTier.EditorOnly => "[cyan]editor[/]",
        _ => "[grey]unknown[/]",
    };
}
