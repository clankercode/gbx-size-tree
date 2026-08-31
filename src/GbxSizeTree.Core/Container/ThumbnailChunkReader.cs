using System.Buffers.Binary;
using GbxSizeTree.Model;

namespace GbxSizeTree.Container;

/// <summary>
/// Reads header chunk 0x03043007 using the literal-marker layout documented in
/// <c>docs/FORMAT-NOTES.md</c>.
/// </summary>
public static class ThumbnailChunkReader
{
    private static ReadOnlySpan<byte> ThumbnailOpen => "<Thumbnail.jpg>"u8;
    private static ReadOnlySpan<byte> ThumbnailClose => "</Thumbnail.jpg>"u8;
    private static ReadOnlySpan<byte> CommentsOpen => "<Comments>"u8;
    private static ReadOnlySpan<byte> CommentsClose => "</Comments>"u8;

    /// <summary>Returns thumbnail accounting information, or null for an empty payload.</summary>
    public static ThumbnailInfo? Read(ReadOnlyMemory<byte> chunkPayload)
    {
        if (chunkPayload.IsEmpty)
        {
            return null;
        }

        var parsed = Parse(chunkPayload);
        var jpeg = JpegProbe.Read(parsed.Jpeg.Span);
        return new ThumbnailInfo(
            chunkPayload.Length,
            parsed.Jpeg.Length,
            jpeg.Width,
            jpeg.Height,
            parsed.CommentLength,
            jpeg.StrippableMetadataBytes);
    }

    /// <summary>Returns the JPEG byte slice from a 0x03043007 payload.</summary>
    public static ReadOnlyMemory<byte> GetJpeg(ReadOnlyMemory<byte> chunkPayload) => Parse(chunkPayload).Jpeg;

    private static ParsedThumbnail Parse(ReadOnlyMemory<byte> chunkPayload)
    {
        var span = chunkPayload.Span;
        var cursor = 0;
        _ = ReadInt32(span, ref cursor, "thumbnail version");
        var thumbnailSize = ReadNonNegativeInt32(span, ref cursor, "thumbnail JPEG size");
        Expect(span, ref cursor, ThumbnailOpen, "<Thumbnail.jpg>");

        EnsureAvailable(span, cursor, thumbnailSize, "thumbnail JPEG bytes");
        var jpegOffset = cursor;
        cursor += thumbnailSize;

        Expect(span, ref cursor, ThumbnailClose, "</Thumbnail.jpg>");
        Expect(span, ref cursor, CommentsOpen, "<Comments>");
        var commentLength = ReadNonNegativeInt32(span, ref cursor, "thumbnail comment length");
        EnsureAvailable(span, cursor, commentLength, "thumbnail comment bytes");
        cursor += commentLength;
        Expect(span, ref cursor, CommentsClose, "</Comments>");

        if (cursor != span.Length)
        {
            throw new InvalidDataException(
                $"Thumbnail chunk contains {span.Length - cursor} unexpected trailing bytes.");
        }

        return new ParsedThumbnail(chunkPayload.Slice(jpegOffset, thumbnailSize), commentLength);
    }

    private static int ReadInt32(ReadOnlySpan<byte> span, ref int cursor, string field)
    {
        EnsureAvailable(span, cursor, sizeof(int), field);
        var value = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(cursor, sizeof(int)));
        cursor += sizeof(int);
        return value;
    }

    private static int ReadNonNegativeInt32(ReadOnlySpan<byte> span, ref int cursor, string field)
    {
        var value = ReadInt32(span, ref cursor, field);
        if (value < 0)
        {
            throw new InvalidDataException($"{field} cannot be negative ({value}).");
        }

        return value;
    }

    private static void Expect(ReadOnlySpan<byte> span, ref int cursor, ReadOnlySpan<byte> marker, string name)
    {
        EnsureAvailable(span, cursor, marker.Length, $"literal marker {name}");
        if (!span.Slice(cursor, marker.Length).SequenceEqual(marker))
        {
            throw new InvalidDataException($"Invalid thumbnail chunk: expected literal marker {name} at offset {cursor}.");
        }

        cursor += marker.Length;
    }

    private static void EnsureAvailable(ReadOnlySpan<byte> span, int offset, int size, string field)
    {
        if (offset < 0 || size < 0 || offset > span.Length || size > span.Length - offset)
        {
            throw new InvalidDataException(
                $"Truncated thumbnail chunk while reading {field} at offset {offset}.");
        }
    }

    private readonly record struct ParsedThumbnail(ReadOnlyMemory<byte> Jpeg, int CommentLength);
}
