using GbxSizeTree.Abstractions;
using GbxSizeTree.Actions;
using GbxSizeTree.Cli.Rendering;
using GbxSizeTree.Model;
using GbxSizeTree.Tests.Fixtures;
using Spectre.Console.Testing;

namespace GbxSizeTree.Tests.Rendering;

public sealed class ReportRendererTests
{
    [Fact]
    public void FullReport_RendersFixtureDiagnostics()
    {
        var console = new TestConsole().Width(120);

        ReportRenderer.Render(console, FakeAnalysis.Build(), topN: 10);

        Assert.Contains("Unattributed residual", console.Output);
        Assert.Contains("Fake Map.Map.Gbx", console.Output);
        Assert.Contains("on disk", console.Output);
        Assert.Contains("on disk 11.7 KiB", console.Output);
        Assert.Contains("on disk ≈ 1.8 KiB", console.Output);
        Assert.Contains("Blocks", console.Output);
    }

    [Fact]
    public void Recommendations_RenderUnreachableVerdict()
    {
        var console = new TestConsole().Width(120);
        var report = new RecommendationReport([], AlreadyUnderLimit: false, RecommendationsToGetUnderLimit: -1);

        RecommendationRenderer.Render(console, report);

        Assert.Contains("cannot get this map under", console.Output);
        Assert.DoesNotContain("first -1", console.Output);
    }

    [Theory]
    [InlineData(8_000_000, "OVER")]
    [InlineData(12_000, "under")]
    public void FullReport_RendersOnlineLimitStatus(long fileBytes, string expected)
    {
        var console = new TestConsole().Width(120);
        var analysis = FakeAnalysis.Build() with { FileBytes = fileBytes };

        ReportRenderer.Render(console, analysis, topN: 10);

        Assert.Contains(expected, console.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Recommendations_RenderRowsEditorBadgeAndVerdict()
    {
        var console = new TestConsole().Width(120);
        var report = new RecommendationReport(
            Ranked:
            [
                new Recommendation("embed-zip", "Recompress embeds", ActionTier.Lossless, 2_000,
                    EstimateKind.Computed, "None", "Run optimize"),
                new Recommendation(null, "Remove editor-only shadows", ActionTier.EditorOnly, 1_000,
                    EstimateKind.Heuristic, "Rebake shadows", "Use the editor"),
            ],
            AlreadyUnderLimit: false,
            RecommendationsToGetUnderLimit: 2);

        RecommendationRenderer.Render(console, report);

        Assert.Contains("Recompress embeds", console.Output);
        Assert.Contains("Remove editor-only shadows", console.Output);
        Assert.Contains("editor", console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Verdict", console.Output);
        Assert.Contains("first 2", console.Output);
    }

    [Fact]
    public void Bytes_IncludesInvariantExactByteCount()
    {
        Assert.Contains("7,832,571", SizeFormat.Bytes(7_832_571));
    }
}
