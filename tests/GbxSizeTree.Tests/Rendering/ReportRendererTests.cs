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
        Assert.Contains("Fake Map by Fake Author", console.Output);
        Assert.Contains("on disk", console.Output);
        Assert.Contains("on disk 11.7 KiB", console.Output);
        Assert.Contains("on disk ≈ 1.8 KiB", console.Output);
        Assert.DoesNotContain("83.3%", console.Output);
        Assert.Contains("Accounted on disk:", console.Output);
        Assert.Contains("Lightmap breakdown", console.Output);
        Assert.Contains("WebP shadow images", console.Output);
        Assert.Contains("Mapping cache (zlib)", console.Output);
        Assert.Contains("3 frames · 512×512", console.Output);
        Assert.Contains("Resolution", console.Output);
        Assert.Contains("matches category contribution", console.Output);
        Assert.Contains("Delta to online limit", console.Output);
        Assert.True(
            console.Output.Split("12,000 B", StringSplitOptions.None).Length >= 3,
            "The title and category reconciliation should show the exact file size.");
        Assert.Contains("Blocks", console.Output);
        Assert.Contains("Vertices", console.Output);
        Assert.Contains("1,234", console.Output);
        Assert.Contains("Used", console.Output);
        Assert.DoesNotContain("Referenced", console.Output);

        var categoryIndex = console.Output.IndexOf("On-disk contribution by category", StringComparison.Ordinal);
        var lightmapIndex = console.Output.IndexOf("Lightmap breakdown", StringComparison.Ordinal);
        var deltaIndex = console.Output.IndexOf("Delta to online limit", StringComparison.Ordinal);
        var treeIndex = console.Output.IndexOf("Fake Map.Map.Gbx", deltaIndex, StringComparison.Ordinal);
        Assert.True(categoryIndex >= 0 && categoryIndex < lightmapIndex);
        Assert.True(lightmapIndex < deltaIndex);
        Assert.True(deltaIndex < treeIndex);
    }

    [Fact]
    public void Title_DeformatsMapNameAndFallsBackToLoginWhenNicknameEmpty()
    {
        var console = new TestConsole().Width(120);
        var analysis = FakeAnalysis.Build();
        analysis = analysis with
        {
            Facts = analysis.Facts! with
            {
                MapName = "$o$f00Hot$g Lap",
                AuthorNickname = "",
                AuthorLogin = "plain-login",
            },
        };

        ReportRenderer.Render(console, analysis, topN: 10);

        Assert.Contains("Hot Lap by plain-login", console.Output);
        Assert.DoesNotContain("$f00", console.Output);
    }

    [Fact]
    public void Title_OmitsIdentityLineWithoutFacts()
    {
        var console = new TestConsole().Width(120);
        var analysis = FakeAnalysis.Build() with { Facts = null };

        ReportRenderer.Render(console, analysis, topN: 10);

        Assert.DoesNotContain("by Fake Author", console.Output);
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
    [InlineData(8_000_000, "over")]
    [InlineData(12_000, "headroom")]
    [InlineData(7_340_032, "Exactly at")]
    public void FullReport_RendersOnlineLimitDelta(long fileBytes, string expected)
    {
        var console = new TestConsole().Width(120);
        var analysis = FakeAnalysis.Build() with { FileBytes = fileBytes };

        ReportRenderer.Render(console, analysis, topN: 10);

        Assert.Contains(expected, console.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FullReport_RendersMixedAndUnknownLightmapResolutions()
    {
        var console = new TestConsole().Width(120);
        var analysis = FakeAnalysis.Build();
        var lightmap = analysis.Body!.Lightmap!;
        var mixedFrame = new LightmapFrameInfo(0, [300L, 200L, 100L])
        {
            BlobDimensions = [new(512, 512), new(256, 256), null],
        };
        analysis = analysis with
        {
            Body = analysis.Body with
            {
                Lightmap = lightmap with { Frames = [mixedFrame], FrameCount = 1 },
            },
        };

        ReportRenderer.Render(console, analysis, topN: 10);

        Assert.Contains("1 frame · mixed resolutions · partly unknown", console.Output);
        Assert.Contains("512×512 / 256×256 / —", console.Output);
    }

    [Fact]
    public void FullReport_MarksRecoveredVertexCountsAsEstimated()
    {
        var console = new TestConsole().Width(120);
        var analysis = FakeAnalysis.Build();
        var zip = analysis.Body!.EmbeddedZip!;
        analysis = analysis with
        {
            Body = analysis.Body with
            {
                EmbeddedZip = zip with
                {
                    Entries = zip.Entries
                        .Select((entry, index) => index == 0
                            ? entry with { VertexCountEstimated = true }
                            : entry)
                        .ToArray(),
                },
            },
        };

        ReportRenderer.Render(console, analysis, topN: 10);

        Assert.Contains("~1,234", console.Output);
    }

    [Theory]
    [InlineData(800, 300, "654 B", "246 B")]
    [InlineData(500, 300, "100 B", "Chunk overhead")]
    public void FullReport_LightmapRowsReconcileToNormalizedCategoryContribution(
        long webpBytes,
        long cacheBytes,
        string expectedComponent,
        string expectedOther)
    {
        var console = new TestConsole().Width(120);
        var analysis = FakeAnalysis.Build();
        var lightmap = analysis.Body!.Lightmap! with
        {
            WebpBytesTotal = webpBytes,
            ZlibCompressedBytes = cacheBytes,
            ChunkBytes = 900,
        };
        var tree = new SizeNode(
            "file", "test", SizeCategory.Other, 1_000, 1_000, null,
            SizeConfidence.ExactOnDisk, null,
            [
                new SizeNode(
                    "body", "Body", SizeCategory.Other, 900, 900, null,
                    SizeConfidence.ExactOnDisk, null,
                    [SizeNode.Leaf(
                        "body.lightmap", "Lightmap", SizeCategory.Lightmap, 900,
                        SizeConfidence.ExactOnDisk, estOnDisk: 900)]),
                SizeNode.Leaf(
                    "header", "Header", SizeCategory.Header, 100,
                    SizeConfidence.ExactOnDisk, onDisk: 100),
            ]);
        analysis = analysis with
        {
            FileBytes = 1_000,
            Body = analysis.Body with { Lightmap = lightmap },
            Tree = tree,
        };

        ReportRenderer.Render(console, analysis, topN: 10);

        Assert.Contains(expectedComponent, console.Output);
        Assert.Contains(expectedOther, console.Output);
        Assert.Contains("Total 900 B · matches category contribution", console.Output);
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

    [Fact]
    public void BodyChunks_CollapsesRowsSmallerThan420Bytes()
    {
        var console = new TestConsole().Width(120);
        var analysis = FakeAnalysis.Build();
        var chunks = analysis.Body!.Chunks.Concat([
            new BodyChunkInfo(1, "Exactly 420", SizeCategory.Other, "", 420,
                SizeConfidence.ExactOnDisk, 0, true, 10),
            new BodyChunkInfo(2, "Small 419", SizeCategory.Other, "", 419,
                SizeConfidence.ExactOnDisk, 0, true, 11),
            new BodyChunkInfo(3, "Small 100", SizeCategory.Other, "", 100,
                SizeConfidence.ExactOnDisk, 0, true, 12),
        ]).ToList();
        analysis = analysis with { Body = analysis.Body with { Chunks = chunks } };

        DrilldownTables.Render(console, analysis, topN: 0);

        Assert.Contains("Exactly 420", console.Output);
        Assert.DoesNotContain("Small 419", console.Output);
        Assert.DoesNotContain("Small 100", console.Output);
        Assert.Contains("and 2 more", console.Output);
        Assert.Contains("519 B", console.Output);
    }
}
