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
        Usage: gbx-size-tree diff [OPTIONS] OLD_MAP NEW_MAP

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
          OLD_MAP                  Earlier or baseline map; shown on the left
          NEW_MAP                  Later or candidate map; shown on the right

        Default comparison:
          File size; embedded ZIP entries; placed items; ordinary blocks; and map metadata.
          Embedded content is fingerprinted with SHA-256. ZIP/raw entry sizes and bounded,
          non-additive outer-body marginal measurements are also shown when available.
          Baked blocks and serialized content fingerprints are included only with --all.

        Coverage:
          Placement and metadata rows compare fields understood by GBX.NET; this is not a
          claim that every item property has deep semantic support. With --all, header/body
          chunk candidates plus complete container-prefix, stored-body (when present), and
          decompressed-body fingerprints can detect opaque changes. These fingerprints
          identify changed bytes, not their meaning. Warnings call out opaque or otherwise
          unavailable metadata. Marginal bytes recompress each original body after removing
          one changed embed; they are context-dependent, do not add up to map size, use a
          bounded trial budget (currently 256 trials per map), and may be unavailable.

        Output:
          Console color is automatic: disabled for redirects, NO_COLOR, and TERM=dumb.
          --color and --no-color override that policy. HTML is styled by default; use
          --not-styled for markup without CSS or inline styles. HTML, Markdown, JSON, PNG,
          and WebP are mutually exclusive formats. PNG and lossless WebP are purpose-built
          diff infographics, not screenshots of HTML; --all controls their coverage too.
          Text reports go to stdout. Image bytes go to stdout only when it is redirected;
          at a terminal, use -o/--output. Image files use a same-directory temporary file
          and atomic rename. Output refuses either input and existing files unless --force,
          and does not create parent directories. Its extension never selects or changes the
          format. Successful image file output leaves stdout empty; errors use stderr.
          Terminal progress shows actual stages and elapsed time; ETA stays unknown until
          measurable trials. It is cleared before final output and omitted when stderr is
          redirected. Nothing is published or uploaded.

        Image restrictions:
          Image output cannot be combined with --pause, -i/--interactive,
          --color/--no-color, or --styled/--not-styled. --no-pause and
          -n/--non-interactive are allowed. diff --help needs no map paths.

        Options:
          --all                    Add baked blocks and serialized content fingerprints
          --color                  Always use color
          --no-color               Never use color
          --html                   Write an HTML report (styled by default)
          --styled                 Include the default HTML stylesheet (with --html)
          --not-styled             Omit CSS and inline styles (with --html)
          --markdown, --md         Write a Markdown report
          --json                   Write machine-readable JSON
          --png                    Write a purpose-built PNG diff infographic
          --webp                   Write a purpose-built lossless WebP diff infographic
          -o, --output PATH        Atomically write image output to PATH
          --force                  Replace an existing image output file
          -h, --help               Show this help

        Examples:
          gbx-size-tree diff 'Sweet 2 burger v205.Map.Gbx' 'Sweet 2 burger v206.Map.Gbx'
          gbx-size-tree diff --all 'Sweet 2 burger v205.Map.Gbx' 'Sweet 2 burger v206.Map.Gbx'
          gbx-size-tree diff --html --not-styled Before.Map.Gbx After.Map.Gbx > diff.html
          gbx-size-tree diff --md Before.Map.Gbx After.Map.Gbx > diff.md
          gbx-size-tree diff --json Before.Map.Gbx After.Map.Gbx > diff.json
          gbx-size-tree diff --png --all -o diff.png Before.Map.Gbx After.Map.Gbx
          gbx-size-tree diff --webp Before.Map.Gbx After.Map.Gbx > diff.webp
        """;

    /// </summary>
    public static string About(string version) =>
        $"gbx-size-tree {version} - Colorful size breakdown + optimizer for TM2020 .Map.Gbx "
        + "files. GPL-3; uses GBX.NET.";
}
