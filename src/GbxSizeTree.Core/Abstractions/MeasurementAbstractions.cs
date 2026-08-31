using GBX.NET;
using GBX.NET.Engines.Game;
using GbxSizeTree.Model;

namespace GbxSizeTree.Abstractions;

/// <summary>Scans a DECOMPRESSED body for chunk regions using skippable framing (exact sizes).</summary>
public interface IRawBodyScanner
{
    RawBodyScan Scan(ReadOnlyMemory<byte> decompressedBody);
}

/// <summary>
/// Measures each body chunk's serialized size by re-serializing through ONE shared GbxWriter
/// (cumulative position deltas). See docs/FORMAT-NOTES.md § measurement.
/// </summary>
public interface IWriterDeltaMeasurer
{
    WriterMeasurement Measure(Gbx gbx, CGameCtnChallenge map);
}

/// <summary>
/// Two-class on-disk attribution: pre-compressed payload bytes ≈ 1:1, remaining bytes scaled
/// so the total equals the real compressed size. Output order matches input order.
/// </summary>
public interface ICompressionAttribution
{
    IReadOnlyList<AttributedChunk> Attribute(
        IReadOnlyList<BodyChunkInfo> chunks, long uncompressedTotal, long compressedTotal);
}
