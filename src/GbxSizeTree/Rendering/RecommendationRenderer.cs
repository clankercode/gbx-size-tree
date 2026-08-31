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

        for (var index = 0; index < report.Ranked.Count; index++)
        {
            var recommendation = report.Ranked[index];
            table.AddRow(
                (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Theme.TierBadge(recommendation.Tier),
                Markup.Escape(recommendation.Title),
                SizeFormat.ShortBytes(recommendation.EstimatedSavingsBytes),
                Markup.Escape(recommendation.Consequence),
                Markup.Escape(recommendation.HowTo));
        }

        console.Write(table);
        if (report.AlreadyUnderLimit)
        {
            console.MarkupLine("[green]Verdict: already under the online limit.[/]");
        }
        else if (report.RecommendationsToGetUnderLimit < 0)
        {
            console.MarkupLine("[red]Verdict: the available recommendations cannot get this map under the online limit.[/]");
        }
        else
        {
            console.MarkupLine(
                $"[yellow]Verdict: apply the first {report.RecommendationsToGetUnderLimit.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                "recommendation(s) to get under the online limit.[/]");
        }
    }
}
