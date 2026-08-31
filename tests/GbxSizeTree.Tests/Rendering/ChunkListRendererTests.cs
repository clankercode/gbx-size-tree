using GbxSizeTree.Cli.Rendering;
using GbxSizeTree.Model;
using GbxSizeTree.Semantics;
using GbxSizeTree.Tests.Fixtures;
using Spectre.Console.Testing;

namespace GbxSizeTree.Tests.Rendering;

public sealed class ChunkListRendererTests
{
    [Fact]
    public void RenderAll_ListsEveryHeaderAndBodyChunkWithoutCutoff()
    {
        var console = new TestConsole().Width(140);

        ChunkListRenderer.RenderAll(console, FakeAnalysis.Build());

        var output = console.Output;
        Assert.Contains("Thumbnail", output, StringComparison.Ordinal);
        Assert.Contains("0x0304301F", output, StringComparison.Ordinal);
        Assert.Contains("0x0304305B", output, StringComparison.Ordinal);
        Assert.Contains("0x03043054", output, StringComparison.Ordinal);
        Assert.Contains("skippable", output, StringComparison.Ordinal);
        Assert.Contains("Optional", output, StringComparison.Ordinal);
        Assert.Contains("✗", output, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderAll_MarksVerifiedDiscardOnLoadChunksOptional()
    {
        var console = new TestConsole().Width(140);
        var analysis = FakeAnalysis.Build();
        var body = analysis.Body!;
        analysis = analysis with
        {
            Body = body with
            {
                Chunks = body.Chunks.Append(new BodyChunkInfo(
                    0x03043061, "Write-only snapshot", SizeCategory.Metadata, "snapshot", 32,
                    SizeConfidence.ExactOnDisk, BodyOffset: 9_450, Skippable: true, Order: 3)).ToList(),
            },
        };

        ChunkListRenderer.RenderAll(console, analysis);

        Assert.Contains("✓", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownChunks_CollectOnlyReturnsIdsMissingFromTheCatalog()
    {
        var unknown = UnknownChunks.Collect(WithUnknownChunks());

        Assert.Equal(2, unknown.Count);
        var header = Assert.Single(unknown, chunk => chunk.Section == "header");
        Assert.Equal("0x03043FEE", header.ChunkId);
        var body = Assert.Single(unknown, chunk => chunk.Section == "body");
        Assert.Equal("0x0304FBFF", body.ChunkId);
        Assert.True(body.Skippable);
        Assert.Equal(123, body.Bytes);
    }

    [Fact]
    public void RenderUnknown_ReportsCleanCatalogWhenEverythingIsKnown()
    {
        var console = new TestConsole().Width(140);

        var count = ChunkListRenderer.RenderUnknown(console, FakeAnalysis.Build());

        Assert.Equal(0, count);
        Assert.Contains("no unknown chunks", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderUnknown_ListsEachUnknownChunk()
    {
        var console = new TestConsole().Width(140);

        var count = ChunkListRenderer.RenderUnknown(console, WithUnknownChunks());

        Assert.Equal(2, count);
        Assert.Contains("0x0304FBFF", console.Output, StringComparison.Ordinal);
        Assert.Contains("0x03043FEE", console.Output, StringComparison.Ordinal);
        Assert.Contains("2 unknown chunk(s)", console.Output, StringComparison.Ordinal);
    }

    private static MapAnalysis WithUnknownChunks()
    {
        var analysis = FakeAnalysis.Build();
        return analysis with
        {
            Header = analysis.Header with
            {
                Chunks =
                [
                    .. analysis.Header.Chunks,
                    new HeaderChunkInfo(0x03043FEE, "Unknown 0x03043FEE", 55, Heavy: false, FileOffset: 9_000),
                ],
            },
            Body = analysis.Body! with
            {
                Chunks =
                [
                    .. analysis.Body!.Chunks,
                    new BodyChunkInfo(0x0304FBFF, "Unknown 0x0304FBFF", SizeCategory.Other,
                        "unrecognized chunk", 123, SizeConfidence.ExactOnDisk,
                        BodyOffset: 9_450, Skippable: true, Order: 3),
                ],
            },
        };
    }
}
