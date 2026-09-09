using System.Globalization;

namespace GbxSizeTree.Cli;

/// <summary>
/// Parses the command-line contract for analysing and optimizing TM2020 <c>.Map.Gbx</c> files.
/// </summary>
public static class ArgParser
{
    /// <summary>
    /// Parses <paramref name="args"/> without performing file-system or map validation.
    /// </summary>
    public static (CliOptions? options, string? error) Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? inputPath = null;
        var inputPaths = new List<string>();
        var diff = false;
        var compareAll = false;
        var headerOnly = false;
        var estimateCompressed = false;
        var allChunks = false;
        var unknownChunks = false;
        var topN = 20;
        var json = false;
        var html = false;
        var markdown = false;
        bool? color = null;
        var interactive = false;
        var nonInteractive = false;
        var optimize = false;
        string? outputPath = null;
        var force = false;
        var stripLightmap = false;
        byte? shadowBrightnessFloor = null;
        var thumbnailMode = "keep";
        var embedStored = false;
        var actions = new List<string>();
        var noActions = new List<string>();
        var dryRun = false;
        var attribute = false;
        var experimental = false;
        var verbose = false;
        var quiet = false;
        var showHelp = false;
        var showVersion = false;
        bool? pause = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--")
            {
                for (i++; i < args.Length; i++)
                {
                    if (!TryAddInput(args[i], inputPaths, ref inputPath, diff, out var inputError))
                    {
                        return (null, inputError);
                    }
                }

                break;
            }

