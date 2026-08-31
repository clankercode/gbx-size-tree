using GbxSizeTree.Abstractions;
using GbxSizeTree.Actions;
using GbxSizeTree.Analysis;
using GbxSizeTree.Cli.Output;
using GbxSizeTree.Cli.Rendering;
using GbxSizeTree.Model;
using GbxSizeTree.Recommendations;
using GbxSizeTree.Session;
using Spectre.Console;

namespace GbxSizeTree.Cli.Modes;

/// <summary>Flag-driven optimization: diagnostic tree first, then apply, validate, save, report delta.</summary>
public static class BatchMode
{
    public static int Run(string inputPath, CliOptions options, ActionRegistry registry)
    {
        var console = ReportMode.BuildConsole(options, options.Json ? Console.Error : null);
        IStatusSink sink = options.Quiet
            ? NullStatusSink.Instance
            : new ConsoleStatusSink(toStderr: options.Json);
        var analyzer = MapAnalyzer.CreateDefault(sink);
        var session = MapSession.Open(inputPath, registry, analyzer, sink);
        var settings = ActionSelection.BuildSettings(options);
        var recommendations = new RecommendationEngine(registry, sink)
            .Recommend(session.Baseline, settings);

        if (!options.Json)
        {
            ReportRenderer.Render(console, session.Baseline, options.TopN);
        }

        var requested = ActionSelection.RequestedIds(options, registry);
        if (requested.Count == 0 && options.DryRun && options.NoActions.Count == 0)
        {
            requested = registry.Defaults().Select(a => a.Id).ToList();
        }

        var detects = new List<(IMapAction Action, ActionApplicability Applicability)>();
        foreach (var id in requested)
        {
            var action = registry.Find(id);
            if (action is null)
            {
                WriteError(console, options, ExitCodes.Usage, $"unknown action id: {id}");
                return ExitCodes.Usage;
            }
            var applicability = action.Detect(
                new ActionDetectContext(session.Baseline, settings, sink, session.DetectMap));
            detects.Add((action, applicability));
        }

        foreach (var (action, applicability) in detects)
        {
            var estimate = applicability.EstimatedSavingsBytes > 0
                ? $"~{SizeFormat.Bytes(applicability.EstimatedSavingsBytes)}"
                : applicability.Kind.ToString();
            if (applicability.Applies)
            {
                console.MarkupLineInterpolated(
                    $"[green]•[/] {action.Id}: applies ({estimate}) — {applicability.Reason}");
            }
            else
            {
                console.MarkupLineInterpolated(
                    $"[grey]•[/] {action.Id}: skipped — {applicability.Reason}");
            }
        }

        if (options.DryRun)
        {
            if (options.Json)
            {
                JsonReportWriter.Write(Console.Out, session.Baseline, recommendations);
            }
            console.MarkupLine("[yellow]dry run:[/] nothing written");
            return ExitCodes.Ok;
        }

        foreach (var (action, applicability) in detects.Where(d => d.Applicability.Applies))
        {
            session.Apply(action.Id, settings);
        }

        MaterializedMap materialized;
        using (sink.Activity("materializing optimized map"))
        {
            materialized = session.Materialize();
        }

        if (!materialized.Validation.Ok)
        {
            if (options.Json)
            {
                WriteError(
                    console,
                    options,
                    ExitCodes.ValidationFailed,
                    "output validation failed: " + string.Join(
                        "; ", materialized.Validation.Issues.Select(issue => issue.Message)));
                return ExitCodes.ValidationFailed;
            }
            console.MarkupLine("[red]output validation FAILED:[/]");
            foreach (var issue in materialized.Validation.Issues)
            {
                console.MarkupLineInterpolated($"  [red]{issue.Rule}[/]: {issue.Message}");
            }
            return ExitCodes.ValidationFailed;
        }

        string outPath;
        try
        {
            outPath = session.SaveAs(options.OutputPath, options.Force);
        }
        catch (GbxSizeTreeOutputException ex)
        {
            WriteError(console, options, ExitCodes.IoError, ex.Message);
            return ExitCodes.IoError;
        }

        if (options.Json)
        {
            JsonReportWriter.Write(Console.Out, session.Baseline, recommendations);
        }
        RenderDelta(console, session, materialized, outPath);
        return ExitCodes.Ok;
    }

    private static void WriteError(IAnsiConsole console, CliOptions options, int code, string message)
    {
        if (options.Json)
        {
            JsonReportWriter.WriteError(Console.Out, code, message);
        }
        else
        {
            console.MarkupLineInterpolated($"[red]error:[/] {message}");
        }
    }

    internal static void RenderDelta(IAnsiConsole console, MapSession session,
        MaterializedMap materialized, string? savedPath)
    {
        var before = session.OriginalBytes.LongLength;
        var after = materialized.FileBytes;
        var saved = before - after;
        var pct = before > 0 ? (double)saved / before : 0;
        var limit = Limits.OnlineMapSizeBytes;

        console.WriteLine();
        console.MarkupLineInterpolated(
            $"[bold]{SizeFormat.Bytes(before)}[/] → [bold]{SizeFormat.Bytes(after)}[/]  (saved {SizeFormat.Bytes(saved)}, {pct:P1}) in {materialized.Elapsed.TotalSeconds:F1}s");
        console.MarkupLine(after <= limit
            ? $"[green]✓ under the {SizeFormat.Bytes(limit)} online limit[/]"
            : $"[red]still {SizeFormat.Bytes(after - limit)} over the online limit[/]");
        if (savedPath is not null)
        {
            console.MarkupLineInterpolated($"[green]wrote[/] {savedPath}");
        }
    }
}
