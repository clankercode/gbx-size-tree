using System.Buffers.Binary;
using GbxSizeTree.Model;

namespace GbxSizeTree.Container;

/// <summary>
/// Reads the byte-level GBX container layout documented in <c>docs/FORMAT-NOTES.md</c>.
/// </summary>
public static class GbxContainerReader
{
    private const int FixedHeaderSize = 17;
    private const uint HeavyBit = 0x80000000;

    private static readonly IReadOnlyDictionary<uint, string> HeaderChunkNames =
        new Dictionary<uint, string>
        {
            [0x03043002] = "Legacy description",
            [0x03043003] = "Common",
            [0x03043004] = "Version",
            [0x03043005] = "XML",
            [0x03043007] = "Thumbnail",
            [0x03043008] = "Author",
        };

    /// <summary>Reads a GBX layout without parsing any GBX.NET nodes.</summary>
    public static GbxFileLayout Read(ReadOnlySpan<byte> file)
    {
        EnsureAvailable(file, 0, FixedHeaderSize, "fixed GBX header");
        if (!file[..3].SequenceEqual("GBX"u8))
        {
            throw new InvalidDataException("Invalid GBX magic; expected ASCII 'GBX'.");
        }

        var version = BinaryPrimitives.ReadInt16LittleEndian(file[3..5]);
        var format = ReadContainerCode(file[5], "format", "B");
        var refTableCompression = ReadContainerCode(file[6], "reference-table compression", "UC");
        var bodyCompression = ReadContainerCode(file[7], "body compression", "UC");
        _ = ReadContainerCode(file[8], "header marker", "RE");
        var classId = BinaryPrimitives.ReadUInt32LittleEndian(file[9..13]);
        var userDataSizeValue = BinaryPrimitives.ReadUInt32LittleEndian(file[13..17]);
        if (userDataSizeValue > int.MaxValue)
        {
            throw new InvalidDataException($"User-data size {userDataSizeValue} exceeds the supported range.");
        }

        var userDataSize = (int)userDataSizeValue;
        const int userDataOffset = FixedHeaderSize;
        EnsureAvailable(file, userDataOffset, userDataSize, "GBX user data");

        var headerChunks = ReadHeaderChunks(file, userDataOffset, userDataSize);
        var cursor = checked(userDataOffset + userDataSize);
        var numNodes = ReadNonNegativeInt32(file, ref cursor, "node count");

        var refTableOffset = cursor;
        var numExternalNodes = ReadNonNegativeInt32(file, ref cursor, "external-node count");
        if (numExternalNodes != 0)
        {
            throw new NotSupportedException(
                "External-node reference-table framing is not specified by docs/FORMAT-NOTES.md.");
        }

        var refTableSize = cursor - refTableOffset;
        int? bodyUncompressedSize = null;
        int? bodyCompressedSize = null;

        if (bodyCompression == 'C')
        {
            bodyUncompressedSize = ReadNonNegativeInt32(file, ref cursor, "uncompressed body size");
            bodyCompressedSize = ReadNonNegativeInt32(file, ref cursor, "compressed body size");
            EnsureAvailable(file, cursor, bodyCompressedSize.Value, "compressed body");
            if (file.Length - cursor != bodyCompressedSize.Value)
            {
                throw new InvalidDataException(
                    $"Compressed body declares {bodyCompressedSize.Value} bytes, but {file.Length - cursor} remain.");
            }
        }

        return new GbxFileLayout(
            Version: version,
            Format: format,
            RefTableCompressed: refTableCompression == 'C',
            BodyCompressed: bodyCompression == 'C',
            ClassId: classId,
            UserDataOffset: userDataOffset,
            UserDataSize: userDataSize,
            HeaderChunks: headerChunks,
            NumNodes: numNodes,
            NumExternalNodes: numExternalNodes,
            RefTableOffset: refTableOffset,
            RefTableSize: refTableSize,
            BodyOffset: cursor,
            BodyUncompressedSize: bodyUncompressedSize,
            BodyCompressedSize: bodyCompressedSize);
    }

    /// <summary>Reads a GBX layout from <paramref name="path"/>.</summary>
    public static GbxFileLayout ReadFile(string path) => Read(File.ReadAllBytes(path));

    private static IReadOnlyList<HeaderChunkInfo> ReadHeaderChunks(
        ReadOnlySpan<byte> file,
        int userDataOffset,
        int userDataSize)
    {
        if (userDataSize == 0)
        {
            return [];
        }

        var userDataEnd = checked(userDataOffset + userDataSize);
        var cursor = userDataOffset;
        var chunkCount = ReadNonNegativeInt32(file, ref cursor, "header chunk count");
        if (chunkCount > (userDataEnd - cursor) / 8)
        {
            throw new InvalidDataException(
                $"Header chunk table declares {chunkCount} entries outside the user-data bounds.");
        }

        var entries = new (uint Id, int Size, bool Heavy)[chunkCount];
        for (var i = 0; i < entries.Length; i++)
        {
            var id = ReadUInt32(file, ref cursor, $"header chunk {i} id");
            var rawSize = ReadUInt32(file, ref cursor, $"header chunk {i} size");
            entries[i] = (id, checked((int)(rawSize & ~HeavyBit)), (rawSize & HeavyBit) != 0);
        }

        var chunks = new HeaderChunkInfo[entries.Length];
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            EnsureAvailableWithin(file, cursor, entry.Size, userDataEnd, $"header chunk {i} payload");
            chunks[i] = new HeaderChunkInfo(
                entry.Id,
                HeaderChunkNames.TryGetValue(entry.Id, out var name) ? name : $"0x{entry.Id:X8}",
                entry.Size,
                entry.Heavy,
                cursor);
            cursor = checked(cursor + entry.Size);
        }

        if (cursor != userDataEnd)
        {
            throw new InvalidDataException(
                $"Header chunks end at offset {cursor}, but user data ends at offset {userDataEnd}.");
        }

        return chunks;
    }

    private static char ReadContainerCode(byte value, string field, string allowed)
    {
        var code = (char)value;
        if (!allowed.Contains(code, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported GBX {field} byte 0x{value:X2}.");
        }

        return code;
    }

    private static int ReadNonNegativeInt32(ReadOnlySpan<byte> file, ref int cursor, string field)
    {
        EnsureAvailable(file, cursor, sizeof(int), field);
        var value = BinaryPrimitives.ReadInt32LittleEndian(file.Slice(cursor, sizeof(int)));
        cursor += sizeof(int);
        if (value < 0)
        {
            throw new InvalidDataException($"GBX {field} cannot be negative ({value}).");
        }

        return value;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> file, ref int cursor, string field)
    {
        EnsureAvailable(file, cursor, sizeof(uint), field);
        var value = BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(cursor, sizeof(uint)));
        cursor += sizeof(uint);
        return value;
    }

    private static void EnsureAvailable(ReadOnlySpan<byte> file, int offset, int size, string field) =>
        EnsureAvailableWithin(file, offset, size, file.Length, field);

    private static void EnsureAvailableWithin(
        ReadOnlySpan<byte> file,
        int offset,
        int size,
        int end,
        string field)
    {
        if (offset < 0 || size < 0 || end < 0 || end > file.Length || offset > end || size > end - offset)
        {
            throw new InvalidDataException(
                $"Truncated GBX while reading {field} at offset {offset}: need {size} bytes, " +
                $"but only {Math.Max(0, end - offset)} are available.");
        }
    }
}
