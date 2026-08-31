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
        GbxSizeTree.Measure.ResaveTrial? trial = null;
        var session = MapSession.Open(inputPath, registry, analyzer, sink,
            bytes => trial = GbxSizeTree.Measure.ResaveTrial.Start(bytes));
        var settings = ActionSelection.BuildSettings(options);
        var recommendations = new RecommendationEngine(registry, sink)
            .Recommend(session.Baseline, settings, trial);

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
                new ActionDetectContext(session.Baseline, settings, sink, session.DetectMap, trial));
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

        IReadOnlyList<AttributionRow>? attribution = null;
        if (options.Attribute)
        {
            attribution = ComputeAttribution(session, registry, sink);
        }

        var summary = new OptimizationSummary(
            AppliedActionIds: session.Applied.Select(a => a.ActionId).ToArray(),
            BeforeBytes: session.OriginalBytes.LongLength,
            AfterBytes: materialized.FileBytes,
            SavedBytes: session.OriginalBytes.LongLength - materialized.FileBytes,
            OutputPath: outPath,
            ElapsedSeconds: materialized.Elapsed.TotalSeconds,
            Attribution: attribution);

        if (options.Json)
        {
            JsonReportWriter.Write(Console.Out, session.Baseline, recommendations, summary);
        }
        RenderDelta(console, session, materialized, outPath);
        if (attribution is not null)
        {
            RenderAttribution(console, attribution);
        }
        return ExitCodes.Ok;
    }

    /// <summary>
    /// Replays cumulative prefixes of the applied set (pipeline order) to measure each
    /// action's marginal contribution. The empty prefix is the pure LZO1x_999 recompression
    /// baseline — every save recompresses, so that share exists even without `resave`.
    /// Leaves the session back at the full applied set.
    /// </summary>
    internal static IReadOnlyList<AttributionRow> ComputeAttribution(
        MapSession session, ActionRegistry registry, IStatusSink sink)
    {
        var requests = session.Applied.ToList();
        var ordered = requests
            .OrderBy(request => registry.Find(request.ActionId)?.Order ?? int.MaxValue)
            .Where(request => request.ActionId != "resave")
            .ToList();

        var rows = new List<AttributionRow>();
        var previous = session.OriginalBytes.LongLength;
        session.Reset();

        // An empty request set short-circuits to the original bytes (see MapSession), so the
        // baseline needs the resave action applied for a real re-serialization to happen.
        if (session.Apply("resave"))
        {
            using (sink.Activity("attributing: LZO1x_999 recompression baseline"))
            {
                var baseline = session.Materialize();
                rows.Add(new AttributionRow(
                    "recompression (LZO1x_999)", baseline.FileBytes, previous - baseline.FileBytes));
                previous = baseline.FileBytes;
            }
        }

        foreach (var request in ordered)
        {
            session.Apply(request.ActionId, request.Settings);
            using (sink.Activity($"attributing: +{request.ActionId}"))
            {
                var step = session.Materialize();
                rows.Add(new AttributionRow(request.ActionId, step.FileBytes, previous - step.FileBytes));
                previous = step.FileBytes;
            }
        }

        session.Reset();
        foreach (var request in requests)
        {
            session.Apply(request.ActionId, request.Settings);
        }

        return rows;
    }

    private static void RenderAttribution(IAnsiConsole console, IReadOnlyList<AttributionRow> rows)
    {
        console.WriteLine();
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title("Attribution (marginal savings, pipeline order)")
            .AddColumn("Step")
            .AddColumn(new TableColumn("File size").RightAligned())
            .AddColumn(new TableColumn("Saved").RightAligned());
        foreach (var row in rows)
        {
            table.AddRow(
                Markup.Escape(row.Label),
                SizeFormat.ShortBytes(row.FileBytes),
                SizeFormat.ShortBytes(row.SavedBytes));
        }
        console.Write(table);
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
