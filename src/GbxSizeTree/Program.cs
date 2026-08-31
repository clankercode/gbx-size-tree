using System.Reflection;
using GBX.NET;
using GbxSizeTree.Cli;
using GbxSizeTree.Cli.Modes;
using GbxSizeTree.Cli.Platform;

Gbx.LZO = new GBX.NET.LZO.Lzo();
Gbx.ZLib = new GBX.NET.ZLib.ZLib();

var version = Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";

var (options, parseError) = ArgParser.Parse(args);
var probe = LaunchModeDetector.ProbeCurrent();
var launchKind = LaunchModeDetector.Detect(probe);
var exitCode = ExitCodes.Ok;

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
        Console.Write(HelpText.Full(version));
    }
    else if (options.ShowVersion)
    {
        Console.WriteLine($"gbx-size-tree {version} (GPL-3.0-or-later; uses GBX.NET + GBX.NET.LZO)");
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
            if (options.Optimize || options.Interactive || options.StripLightmap
                || options.Actions.Count > 0 || options.DryRun)
            {
                Console.Error.WriteLine(
                    "note: optimization actions are not wired up in this build yet — showing the report only.");
            }
            exitCode = ReportMode.Run(input, options);
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
