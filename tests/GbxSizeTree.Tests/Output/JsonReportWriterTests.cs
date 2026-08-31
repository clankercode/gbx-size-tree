using System.Text.Json;
using GbxSizeTree.Abstractions;
using GbxSizeTree.Cli.Output;
using GbxSizeTree.Tests.Fixtures;

namespace GbxSizeTree.Tests.Output;

public sealed class JsonReportWriterTests
{
    [Fact]
    public void Write_ProducesVersionedSourceGeneratedAnalysis()
    {
        using var output = new StringWriter();

        JsonReportWriter.Write(output, FakeAnalysis.Build(), recommendations: null);

        using var document = JsonDocument.Parse(output.ToString());
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());

        var analysis = root.GetProperty("analysis");
        Assert.Equal(12_000, analysis.GetProperty("fileBytes").GetInt64());
        Assert.False(root.TryGetProperty("recommendations", out _));

        var json = output.ToString();
        Assert.Contains("body.residual", json, StringComparison.Ordinal);
        Assert.Contains("ExactOnDisk", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WithRecommendations_SerializesRecommendationsProperty()
    {
        using var output = new StringWriter();
        var recommendations = new RecommendationReport(
            Ranked: [],
            AlreadyUnderLimit: false,
            RecommendationsToGetUnderLimit: -1);

        JsonReportWriter.Write(output, FakeAnalysis.Build(), recommendations);

        using var document = JsonDocument.Parse(output.ToString());
        var report = document.RootElement.GetProperty("recommendations");
        Assert.False(report.GetProperty("alreadyUnderLimit").GetBoolean());
        Assert.Equal(-1, report.GetProperty("recommendationsToGetUnderLimit").GetInt32());
    }

    [Fact]
    public void WriteError_ProducesSourceGeneratedErrorEnvelope()
    {
        using var output = new StringWriter();

        JsonReportWriter.WriteError(output, 4, "cannot read map");

        using var document = JsonDocument.Parse(output.ToString());
        var error = document.RootElement.GetProperty("error");
        Assert.Equal(4, error.GetProperty("code").GetInt32());
        Assert.Equal("cannot read map", error.GetProperty("message").GetString());
    }

    [Fact]
    public void Write_MatchesGoldenFakeAnalysis()
    {
        var analysis = FakeAnalysis.Build() with
        {
            SourceLabel = Path.GetFullPath("Fake Map.Map.Gbx"),
        };
        using var output = new StringWriter();
        JsonReportWriter.Write(output, analysis, recommendations: null);

        var actual = JsonReportWriter.Normalize(output.ToString());

        // Regenerate with `just golden-update` (sets GBX_SIZE_TREE_UPDATE_GOLDEN=1).
        if (Environment.GetEnvironmentVariable("GBX_SIZE_TREE_UPDATE_GOLDEN") == "1")
        {
            File.WriteAllText(GoldenPath(), actual);
        }

        var expected = File.ReadAllText(GoldenPath()).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Normalize_NormalizesCrLf()
    {
        const string json = "{\r\n  \"SourceLabel\": \"relative.map.gbx\"\r\n}";

        var normalized = JsonReportWriter.Normalize(json);

        Assert.Equal("{\n  \"SourceLabel\": \"relative.map.gbx\"\n}", normalized);
    }

    [Theory]
    [InlineData("{\"SourceLabel\":\"/maps/Fake Map.Map.Gbx\"}", "SourceLabel")]
    [InlineData(@"{""sourceLabel"":""C:\\Maps\\Fake Map.Map.Gbx""}", "sourceLabel")]
    [InlineData(@"{""SourceLabel"":""\\\\server\\share\\Fake Map.Map.Gbx""}", "SourceLabel")]
    public void Normalize_ReplacesAbsoluteSourceLabels(string json, string propertyName)
    {
        var normalized = JsonReportWriter.Normalize(json);

        Assert.Equal($"{{\"{propertyName}\":\"<path>\"}}", normalized);
    }

    [Fact]
    public void ConsoleStatusSink_JsonModeWritesEverythingToStderr()
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var sink = new ConsoleStatusSink(toStderr: true);

            sink.Info("reading map");
            sink.Warn("synthetic warning");
            using (sink.Activity("measuring chunks"))
            {
            }
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        Assert.Equal(string.Empty, stdout.ToString());
        Assert.Contains("[info] reading map", stderr.ToString(), StringComparison.Ordinal);
        Assert.Contains("[warn] synthetic warning", stderr.ToString(), StringComparison.Ordinal);
        Assert.Contains("[info] measuring chunks...", stderr.ToString(), StringComparison.Ordinal);
        Assert.Matches(@"\[info\] done \(\d+ ms\)", stderr.ToString());
    }

    private static string GoldenPath() => Path.GetFullPath(
        Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../tests/GbxSizeTree.Tests/Output/golden-fake-analysis.json"));
}
