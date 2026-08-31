using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.LZO;
using GBX.NET.Serialization.Chunking;
using GBX.NET.ZLib;
using GbxSizeTree.Measure;
using GbxSizeTree.Tests.Fixtures;

namespace GbxSizeTree.Tests.Measure;

public sealed class CumulativeChunkMeasurerTests
{
    private const uint BlocksChunkId = 0x0304301F;
    private const uint ItemsChunkId = 0x03043040;
    private const uint BakedBlocksChunkId = 0x03043048;
    private const uint EmbeddedChunkId = 0x03043054;
    private const uint LightmapChunkId = 0x0304305B;

    [Fact]
    public void Measure_SampleMap_ProducesCumulativeBodyDeltas()
    {
        SampleMap.SkipUnlessAvailable();
        Gbx.LZO = new Lzo();
        Gbx.ZLib = new ZLib();
        var gbx = Gbx.Parse<CGameCtnChallenge>(SampleMap.Path);
        var map = gbx.Node;

        var measurement = new CumulativeChunkMeasurer().Measure(gbx, map);

        Assert.Equal(SampleMap.BodyUncompressed, gbx.Body.UncompressedSize);
        var expectedChunkCount = map.Chunks.Count(chunk => chunk is not IHeaderChunk);
        Assert.Equal(expectedChunkCount, measurement.Deltas.Count);
        Assert.All(measurement.Deltas, delta => Assert.True(delta.Bytes > 0));

        var indexes = measurement.Deltas.Select(delta => delta.Index).ToArray();
        Assert.Equal(indexes.Length, indexes.Distinct().Count());
        Assert.True(indexes.SequenceEqual(indexes.Order()));

        var relativeDifference = Math.Abs(measurement.TotalWrittenBytes - gbx.Body.UncompressedSize)
            / (double)gbx.Body.UncompressedSize;
        Assert.True(relativeDifference < 0.005, $"relative difference was {relativeDifference:P4}");
        Assert.Equal(
            measurement.ReferenceUncompressedBytes - measurement.TotalWrittenBytes,
            measurement.ResidualBytes);

        var requiredIds = new[]
        {
            BlocksChunkId,
            ItemsChunkId,
            BakedBlocksChunkId,
            EmbeddedChunkId,
            LightmapChunkId,
        };
        Assert.All(requiredIds, id => Assert.Contains(measurement.Deltas, delta => delta.ChunkId == id));

        var blocks = Assert.Single(measurement.Deltas, delta => delta.ChunkId == BlocksChunkId);
        Assert.True(blocks.Bytes > measurement.Deltas.Average(delta => delta.Bytes));

        var embedded = Assert.Single(measurement.Deltas, delta => delta.ChunkId == EmbeddedChunkId);
        var embeddedPayloadBytes = Assert.IsType<byte[]>(map.EmbeddedZipData).LongLength;
        Assert.True(embedded.Bytes > embeddedPayloadBytes);
        Assert.InRange(embeddedPayloadBytes / (double)embedded.Bytes, 0.99, 1.0);
    }
}
