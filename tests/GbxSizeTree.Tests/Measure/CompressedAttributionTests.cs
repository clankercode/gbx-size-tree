using GbxSizeTree.Measure;
using GbxSizeTree.Model;

namespace GbxSizeTree.Tests.Measure;

public class CompressedAttributionTests
{
    [Fact]
    public void Attribute_UsesTwoClassesAndReconcilesChunksAndResidual()
    {
        var chunks = new[]
        {
            Chunk(0x03043054, 1_000, 1_000),
            Chunk(0x0304301F, 2_000),
            Chunk(0x0304305B, 1_000, 400),
        };
        var attribution = new CompressedAttribution();

        var result = attribution.Attribute(chunks, uncompressedTotal: 5_000, compressedTotal: 2_800);

        Assert.Equal(2_800, result.Sum(x => x.EstimatedOnDiskBytes) + attribution.ResidualEstimate);
        Assert.True(result[0].EstimatedOnDiskBytes >= chunks[0].Bytes * 0.99);
        Assert.Equal(1_000, result[0].EstimatedOnDiskBytes);
        Assert.Equal(778, result[1].EstimatedOnDiskBytes);
        Assert.Equal(389, attribution.ResidualEstimate);
    }

    [Fact]
    public void Attribute_WhenPrecompressedExceedsCompressedTotal_FallsBackWithoutNegatives()
    {
        var chunks = new[]
        {
            Chunk(0x03043054, 800, 800),
            Chunk(0x0304301F, 600),
            Chunk(0x0304305B, 400, 300),
        };
        var attribution = new CompressedAttribution();

        var result = attribution.Attribute(chunks, uncompressedTotal: 2_000, compressedTotal: 500);

        Assert.All(result, chunk => Assert.True(chunk.EstimatedOnDiskBytes >= 0));
        Assert.True(attribution.ResidualEstimate >= 0);
        Assert.Equal(500, result.Sum(x => x.EstimatedOnDiskBytes) + attribution.ResidualEstimate);
        Assert.Equal(200, result[0].EstimatedOnDiskBytes);
    }

    [Fact]
    public void Attribute_WhenClampedPrecompressedMassConsumesTotal_FallsBackProRata()
    {
        var chunks = new[]
        {
            Chunk(0x03043054, 800, 999),
            Chunk(0x0304301F, 200, 200),
        };
        var attribution = new CompressedAttribution();

        var result = attribution.Attribute(chunks, uncompressedTotal: 1_000, compressedTotal: 1_200);

        Assert.All(result, chunk => Assert.True(chunk.EstimatedOnDiskBytes >= 0));
        Assert.True(attribution.ResidualEstimate >= 0);
        Assert.Equal(1_200, result.Sum(x => x.EstimatedOnDiskBytes) + attribution.ResidualEstimate);
        Assert.Equal(960, result[0].EstimatedOnDiskBytes);
    }

    [Fact]
    public void Attribute_WhenRoundingDrifts_CorrectsTheLargestChunk()
    {
        var chunks = new[]
        {
            Chunk(0x0304301F, 3),
            Chunk(0x0304305B, 2),
        };
        var attribution = new CompressedAttribution();

        var result = attribution.Attribute(chunks, uncompressedTotal: 6, compressedTotal: 3);

        Assert.All(result, chunk => Assert.True(chunk.EstimatedOnDiskBytes >= 0));
        Assert.True(attribution.ResidualEstimate >= 0);
        Assert.Equal(3, result.Sum(x => x.EstimatedOnDiskBytes) + attribution.ResidualEstimate);
        Assert.Equal(1, result[0].EstimatedOnDiskBytes);
        Assert.Equal(1, result[1].EstimatedOnDiskBytes);
        Assert.Equal(1, attribution.ResidualEstimate);
    }

    [Fact]
    public void EstimateCompressed_HighlyCompressibleZerosAreSmall()
    {
        var payload = new byte[64 * 1024];

        var result = TrialLzoEstimator.EstimateCompressed(payload);

        Assert.True(result < 4_096, $"Expected fewer than 4096 bytes, got {result}.");
    }

    [Fact]
    public void EstimateCompressed_RandomBytesRemainLarge()
    {
        var payload = new byte[64 * 1024];
        new Random(42).NextBytes(payload);

        var result = TrialLzoEstimator.EstimateCompressed(payload);

        Assert.True(result > 60_000, $"Expected more than 60000 bytes, got {result}.");
    }

    private static BodyChunkInfo Chunk(uint id, long bytes, long precompressed = 0) =>
        new(
            id,
            "Fake",
            SizeCategory.Other,
            "Synthetic test chunk",
            bytes,
            SizeConfidence.ExactOnDisk,
            BodyOffset: null,
            Skippable: true,
            Order: 0,
            precompressed);
}
