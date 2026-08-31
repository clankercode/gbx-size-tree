using System.Buffers.Binary;

namespace GbxSizeTree.Container;

/// <summary>
/// Reads WebP canvas dimensions without decoding pixels. Supports the simple
/// lossy/lossless headers and the extended WebP container header.
/// </summary>
public static class WebpProbe
{
    public static (int Width, int Height)? TryReadDimensions(ReadOnlySpan<byte> webp)
    {
        if (webp.Length < 20 ||
            !webp[..4].SequenceEqual("RIFF"u8) ||
            !webp.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return null;
        }

        var riffLengthValue = BinaryPrimitives.ReadUInt32LittleEndian(webp.Slice(4, 4));
        if (riffLengthValue < 12 || riffLengthValue > webp.Length - 8)
        {
            return null;
        }

        var riffEnd = 8 + (int)riffLengthValue;
        var cursor = 12;
        while (cursor <= riffEnd - 8)
        {
            var chunkType = webp.Slice(cursor, 4);
            var chunkLengthValue = BinaryPrimitives.ReadUInt32LittleEndian(webp.Slice(cursor + 4, 4));
            if (chunkLengthValue > int.MaxValue)
            {
                return null;
            }

            var chunkLength = (int)chunkLengthValue;
            var payloadOffset = cursor + 8;
            if (chunkLength > riffEnd - payloadOffset)
            {
                return null;
            }

            var payload = webp.Slice(payloadOffset, chunkLength);
            var dimensions = ReadChunkDimensions(chunkType, payload);
            if (dimensions is not null)
            {
                return dimensions;
            }

            var paddedLength = chunkLength + (chunkLength & 1);
            if (paddedLength > riffEnd - payloadOffset)
            {
                return null;
            }

            cursor = payloadOffset + paddedLength;
        }

        return null;
    }

    private static (int Width, int Height)? ReadChunkDimensions(
        ReadOnlySpan<byte> chunkType,
        ReadOnlySpan<byte> payload)
    {
        if (chunkType.SequenceEqual("VP8X"u8))
        {
            if (payload.Length < 10)
            {
                return null;
            }

            return (ReadUInt24LittleEndian(payload.Slice(4, 3)) + 1,
                ReadUInt24LittleEndian(payload.Slice(7, 3)) + 1);
        }

        if (chunkType.SequenceEqual("VP8 "u8))
        {
            if (payload.Length < 10 || !payload.Slice(3, 3).SequenceEqual(new byte[] { 0x9D, 0x01, 0x2A }))
            {
                return null;
            }

            var width = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(6, 2)) & 0x3FFF;
            var height = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(8, 2)) & 0x3FFF;
            return width > 0 && height > 0 ? (width, height) : null;
        }

        if (chunkType.SequenceEqual("VP8L"u8))
        {
            if (payload.Length < 5 || payload[0] != 0x2F)
            {
                return null;
            }

            var bits = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(1, 4));
            if ((bits >> 29) != 0)
            {
                return null;
            }

            var width = (int)(bits & 0x3FFF) + 1;
            var height = (int)((bits >> 14) & 0x3FFF) + 1;
            return (width, height);
        }

        return null;
    }

    private static int ReadUInt24LittleEndian(ReadOnlySpan<byte> bytes) =>
        bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);
}
