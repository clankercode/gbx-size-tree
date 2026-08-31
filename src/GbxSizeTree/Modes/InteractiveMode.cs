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

/// <summary>
/// Menu-driven session: full diagnostic tree first, then pick actions (with estimated
/// savings), apply + re-measure in memory, undo/reset freely, save when happy.
/// </summary>
public static class InteractiveMode
{
    public static int Run(string inputPath, CliOptions options, ActionRegistry registry)
    {
        var console = ReportMode.BuildInteractiveConsole(options);
        if (!console.Profile.Capabilities.Interactive)
        {
            Console.Error.WriteLine("note: no interactive terminal detected — showing the report instead.");
            return ReportMode.Run(inputPath, options, registry);
        }

        var sink = new ConsoleStatusSink(toStderr: false);
        var analyzer = MapAnalyzer.CreateDefault(sink);
        GbxSizeTree.Measure.ResaveTrial? trial = null;
        var session = MapSession.Open(inputPath, registry, analyzer, sink,
            bytes => trial = GbxSizeTree.Measure.ResaveTrial.Start(bytes));
        var settings = ActionSelection.BuildSettings(options);

        ReportRenderer.Render(console, session.Baseline, options.TopN);
        var engine = new RecommendationEngine(registry, sink);
        RecommendationRenderer.Render(
            console, engine.Recommend(session.Baseline, settings, trial, session.DetectMap));

        while (true)
        {
            if (session.Applied.Count > 0)
            {
                console.MarkupLineInterpolated(
                    $"[dim]applied so far: {string.Join(" → ", session.Applied.Select(a => a.ActionId))}[/]");
            }

            var choice = console.Prompt(new SelectionPrompt<string>()
                .Title("[bold]What next?[/]")
                .AddChoices(BuildMenu(session)));

            switch (choice.Split(' ')[0])
            {
                case "apply":
                    PromptAndApply(console, session, registry, settings, trial);
                    break;
                case "undo":
                    var undone = session.Undo();
                    console.MarkupLineInterpolated($"[yellow]undid[/] {undone ?? "(nothing)"}");
                    ShowCurrent(console, session);
                    break;
                case "reset":
                    session.Reset();
                    console.MarkupLine("[yellow]reset to original[/]");
                    break;
                case "show":
                    ShowCurrent(console, session, renderTree: true);
                    break;
                case "save":
                    Save(console, session, options);
                    break;
                case "quit":
                    return ExitCodes.Ok;
            }
        }
    }

    private static List<string> BuildMenu(MapSession session)
    {
        var applied = session.Applied.Count;
        var menu = new List<string> { "apply actions" };
        if (applied > 0)
        {
            menu.Add($"undo last ({session.Applied[^1].ActionId})");
            menu.Add("reset to original");
            menu.Add("show updated report");
            menu.Add("save optimized map");
        }
        menu.Add("quit without saving");
        return menu;
    }

    private static void PromptAndApply(IAnsiConsole console, MapSession session,
        ActionRegistry registry, IReadOnlyDictionary<string, string> settings,
        GbxSizeTree.Measure.ResaveTrial? trial)
    {
        var sink = NullStatusSink.Instance;
        var appliedIds = session.Applied.Select(a => a.ActionId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<(IMapAction Action, ActionApplicability App)>();
        foreach (var action in registry.Applyable(settings.TryGetValue("experimental", out var e) && e == "true"))
        {
            if (appliedIds.Contains(action.Id))
            {
                continue;
            }
            var app = action.Detect(
                new ActionDetectContext(session.Baseline, settings, sink, session.DetectMap, trial));
            if (app.Applies)
            {
                candidates.Add((action, app));
            }
        }

        if (candidates.Count == 0)
        {
            console.MarkupLine("[grey]no further applicable actions[/]");
            return;
        }

        var prompt = new MultiSelectionPrompt<string>()
            .Title("Pick actions to apply (space = toggle, enter = confirm)")
            .NotRequired()
            .PageSize(12);
        var byLabel = new Dictionary<string, IMapAction>();
        foreach (var (action, app) in candidates.OrderByDescending(c => c.App.EstimatedSavingsBytes))
        {
            var tier = action.Tier == ActionTier.Lossless ? "[green]lossless[/]" : "[yellow]lossy[/]";
            var est = app.EstimatedSavingsBytes > 0
                ? $"~{SizeFormat.ShortBytes(app.EstimatedSavingsBytes)}"
                : app.Kind == EstimateKind.MeasuredOnSave ? "measured at save" : app.Kind.ToString();
            var label = $"{action.Id} — {est} {tier}" +
                (action.Consequence.Length > 0 ? $" [grey]({action.Consequence})[/]" : "");
            byLabel[label] = action;
            prompt.AddChoice(label);
        }

        var selected = console.Prompt(prompt);
        if (selected.Count == 0)
        {
            return;
        }
        foreach (var label in selected)
        {
            session.Apply(byLabel[label].Id, settings);
        }
        ShowCurrent(console, session);
    }

    private static void ShowCurrent(IAnsiConsole console, MapSession session, bool renderTree = false)
    {
        if (session.Applied.Count == 0)
        {
            console.MarkupLine("[grey]no actions applied — at original state[/]");
            return;
        }
        MaterializedMap materialized = default!;
        console.Status().Start("materializing + re-measuring…", _ => materialized = session.Materialize());
        if (!materialized.Validation.Ok)
        {
            console.MarkupLine("[red]validation FAILED — do not save this state:[/]");
            foreach (var issue in materialized.Validation.Issues)
            {
                console.MarkupLineInterpolated($"  [red]{issue.Rule}[/]: {issue.Message}");
            }
        }
        if (renderTree)
        {
            ReportRenderer.Render(console, materialized.Analysis, 20);
        }
        BatchMode.RenderDelta(console, session, materialized, savedPath: null);
    }

    private static void Save(IAnsiConsole console, MapSession session, CliOptions options)
    {
        var defaultPath = OutputPathResolver.Resolve(session.SourcePath, options.OutputPath);
        var path = console.Prompt(new TextPrompt<string>("Save as:").DefaultValue(defaultPath));
        try
        {
            var written = session.SaveAs(path, options.Force);
            console.MarkupLineInterpolated($"[green]wrote[/] {written}");
        }
        catch (GbxSizeTreeOutputException ex) when (ex.Kind == OutputFailureKind.AlreadyExists)
        {
            if (console.Confirm("Output exists — overwrite?", defaultValue: false))
            {
                var written = session.SaveAs(path, force: true);
                console.MarkupLineInterpolated($"[green]wrote[/] {written}");
            }
        }
        catch (GbxSizeTreeOutputException ex)
        {
            console.MarkupLineInterpolated($"[red]error:[/] {ex.Message}");
        }
    }
}
