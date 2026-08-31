using System.Buffers.Binary;

namespace GbxSizeTree.Container;

/// <summary>
/// Probes JPEG SOF and metadata segments using the thumbnail format facts in
/// <c>docs/FORMAT-NOTES.md</c>, without decoding image pixels.
/// </summary>
public static class JpegProbe
{
    /// <summary>Returns dimensions and the complete byte size of APPn and COM segments.</summary>
    public static (int? Width, int? Height, long StrippableMetadataBytes) Read(ReadOnlySpan<byte> jpeg)
    {
        if (jpeg.Length < 2 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            throw new InvalidDataException("Invalid JPEG: missing SOI marker.");
        }

        int? width = null;
        int? height = null;
        long metadataBytes = 0;
        var cursor = 2;

        while (cursor < jpeg.Length)
        {
            while (cursor < jpeg.Length && jpeg[cursor] != 0xFF)
            {
                cursor++;
            }

            if (cursor == jpeg.Length)
            {
                break;
            }

            var markerOffset = cursor++;
            while (cursor < jpeg.Length && jpeg[cursor] == 0xFF)
            {
                cursor++;
            }

            if (cursor == jpeg.Length)
            {
                throw new InvalidDataException("Truncated JPEG marker at end of data.");
            }

            var marker = jpeg[cursor++];
            if (marker == 0x00 || marker is >= 0xD0 and <= 0xD9 || marker == 0x01)
            {
                if (marker == 0xD9)
                {
                    break;
                }

                continue;
            }

            if (jpeg.Length - cursor < 2)
            {
                throw new InvalidDataException($"Truncated JPEG segment length at offset {markerOffset}.");
            }

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(jpeg.Slice(cursor, 2));
            if (segmentLength < 2 || segmentLength > jpeg.Length - cursor)
            {
                throw new InvalidDataException(
                    $"Invalid or truncated JPEG segment 0xFF{marker:X2} at offset {markerOffset}.");
            }

            if (marker is >= 0xE0 and <= 0xEF || marker == 0xFE)
            {
                metadataBytes += 2L + segmentLength;
            }

            if (marker is 0xC0 or 0xC1 or 0xC2)
            {
                if (segmentLength < 7)
                {
                    throw new InvalidDataException($"JPEG SOF segment at offset {markerOffset} is too short.");
                }

                height = BinaryPrimitives.ReadUInt16BigEndian(jpeg.Slice(cursor + 3, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(jpeg.Slice(cursor + 5, 2));
            }

            cursor += segmentLength;
        }

        return (width, height, metadataBytes);
    }
}
