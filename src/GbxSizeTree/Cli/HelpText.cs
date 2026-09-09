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
        Usage: gbx-size-tree diff [--json] [--all] old.Map.Gbx new.Map.Gbx

        Analysis:
          diff                      Compare two map versions
          --all                     Include all changed header/body chunks
          --header-only             Read and report only the map header
          --estimate-compressed     Estimate compressed sizes for tree entries
          --all-chunks              List every header and body chunk (no size cutoff)
          --unknown-chunks          Debug: print only chunks the catalog does not
                                    recognize, then exit (works with --json)
          --top N                   Show the top N entries (default: 20)
          --json                    Write the analysis as JSON
          --html                    Write the analysis as HTML
          --markdown, --md          Write the analysis as Markdown
          --color                   Always use color
          --no-color                Never use color
          -i, --interactive         Explicitly open the interactive interface
          -n, --non-interactive     Show the report instead of the automatic TUI

        Optimization:
          -O, --optimize            Optimize the map
          -o, --output PATH         Write the optimized map to PATH
          --force                   Overwrite an existing output file
          --strip-lightmap          Remove the baked lightmap
          --lighten-shadows MIN     Floor shadow-brightness bytes at 0-255 (DD2: 100)
          --thumbnail MODE          keep, strip, lossless, recompress:1-100, or
                                    downscale:32-1024
          --embed-stored            Store embedded ZIP entries (experimental)
          --action LIST             Enable comma-separated actions; may be repeated
          --no-action LIST          Disable comma-separated actions; may be repeated
          --dry-run                 Plan optimization without writing output
          --attribute               Measure each applied action's marginal savings
                                    (replays the pipeline once per action)
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
          gbx-size-tree -n MyMap.Map.Gbx
          gbx-size-tree --top 50 --estimate-compressed MyMap.Map.Gbx
          gbx-size-tree -O --strip-lightmap -o Smaller.Map.Gbx MyMap.Map.Gbx
          gbx-size-tree --lighten-shadows 100 -o Lighter.Map.Gbx MyMap.Map.Gbx
          gbx-size-tree --action embed-zip,thumbnail --thumbnail recompress:80 MyMap.Map.Gbx

        A map opened in an interactive terminal starts the TUI. Redirected output and
        -n/--non-interactive use report mode. Double-click launches open a file picker.
        """;

    /// <summary>Returns focused help for comparing two map versions.</summary>
    public static string Diff(string version) => $"""
        gbx-size-tree {version} — compare two map versions

        Usage:
          gbx-size-tree diff [OPTIONS] OLD_MAP NEW_MAP

        File order:
          OLD_MAP                  The earlier or baseline map (left side)
          NEW_MAP                  The later or candidate map (right side)

        The diff reports added, removed, and changed blocks, baked blocks, anchored items,
        embedded files, map metadata, and file size. Use --all to include every detected
        header and body chunk, including lightmaps, genealogies, password chunks, and
        other chunks without a dedicated semantic comparison.

        The human-readable report shows embedded files first, then placed items, followed by
        blocks and metadata. Use --html or --markdown/--md for portable reports.

        Options:
          --all                    Include all header/body chunk changes
          --html                   Write an HTML report
          --markdown, --md         Write a Markdown report
          --json                   Write machine-readable JSON to stdout
          --help                   Show this help

        Examples:
          gbx-size-tree diff OLD_MAP.Map.Gbx NEW_MAP.Map.Gbx
          gbx-size-tree diff --all OLD_MAP.Map.Gbx NEW_MAP.Map.Gbx
          gbx-size-tree diff --json OLD_MAP.Map.Gbx NEW_MAP.Map.Gbx
        """;

    /// </summary>
    public static string About(string version) =>
        $"gbx-size-tree {version} - Colorful size breakdown + optimizer for TM2020 .Map.Gbx "
        + "files. GPL-3; uses GBX.NET.";
}
