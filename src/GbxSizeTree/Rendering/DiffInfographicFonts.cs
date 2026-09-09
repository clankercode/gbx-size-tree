using System.Reflection;
using SixLabors.Fonts;

namespace GbxSizeTree.Cli.Rendering;

internal static class DiffInfographicFonts
{
    private const string RegularResource = "GbxSizeTree.Cli.Resources.Fonts.AtkinsonHyperlegibleNext-Regular.ttf";
    private const string BoldResource = "GbxSizeTree.Cli.Resources.Fonts.AtkinsonHyperlegibleNext-Bold.ttf";
    private static readonly Lazy<FontFamily> RegularFamily = new(() => Load(RegularResource));
    private static readonly Lazy<FontFamily> BoldFamily = new(() => Load(BoldResource));

    public static Font Regular(float size) => RegularFamily.Value.CreateFont(size, FontStyle.Regular);
    public static Font Bold(float size) => BoldFamily.Value.CreateFont(size, FontStyle.Regular);

    private static FontFamily Load(string resource)
    {
        var assembly = typeof(DiffInfographicFonts).Assembly;
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Missing embedded infographic font resource: {resource}");
        var collection = new FontCollection();
        return collection.Add(stream);
    }
}
