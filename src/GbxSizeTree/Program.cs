using System.Reflection;
using GBX.NET;
using GbxSizeTree.Actions;
using GbxSizeTree.Actions.Passes;
using GbxSizeTree.Cli;
using GbxSizeTree.Cli.Modes;
using GbxSizeTree.Cli.Platform;

if (OperatingSystem.IsWindows() && !Console.IsOutputRedirected)
{
    try
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
    }
    catch
    {
        // Legacy console hosts may refuse; box-drawing degrades but nothing breaks.
    }
}

Gbx.LZO = new GBX.NET.LZO.Lzo();
Gbx.ZLib = new GBX.NET.ZLib.ZLib();

var version = Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";

var (options, parseError) = ArgParser.Parse(args);
var probe = LaunchModeDetector.ProbeCurrent();
var launchKind = LaunchModeDetector.Detect(probe);
var exitCode = ExitCodes.Ok;
var registry = new ActionRegistry([
    new OrphanEmbedsAction(),
    new EmbeddedZipAction(),
    new PruneChunksAction(),
    new LightenShadowsAction(),
    new StripLightmapAction(),
    new ThumbnailAction(new ImageSharpJpegRecoder()),
    new ResaveAction(),
]);

try
{
    if (parseError is not null || options is null)
    {
        Console.Error.WriteLine($"error: {parseError ?? "invalid arguments"}");
        Console.Error.WriteLine("run with --help for usage");
        exitCode = ExitCodes.Usage;
    }
    else if (options.ShowHelp)
    {
        Console.Write(options.Diff ? HelpText.Diff(version) : HelpText.Full(version));
    }
    else if (options.ShowVersion)
    {
        Console.WriteLine($"gbx-size-tree {version} (GPL-3.0-or-later; uses GBX.NET + GBX.NET.LZO)");
    }
    else
    {
        if (options.Diff)
        {
            exitCode = DiffMode.Run(options.InputPaths, options.Format, options.CompareAll, options.Color, options.Styled);
        }
        else
        {
        var input = options.InputPath;
        if (input is null && launchKind == LaunchKind.GuiOwnConsole)
        {
            IFilePicker picker = OperatingSystem.IsWindows()
                ? new WindowsFilePicker()
                : new ZenityFilePicker();
            input = picker.PickMapFile();
        }

        if (input is null)
        {
            Console.Write(HelpText.Full(version));
            exitCode = options.InputPath is null && launchKind == LaunchKind.Terminal
                ? ExitCodes.Usage
                : ExitCodes.Ok;
        }
        else
        {
            var wantsOptimization = ActionSelection.WantsOptimization(options);
            var reportOnly = options.HeaderOnly
                || options.EstimateCompressed
                || options.AllChunks
                || options.UnknownChunks
                || options.Format != CliOutputFormat.Console;
            var startInteractive = LaunchModeDetector.ShouldStartInteractive(
                probe,
                explicitlyRequested: options.Interactive,
                disabled: options.NonInteractive,
                json: options.Json,
                reportOnly: reportOnly,
                wantsOptimization: wantsOptimization);
            if (startInteractive)
            {
                exitCode = InteractiveMode.Run(input, options, registry);
            }
            else if (!options.Interactive && wantsOptimization)
            {
                exitCode = BatchMode.Run(input, options, registry);
            }
            else
            {
                exitCode = ReportMode.Run(input, options, registry);
            }
        }
    }
    }
}
catch (Exception ex)
{
    if (options?.Json == true)
    {
        GbxSizeTree.Cli.Output.JsonReportWriter.WriteError(Console.Out, ExitCodes.Internal, ex.Message);
    }
    Console.Error.WriteLine($"error: {ex.Message}");
    if (options?.Verbose == true)
    {
        Console.Error.WriteLine(ex.ToString());
    }
    exitCode = ex is FileNotFoundException or IOException ? ExitCodes.IoError : ExitCodes.Internal;
}
finally
{
    PauseOnExit.PauseIfNeeded(options?.Pause, launchKind);
}

return exitCode;
