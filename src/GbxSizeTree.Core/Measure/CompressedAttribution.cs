using GbxSizeTree.Abstractions;
using GbxSizeTree.Model;

namespace GbxSizeTree.Measure;

/// <summary>
/// Attributes compressed body bytes with the two-class model documented in
/// <c>docs/FORMAT-NOTES.md</c>: already-compressed payload stays approximately 1:1 while
/// the remaining body mass shares one compression factor.
/// </summary>
public sealed class CompressedAttribution : ICompressionAttribution
{
    /// <summary>
    /// Gets the most recent estimate for unmeasured body bytes. Together with the returned
    /// chunk estimates, this reconciles exactly to the supplied compressed total.
    /// </summary>
    public long ResidualEstimate { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<AttributedChunk> Attribute(
        IReadOnlyList<BodyChunkInfo> chunks,
        long uncompressedTotal,
        long compressedTotal)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        if (uncompressedTotal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(uncompressedTotal));
        }

        if (compressedTotal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(compressedTotal));
        }

        var precompressed = new long[chunks.Count];
        long measuredTotal = 0;
        long precompressedTotal = 0;

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            if (chunk.Bytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunks), "Chunk byte counts cannot be negative.");
            }

            measuredTotal = checked(measuredTotal + chunk.Bytes);
            precompressed[i] = Math.Clamp(chunk.PrecompressedPayloadBytes, 0, chunk.Bytes);
            precompressedTotal = checked(precompressedTotal + precompressed[i]);
        }

        var compressibleTotal = uncompressedTotal - precompressedTotal;
        var useProRata = compressibleTotal <= 0 || compressedTotal < precompressedTotal;
        var factor = useProRata
            ? (uncompressedTotal == 0 ? 0d : (double)compressedTotal / uncompressedTotal)
            : (double)(compressedTotal - precompressedTotal) / compressibleTotal;

        var attributed = new AttributedChunk[chunks.Count];
        long attributedTotal = 0;

        for (var i = 0; i < chunks.Count; i++)
        {
            var estimate = useProRata
                ? RoundNonNegative(chunks[i].Bytes * factor)
                : RoundNonNegative(precompressed[i] + ((chunks[i].Bytes - precompressed[i]) * factor));

            attributed[i] = new AttributedChunk(chunks[i].ChunkId, estimate);
            attributedTotal = checked(attributedTotal + estimate);
        }

        var residualBytes = uncompressedTotal - measuredTotal;
        ResidualEstimate = RoundNonNegative(residualBytes * factor);

        var drift = compressedTotal - checked(attributedTotal + ResidualEstimate);
        if (attributed.Length == 0)
        {
            ResidualEstimate = compressedTotal;
            return attributed;
        }

        var largestIndex = 0;
        for (var i = 1; i < chunks.Count; i++)
        {
            if (chunks[i].Bytes > chunks[largestIndex].Bytes)
            {
                largestIndex = i;
            }
        }

        if (drift >= -attributed[largestIndex].EstimatedOnDiskBytes)
        {
            var adjusted = checked(attributed[largestIndex].EstimatedOnDiskBytes + drift);
            attributed[largestIndex] = attributed[largestIndex] with { EstimatedOnDiskBytes = adjusted };
        }
        else
        {
            var remainingReduction = checked(-drift - attributed[largestIndex].EstimatedOnDiskBytes);
            attributed[largestIndex] = attributed[largestIndex] with { EstimatedOnDiskBytes = 0 };

            for (var i = 0; i < attributed.Length && remainingReduction > 0; i++)
            {
                if (i == largestIndex)
                {
                    continue;
                }

                var reduction = Math.Min(attributed[i].EstimatedOnDiskBytes, remainingReduction);
                attributed[i] = attributed[i] with
                {
                    EstimatedOnDiskBytes = attributed[i].EstimatedOnDiskBytes - reduction,
                };
                remainingReduction -= reduction;
            }

            ResidualEstimate -= remainingReduction;
        }

        return attributed;
    }

    private static long RoundNonNegative(double value) =>
        value <= 0 ? 0 : checked((long)Math.Round(value, MidpointRounding.AwayFromZero));
}