            switch (arg)
            {
                case "diff":
                    diff = true;
                    break;
                case "--all":
                    compareAll = true;
                    break;
                case "--header-only":
                    headerOnly = true;
                    break;
                case "--estimate-compressed":
                    estimateCompressed = true;
                    break;
                case "--all-chunks":
                    allChunks = true;
                    break;
                case "--unknown-chunks":
                    unknownChunks = true;
                    break;
                case "--top":
                    if (!TryTakeValue(args, ref i, arg, out var topValue, out var topError))
                    {
                        return (null, topError);
                    }

                    if (!int.TryParse(topValue, NumberStyles.Integer, CultureInfo.InvariantCulture,
                            out topN) || topN <= 0)
                    {
                        return (null, "--top requires a positive integer.");
                    }

                    break;
                case "--json":
                    json = true;
                    break;
                case "--html":
                    html = true;
                    break;
                case "--markdown":
                case "--md":
                    markdown = true;
                    break;
                case "--color":
                    color = true;
                    break;
                case "--no-color":
                    color = false;
                    break;
                case "-i":
                case "--interactive":
                    interactive = true;
                    break;
                case "-n":
                case "--non-interactive":
                    nonInteractive = true;
                    break;
                case "-O":
                case "--optimize":
                    optimize = true;
                    break;
                case "-o":
                case "--output":
                    if (!TryTakeValue(args, ref i, arg, out outputPath, out var outputError))
                    {
                        return (null, outputError);
                    }

                    break;
                case "--force":
                    force = true;
                    break;
                case "--strip-lightmap":
                    stripLightmap = true;
                    break;
                case "--lighten-shadows":
                    if (!TryTakeValue(args, ref i, arg, out var floorValue, out var floorError))
                    {
                        return (null, floorError);
                    }

                    if (!byte.TryParse(
                            floorValue,
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out var parsedFloor))
                    {
                        return (null, "--lighten-shadows requires a byte minimum from 0 to 255.");
                    }

                    shadowBrightnessFloor = parsedFloor;
                    break;
                case "--thumbnail":
                    if (!TryTakeValue(args, ref i, arg, out thumbnailMode, out var thumbnailError))
                    {
                        return (null, thumbnailError);
                    }

                    if (!IsValidThumbnailMode(thumbnailMode))
                    {
                        return (null, $"Invalid --thumbnail mode '{thumbnailMode}'.");
                    }

                    break;
                case "--embed-stored":
                    embedStored = true;
                    break;
                case "--action":
                    if (!TryTakeValue(args, ref i, arg, out var actionValue, out var actionError))
                    {
                        return (null, actionError);
                    }

                    AddList(actions, actionValue);
                    break;
                case "--no-action":
                    if (!TryTakeValue(args, ref i, arg, out var noActionValue, out var noActionError))
                    {
                        return (null, noActionError);
                    }

                    AddList(noActions, noActionValue);
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--attribute":
                    attribute = true;
                    break;
                case "--experimental":
                    experimental = true;
                    break;
                case "-v":
                case "--verbose":
                    verbose = true;
                    break;
                case "-q":
                case "--quiet":
                    quiet = true;
                    break;
                case "-h":
                case "--help":
                    showHelp = true;
                    break;
                case "--version":
                    showVersion = true;
                    break;
                case "--pause":
                    pause = true;
                    break;
                case "--no-pause":
                    pause = false;
                    break;
                default:
                    if (arg.StartsWith("-", StringComparison.Ordinal))
                    {
                        return (null, $"Unknown option '{arg}'.");
                    }

                    if (!TryAddInput(arg, inputPaths, ref inputPath, diff, out var inputError))
                    {
                        return (null, inputError);
                    }

                    break;
            }
        }

        if (diff && showHelp)
        {
            return (new CliOptions
            {
                Diff = true,
                CompareAll = compareAll,
                Json = json,
                ShowHelp = true,
            }, null);
        }

        if (diff && inputPaths.Count != 2)
        {
            return (null, "diff requires exactly two input map paths.");
        }

        if (json && interactive)
        {
            return (null, "--json cannot be used with -i/--interactive.");
        }

        var formatCount = (json ? 1 : 0) + (html ? 1 : 0) + (markdown ? 1 : 0);
        if (formatCount > 1)
        {
            return (null, "--json, --html, and --markdown/--md cannot be combined.");
        }

        if ((html || markdown) && interactive)
        {
            return (null, "--html and --markdown/--md cannot be used with -i/--interactive.");
        }

        var format = html ? CliOutputFormat.Html : markdown ? CliOutputFormat.Markdown : json ? CliOutputFormat.Json : CliOutputFormat.Console;

        if (interactive && nonInteractive)
        {
            return (null, "-i/--interactive cannot be used with --non-interactive.");
        }

        if (quiet && verbose)
        {
            return (null, "-q/--quiet cannot be used with -v/--verbose.");
        }

        var stripRequested = (stripLightmap ||
                actions.Contains("strip-lightmap", StringComparer.OrdinalIgnoreCase)) &&
            !noActions.Contains("strip-lightmap", StringComparer.OrdinalIgnoreCase);
        if (stripRequested && shadowBrightnessFloor is > 0)
        {
            return (null, "--lighten-shadows cannot be combined with --strip-lightmap.");
        }

        return (new CliOptions
        {
            InputPath = inputPath,
            InputPaths = inputPaths.ToArray(),
            Diff = diff,
            CompareAll = compareAll,
            HeaderOnly = headerOnly,
            EstimateCompressed = estimateCompressed,
            AllChunks = allChunks,
            UnknownChunks = unknownChunks,
            TopN = topN,
            Json = json,
            Format = format,
            Color = color,
            Interactive = interactive,
            NonInteractive = nonInteractive,
            Optimize = optimize,
            OutputPath = outputPath,
            Force = force,
            StripLightmap = stripLightmap,
            ShadowBrightnessFloor = shadowBrightnessFloor,
            ThumbnailMode = thumbnailMode,
            EmbedStored = embedStored,
            Actions = actions.ToArray(),
            NoActions = noActions.ToArray(),
            DryRun = dryRun,
            Attribute = attribute,
            Experimental = experimental,
            Verbose = verbose,
            Quiet = quiet,
            ShowHelp = showHelp,
            ShowVersion = showVersion,
            Pause = pause,
        }, null);
    }

    private static bool TryTakeValue(
        string[] args,
        ref int index,
        string option,
        out string value,
        out string? error)
    {
        if (index + 1 >= args.Length)
        {
            value = string.Empty;
            error = $"{option} requires a value.";
            return false;
        }

        value = args[++index];
        error = null;
        return true;
    }

    private static bool TryAddInput(string value, List<string> inputPaths, ref string? inputPath, bool diff, out string? error)
    {
        if (!diff && inputPath is not null)
        {
            error = $"Unexpected argument '{value}': only one input path is allowed.";
            return false;
        }
        if (diff && inputPaths.Count >= 2)
        {
            error = "diff requires exactly two input map paths.";
            return false;
        }
        inputPaths.Add(value);
        inputPath ??= value;
        error = null;
        return true;
    }

    private static bool TrySetInput(string value, ref string? inputPath, out string? error)
    {
        if (inputPath is not null)
        {
            error = $"Unexpected argument '{value}': only one input path is allowed.";
            return false;
        }

        inputPath = value;
        error = null;
        return true;
    }

    private static bool IsValidThumbnailMode(string mode)
    {
        if (mode is "keep" or "strip" or "lossless")
        {
            return true;
        }

        return TryGetBoundedSuffix(mode, "recompress:", 1, 100)
            || TryGetBoundedSuffix(mode, "downscale:", 32, 1024);
    }

    private static bool TryGetBoundedSuffix(string value, string prefix, int minimum, int maximum)
    {
        if (!value.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var suffix = value.AsSpan(prefix.Length);
        return int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && number >= minimum
            && number <= maximum;
    }

    private static void AddList(List<string> destination, string value) =>
        destination.AddRange(value.Split(',', StringSplitOptions.RemoveEmptyEntries
            | StringSplitOptions.TrimEntries));
}
