using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.Serialization;
using GBX.NET.Serialization.Chunking;
using GbxSizeTree.Abstractions;
using GbxSizeTree.Model;

namespace GbxSizeTree.Measure;

/// <summary>
/// Measures decompressed body chunk sizes with the single stateful writer required by the Gbx
/// lookback-string and node-reference format; see docs/FORMAT-NOTES.md § measurement.
/// </summary>
public sealed class CumulativeChunkMeasurer : IWriterDeltaMeasurer
{
    private const uint SkipMarker = 0x534B4950;
    private const uint NodeTerminator = 0xFACADE01;

    public WriterMeasurement Measure(Gbx gbx, CGameCtnChallenge map)
    {
        ArgumentNullException.ThrowIfNull(gbx);
        ArgumentNullException.ThrowIfNull(map);

        using var stream = new MemoryStream();
        using var writer = new GbxWriter(stream);
        var readerWriter = new GbxReaderWriter(writer);
        var deltas = new List<ChunkDelta>();
        Exception? firstFailure = null;
        var index = 0;

        foreach (var chunk in map.Chunks)
        {
            // Skipping header chunks mirrors CMwNod.Write (GBX.NET 2.4.4); see docs/FORMAT-NOTES.md.
            if (chunk is IHeaderChunk)
            {
                index++;
                continue;
            }

            var start = stream.Position;
            var skippable = chunk is ISkippableChunk;

            try
            {
                // Writing the chunk id first mirrors CMwNod.Write (GBX.NET 2.4.4); see docs/FORMAT-NOTES.md.
                writer.Write(chunk.Id);

                if (chunk is ISkippableChunk { Data: not null } rawChunk)
                {
                    // Re-emitting retained raw framing mirrors CMwNod.Write (GBX.NET 2.4.4); see docs/FORMAT-NOTES.md.
                    writer.Write(SkipMarker);
                    writer.Write(rawChunk.Data.Length);
                    writer.Write(rawChunk.Data);
                }
                else if (chunk is ISkippableChunk)
                {
                    // Reserving the skippable body size mirrors CMwNod.Write (GBX.NET 2.4.4); see docs/FORMAT-NOTES.md.
                    writer.Write(SkipMarker);
                    var sizePosition = stream.Position;
                    writer.Write(0);
                    var bodyPosition = stream.Position;

                    try
                    {
                        WriteBody(chunk, map, writer, readerWriter);
                    }
                    finally
                    {
                        // Patching the measured body size mirrors CMwNod.Write (GBX.NET 2.4.4); see docs/FORMAT-NOTES.md.
                        var end = stream.Position;
                        stream.Position = sizePosition;
                        writer.Write(checked((int)(end - bodyPosition)));
                        stream.Position = end;
                    }
                }
                else
                {
                    WriteBody(chunk, map, writer, readerWriter);
                }
            }
            catch (Exception exception)
            {
                firstFailure ??= exception;
            }

            deltas.Add(new ChunkDelta(chunk.Id, index, stream.Position - start, skippable));
            index++;
        }

        if (firstFailure is not null && deltas.All(delta => delta.Bytes == 0))
        {
            throw firstFailure;
        }

        // Writing the node terminator mirrors CMwNod.Write (GBX.NET 2.4.4); see docs/FORMAT-NOTES.md.
        writer.Write(NodeTerminator);
        var total = stream.Position;
        var reference = gbx.Body.UncompressedSize;

        return new WriterMeasurement(deltas, total, reference, reference - total);
    }

    private static void WriteBody(
        IChunk chunk,
        CGameCtnChallenge map,
        GbxWriter writer,
        GbxReaderWriter readerWriter)
    {
        // ReadWrite-before-Write dispatch mirrors CMwNod.Write (GBX.NET 2.4.4); see docs/FORMAT-NOTES.md.
        if (chunk is IReadableWritableChunk readableWritableChunk)
        {
            readableWritableChunk.ReadWrite(map, readerWriter);
        }
        else if (chunk is IWritableChunk writableChunk)
        {
            writableChunk.Write(map, writer);
        }
    }
}
