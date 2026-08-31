using GbxSizeTree.Actions;

namespace GbxSizeTree.Cli.Modes;

/// <summary>Shared translation from CLI flags to action ids + settings.</summary>
public static class ActionSelection
{
    public static IReadOnlyDictionary<string, string> BuildSettings(CliOptions options) =>
        new Dictionary<string, string>
        {
            ["stored"] = options.EmbedStored ? "true" : "false",
            ["mode"] = options.ThumbnailMode,
            ["experimental"] = options.Experimental ? "true" : "false",
        };

    /// <summary>Requested action ids in request order (distinct); empty = nothing requested.</summary>
    public static IReadOnlyList<string> RequestedIds(CliOptions options, ActionRegistry registry)
    {
        var ids = new List<string>();
        if (options.Optimize)
        {
            ids.AddRange(registry.Defaults().Select(a => a.Id));
        }
        if (options.StripLightmap)
        {
            ids.Add("strip-lightmap");
        }
        if (!string.Equals(options.ThumbnailMode, "keep", StringComparison.OrdinalIgnoreCase))
        {
            ids.Add("thumbnail");
        }
        ids.AddRange(options.Actions);
        return ids
            .Where(id => !options.NoActions.Contains(id, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool WantsOptimization(CliOptions options) =>
        options.Optimize || options.StripLightmap || options.DryRun
        || options.Actions.Count > 0
        || !string.Equals(options.ThumbnailMode, "keep", StringComparison.OrdinalIgnoreCase);
}
