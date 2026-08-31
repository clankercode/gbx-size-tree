using GbxSizeTree.Abstractions;
using Spectre.Console;

namespace GbxSizeTree.Cli.Rendering;

/// <summary>
/// Renders ranked optimization advice against the online limit documented in docs/FORMAT-NOTES.md.
/// </summary>
public static class RecommendationRenderer
{
    public static void Render(IAnsiConsole console, RecommendationReport report)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(report);

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title("Recommendations")
            .AddColumn(new TableColumn("Rank").RightAligned())
            .AddColumn("Tier")
            .AddColumn("Title")
            .AddColumn(new TableColumn("Est. saving").RightAligned())
            .AddColumn("Consequence")
            .AddColumn("How");

        var needed = report.RecommendationsToGetUnderLimit;
        for (var index = 0; index < report.Ranked.Count; index++)
        {
            var recommendation = report.Ranked[index];
            // Rows past the under-limit cutoff are options, not advice: render them dim so
            // nobody re-bakes shadows a lossless pass would have made unnecessary.
            var dim = !report.AlreadyUnderLimit && needed > 0 && index >= needed;
            string Cell(string text) => dim ? $"[dim]{text}[/]" : text;
            table.AddRow(
                Cell((index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Theme.TierBadge(recommendation.Tier),
                Cell(Markup.Escape(recommendation.Title)),
                Cell(SizeFormat.ShortBytes(recommendation.EstimatedSavingsBytes)),
                Cell(Markup.Escape(recommendation.Consequence)),
                Cell(Markup.Escape(recommendation.HowTo)));
        }

        console.Write(table);
        if (report.AlreadyUnderLimit)
        {
            console.MarkupLine("[green]Verdict: already under the online limit.[/]");
        }
        else if (needed < 0)
        {
            console.MarkupLine("[red]Verdict: the available recommendations cannot get this map under the online limit.[/]");
        }
        else
        {
            var neededText = needed.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var losslessSuffices = report.Ranked.Take(needed).All(recommendation =>
                recommendation.ActionId is not null
                && recommendation.Tier == Actions.ActionTier.Lossless);
            console.MarkupLine(losslessSuffices
                ? $"[green]Verdict: the first {neededText} recommendation(s) — all lossless, applied by --optimize — get this map under the online limit.[/]"
                : $"[yellow]Verdict: apply the first {neededText} recommendation(s) to get under the online limit.[/]");
            if (needed < report.Ranked.Count)
            {
                console.MarkupLine("[dim]Dimmed rows are further options, not needed for the limit.[/]");
            }
        }

        foreach (var caution in report.Cautions ?? [])
        {
            console.MarkupLineInterpolated(
                $"[yellow]⚠ not recommended:[/] {caution.Title} would save {SizeFormat.ShortBytes(caution.EstimatedSavingsBytes)} ({caution.HowTo}), but: {caution.Consequence}");
        }
    }
}
