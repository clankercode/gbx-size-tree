using GBX.NET;
using GBX.NET.LZO;

namespace GbxSizeTree.Measure;

/// <summary>
/// Produces a standalone LZO1x-999 trial size using the GBX.NET 2.4.4 compressor described in
/// <c>docs/FORMAT-NOTES.md</c>. The result is an upper-bound estimate for an isolated payload.
/// </summary>
public static class TrialLzoEstimator
{
    /// <summary>Compresses one payload independently and returns its compressed byte length.</summary>
    public static long EstimateCompressed(ReadOnlySpan<byte> chunkPayload)
    {
        Gbx.LZO ??= new Lzo();
        var payload = chunkPayload.ToArray();
        return Gbx.LZO.Compress(payload).Length;
    }
}
