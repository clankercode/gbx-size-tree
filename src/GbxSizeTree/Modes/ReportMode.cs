using GbxSizeTree.Abstractions;
using GbxSizeTree.Analysis;
using GbxSizeTree.Cli.Output;
using GbxSizeTree.Cli.Rendering;
using Spectre.Console;

namespace GbxSizeTree.Cli.Modes;

/// <summary>The default frontend: full diagnostic tree (always first), then recommendations.</summary>
public static class ReportMode
{
    public static int Run(string inputPath, CliOptions options)
    {
        if (!File.Exists(inputPath))
        {
            if (options.Json)
            {
                JsonReportWriter.WriteError(Console.Out, ExitCodes.IoError, $"file not found: {inputPath}");
            }
            else
            {
                Console.Error.WriteLine($"error: file not found: {inputPath}");
            }
            return ExitCodes.IoError;
        }

        var sink = new ConsoleStatusSink(toStderr: options.Json || options.Quiet);
        var analyzer = MapAnalyzer.CreateDefault(sink);
        var analysis = analyzer.Analyze(
            new MapSource.FromFile(inputPath),
            new AnalyzeOptions(
                HeaderOnly: options.HeaderOnly,
                TrialCompressionEstimates: options.EstimateCompressed,
                TopN: options.TopN));

        if (options.Json)
        {
            JsonReportWriter.Write(Console.Out, analysis, null);
            return ExitCodes.Ok;
        }

        ReportRenderer.Render(BuildConsole(options), analysis, options.TopN);
        return ExitCodes.Ok;
    }

    public static IAnsiConsole BuildConsole(CliOptions options) => options.Color switch
    {
        false => AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
        }),
        true => AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.Detect,
        }),
        null => AnsiConsole.Console,
    };
}
