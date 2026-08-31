using System.Buffers.Binary;
using GBX.NET.Engines.Game;
using GbxSizeTree.Abstractions;
using GbxSizeTree.Model;

namespace GbxSizeTree.Drilldown;

/// <summary>
/// Reads the raw LightMapCacheSmall archive in map chunk 0x0304305B using the
/// verified layout in <c>docs/FORMAT-NOTES.md</c>.
/// </summary>
public sealed class LightmapDrilldown : ILightmapDrilldown
{
    private const int BuffersPerFrame = 3;

    public LightmapInfo? Inspect(
        ReadOnlyMemory<byte> decompressedBody,
        RawChunkRegion? lightmapRegion,
        CGameCtnChallenge map)
    {
        if (lightmapRegion is null)
        {
            return null;
        }

        ArgumentNullException.ThrowIfNull(map);

        var payload = GetPayload(decompressedBody, lightmapRegion);
        var cursor = 0;

        _ = ReadInt32(payload, ref cursor, "chunk version");
        var hasLightmaps = ReadInt32(payload, ref cursor, "u01") != 0;
        _ = ReadInt32(payload, ref cursor, "u02");
        _ = ReadInt32(payload, ref cursor, "u03");

        if (!hasLightmaps)
        {
            return new LightmapInfo(
                HasLightmaps: false,
                Version: 0,
                FrameCount: 0,
                Frames: [],
                WebpBytesTotal: 0,
                ZlibCompressedBytes: 0,
                ZlibUncompressedBytes: 0,
                ChunkBytes: lightmapRegion.Length);
        }

        var cacheVersion = ReadInt32(payload, ref cursor, "cache version");
        var frameCount = cacheVersion >= 5
            ? ReadNonNegativeInt32(payload, ref cursor, "frame count")
            : 1;

        var minimumFrameBytes = (long)frameCount * BuffersPerFrame * sizeof(int);
        if (minimumFrameBytes > payload.Length - cursor)
        {
            throw new InvalidDataException(
                $"Lightmap frame table declares {frameCount} frame(s), but only " +
                $"{payload.Length - cursor} payload byte(s) remain.");
        }

        var frames = new LightmapFrameInfo[frameCount];
        long webpBytesTotal = 0;
        var hasNonEmptyBuffer = false;

        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var blobBytes = new long[BuffersPerFrame];
            for (var bufferIndex = 0; bufferIndex < BuffersPerFrame; bufferIndex++)
            {
                var bufferLength = ReadNonNegativeInt32(
                    payload,
                    ref cursor,
                    $"frame {frameIndex} buffer {bufferIndex} length");
                EnsureAvailable(
                    payload,
                    cursor,
                    bufferLength,
                    $"frame {frameIndex} buffer {bufferIndex}");

                blobBytes[bufferIndex] = bufferLength;
                webpBytesTotal += bufferLength;
                hasNonEmptyBuffer |= bufferLength != 0;
                cursor += bufferLength;
            }

            frames[frameIndex] = new LightmapFrameInfo(frameIndex, blobBytes);
        }

        long zlibCompressedBytes = 0;
        long zlibUncompressedBytes = 0;
        if (hasNonEmptyBuffer)
        {
            zlibUncompressedBytes = ReadNonNegativeInt32(
                payload,
                ref cursor,
                "zlib uncompressed size");
            var zlibLength = ReadNonNegativeInt32(payload, ref cursor, "zlib length");
            EnsureAvailable(payload, cursor, zlibLength, "zlib data");
            zlibCompressedBytes = zlibLength;
            cursor += zlibLength;

            (zlibCompressedBytes, zlibUncompressedBytes) = CrossCheckCacheData(
                map,
                zlibCompressedBytes,
                zlibUncompressedBytes);
        }

        return new LightmapInfo(
            HasLightmaps: true,
            Version: cacheVersion,
            FrameCount: frameCount,
            Frames: frames,
            WebpBytesTotal: webpBytesTotal,
            ZlibCompressedBytes: zlibCompressedBytes,
            ZlibUncompressedBytes: zlibUncompressedBytes,
            ChunkBytes: lightmapRegion.Length);
    }

    private static ReadOnlySpan<byte> GetPayload(
        ReadOnlyMemory<byte> decompressedBody,
        RawChunkRegion region)
    {
        if (region.PayloadOffset < 0 ||
            region.PayloadLength < 0 ||
            region.PayloadOffset > decompressedBody.Length ||
            region.PayloadLength > decompressedBody.Length - region.PayloadOffset)
        {
            throw new InvalidDataException(
                $"Lightmap payload at offset {region.PayloadOffset} with length " +
                $"{region.PayloadLength} is outside the {decompressedBody.Length}-byte body.");
        }

        return decompressedBody.Span.Slice(
            checked((int)region.PayloadOffset),
            checked((int)region.PayloadLength));
    }

    private static int ReadNonNegativeInt32(
        ReadOnlySpan<byte> payload,
        ref int cursor,
        string field)
    {
        var value = ReadInt32(payload, ref cursor, field);
        if (value < 0)
        {
            throw new InvalidDataException($"Lightmap {field} cannot be negative ({value}).");
        }

        return value;
    }

    private static int ReadInt32(ReadOnlySpan<byte> payload, ref int cursor, string field)
    {
        EnsureAvailable(payload, cursor, sizeof(int), field);
        var value = BinaryPrimitives.ReadInt32LittleEndian(payload[cursor..]);
        cursor += sizeof(int);
        return value;
    }

    private static void EnsureAvailable(
        ReadOnlySpan<byte> payload,
        int offset,
        int length,
        string field)
    {
        if (offset < 0 || length < 0 || offset > payload.Length || length > payload.Length - offset)
        {
            throw new InvalidDataException(
                $"Truncated lightmap payload while reading {field} at offset {offset}: " +
                $"need {length} byte(s), but only {Math.Max(0, payload.Length - offset)} remain.");
        }
    }

    private static (long CompressedBytes, long UncompressedBytes) CrossCheckCacheData(
        CGameCtnChallenge map,
        long rawCompressedBytes,
        long rawUncompressedBytes)
    {
        // LightmapCacheData is the one safe parsed property here. The other lightmap
        // properties trigger the lazy zlib parse described by FORMAT-NOTES.md.
        var cacheData = map.LightmapCacheData;
        if (cacheData is null)
        {
            return (rawCompressedBytes, rawUncompressedBytes);
        }

        var parsedCompressedBytes = (long)cacheData.Data.Length;
        var parsedUncompressedBytes = (long)cacheData.UncompressedSize;
        return parsedCompressedBytes == rawCompressedBytes &&
               parsedUncompressedBytes == rawUncompressedBytes
            ? (parsedCompressedBytes, parsedUncompressedBytes)
            : (rawCompressedBytes, rawUncompressedBytes);
    }
}
