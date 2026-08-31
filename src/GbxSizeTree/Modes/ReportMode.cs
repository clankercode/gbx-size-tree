using GbxSizeTree.Abstractions;
using GbxSizeTree.Actions;
using GbxSizeTree.Analysis;
using GbxSizeTree.Cli.Output;
using GbxSizeTree.Cli.Rendering;
using GbxSizeTree.Recommendations;
using Spectre.Console;

namespace GbxSizeTree.Cli.Modes;

/// <summary>The default frontend: full diagnostic tree (always first), then recommendations.</summary>
public static class ReportMode
{
    public static int Run(string inputPath, CliOptions options, ActionRegistry registry)
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

        IStatusSink sink = options.Quiet
            ? NullStatusSink.Instance
            : new ConsoleStatusSink(toStderr: options.Json);
        var analyzer = MapAnalyzer.CreateDefault(sink);
        var analysis = analyzer.Analyze(
            new MapSource.FromFile(inputPath),
            new AnalyzeOptions(
                HeaderOnly: options.HeaderOnly,
                TrialCompressionEstimates: options.EstimateCompressed,
                TopN: options.TopN));
        var recommendations = new RecommendationEngine(registry, sink)
            .Recommend(analysis, ActionSelection.BuildSettings(options));

        if (options.Json)
        {
            JsonReportWriter.Write(Console.Out, analysis, recommendations);
            return ExitCodes.Ok;
        }

        var console = BuildConsole(options);
        ReportRenderer.Render(console, analysis, options.TopN);
        RecommendationRenderer.Render(console, recommendations);
        return ExitCodes.Ok;
    }

    public static IAnsiConsole BuildConsole(CliOptions options, TextWriter? output = null)
    {
        if (options.Color is null && output is null)
        {
            return AnsiConsole.Console;
        }

        return AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(output ?? Console.Out),
            Ansi = options.Color switch
            {
                false => AnsiSupport.No,
                true => AnsiSupport.Yes,
                null => AnsiSupport.Detect,
            },
            ColorSystem = options.Color == false
                ? ColorSystemSupport.NoColors
                : ColorSystemSupport.Detect,
        });
    }
}
