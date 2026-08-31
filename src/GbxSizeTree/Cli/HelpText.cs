namespace GbxSizeTree.Cli;

/// <summary>
/// Provides plain-text CLI help using the format facts recorded in <c>docs/FORMAT-NOTES.md</c>.
/// </summary>
public static class HelpText
{
    /// <summary>
    /// Returns the complete command-line help text.
    /// </summary>
    public static string Full(string version) => $"""
        gbx-size-tree {version}

        Colorful size breakdown + optimizer for TM2020 .Map.Gbx files. Licensed GPL-3 and
        powered by GBX.NET.

        Usage: gbx-size-tree [options] [file.Map.Gbx]

        Analysis:
          --header-only             Read and report only the map header
          --estimate-compressed     Estimate compressed sizes for tree entries
          --top N                   Show the top N entries (default: 20)
          --json                    Write the analysis as JSON
          --color                   Always use color
          --no-color                Never use color
          -i, --interactive         Open the interactive interface
          --non-interactive         Disable automatic interactive mode

        Optimization:
          -O, --optimize            Optimize the map
          -o, --output PATH         Write the optimized map to PATH
          --force                   Overwrite an existing output file
          --strip-lightmap          Remove the baked lightmap
          --thumbnail MODE          keep, strip, lossless, recompress:1-100, or
                                    downscale:32-1024
          --embed-stored            Store embedded ZIP entries (experimental)
          --action LIST             Enable comma-separated actions; may be repeated
          --no-action LIST          Disable comma-separated actions; may be repeated
          --dry-run                 Plan optimization without writing output
          --experimental            Enable experimental optimizations

        General:
          -v, --verbose             Show additional status information
          -q, --quiet               Suppress non-error status information
          -h, --help                Show this help
          --version                 Show the version
          --pause                   Pause before exiting
          --no-pause                Never pause before exiting
          --                        End option parsing

        Examples:
          gbx-size-tree MyMap.Map.Gbx
          gbx-size-tree --top 50 --estimate-compressed MyMap.Map.Gbx
          gbx-size-tree -O --strip-lightmap -o Smaller.Map.Gbx MyMap.Map.Gbx
          gbx-size-tree --action embed-zip,thumbnail --thumbnail recompress:80 MyMap.Map.Gbx

        Double-click launches open a file picker and starts interactive mode.
        """;

    /// <summary>
    /// Returns the short product and licensing description.
    /// </summary>
    public static string About(string version) =>
        $"gbx-size-tree {version} - Colorful size breakdown + optimizer for TM2020 .Map.Gbx "
        + "files. GPL-3; uses GBX.NET.";
}
