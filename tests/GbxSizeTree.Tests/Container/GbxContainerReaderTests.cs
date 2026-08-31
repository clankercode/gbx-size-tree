using System.Buffers.Binary;
using GbxSizeTree.Container;
using GbxSizeTree.Tests.Fixtures;

namespace GbxSizeTree.Tests.Container;

public sealed class GbxContainerReaderTests
{
    [Fact]
    public void Read_CompressedSyntheticContainer_ReturnsEveryLayoutField()
    {
        var file = BuildSyntheticContainer(bodyCompressed: true);

        var layout = GbxContainerReader.Read(file);

        Assert.Equal(6, layout.Version);
        Assert.Equal('B', layout.Format);
        Assert.False(layout.RefTableCompressed);
        Assert.True(layout.BodyCompressed);
        Assert.Equal(0x03043000U, layout.ClassId);
        Assert.Equal(17, layout.UserDataOffset);
        Assert.Equal(25, layout.UserDataSize);
        Assert.Collection(
            layout.HeaderChunks,
            chunk =>
            {
                Assert.Equal(0x03043002U, chunk.ChunkId);
                Assert.Equal("Legacy description", chunk.Name);
                Assert.Equal(3, chunk.Bytes);
                Assert.False(chunk.Heavy);
                Assert.Equal(37, chunk.FileOffset);
            },
            chunk =>
            {
                Assert.Equal(0x12345678U, chunk.ChunkId);
                Assert.Equal("0x12345678", chunk.Name);
                Assert.Equal(2, chunk.Bytes);
                Assert.True(chunk.Heavy);
                Assert.Equal(40, chunk.FileOffset);
            });
        Assert.Equal(688, layout.NumNodes);
        Assert.Equal(0, layout.NumExternalNodes);
        Assert.Equal(46, layout.RefTableOffset);
        Assert.Equal(4, layout.RefTableSize);
        Assert.Equal(58, layout.BodyOffset);
        Assert.Equal(9, layout.BodyUncompressedSize);
        Assert.Equal(4, layout.BodyCompressedSize);
        Assert.Equal(layout.BodyOffset + layout.BodyCompressedSize, file.Length);
    }

    [Fact]
    public void Read_UncompressedSyntheticContainer_LeavesBodySizesNull()
    {
        var file = BuildSyntheticContainer(bodyCompressed: false);

        var layout = GbxContainerReader.Read(file);

        Assert.False(layout.BodyCompressed);
        Assert.Equal(50, layout.BodyOffset);
        Assert.Null(layout.BodyUncompressedSize);
        Assert.Null(layout.BodyCompressedSize);
        Assert.Equal(4, file.Length - layout.BodyOffset);
    }

    [Fact]
    public void Read_TruncatedCompressedBody_ThrowsClearException()
    {
        var file = BuildSyntheticContainer(bodyCompressed: true);

        var error = Assert.Throws<InvalidDataException>(() => GbxContainerReader.Read(file.AsSpan(0, file.Length - 1)));

        Assert.Contains("Truncated GBX", error.Message, StringComparison.Ordinal);
        Assert.Contains("compressed body", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_TruncatedHeaderChunkTable_ThrowsWithoutReadingOutOfBounds()
    {
        var file = BuildSyntheticContainer(bodyCompressed: true);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(17, 4), 100);

        var error = Assert.Throws<InvalidDataException>(() => GbxContainerReader.Read(file));

        Assert.Contains("chunk table", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadFile_Sample_ReturnsVerifiedContainerLayout()
    {
        SampleMap.SkipUnlessAvailable();

        var layout = GbxContainerReader.ReadFile(SampleMap.Path);

        Assert.Equal(SampleMap.UserDataSize, layout.UserDataSize);
        Assert.Equal(688, layout.NumNodes);
        Assert.Equal(0, layout.NumExternalNodes);
        Assert.Equal(SampleMap.BodyUncompressed, layout.BodyUncompressedSize.GetValueOrDefault());
        Assert.Equal(SampleMap.BodyCompressed, layout.BodyCompressedSize.GetValueOrDefault());
        Assert.Equal(SampleMap.FileBytes, layout.BodyOffset + layout.BodyCompressedSize);
        Assert.Collection(
            layout.HeaderChunks,
            chunk => AssertHeaderChunk(chunk, 0x03043002, 57, heavy: false),
            chunk => AssertHeaderChunk(chunk, 0x03043003, 241, heavy: false),
            chunk => AssertHeaderChunk(chunk, 0x03043004, 4, heavy: false),
            chunk => AssertHeaderChunk(chunk, 0x03043005, 6_135, heavy: true),
            chunk => AssertHeaderChunk(chunk, 0x03043007, 58_889, heavy: true),
            chunk => AssertHeaderChunk(chunk, 0x03043008, 95, heavy: false));
    }

    [Fact]
    public void GetBody_Sample_ReturnsExactDecompressedBody()
    {
        SampleMap.SkipUnlessAvailable();

        var body = DecompressedBody.GetBody(SampleMap.Path);

        Assert.Equal(SampleMap.BodyUncompressed, body.LongLength);
    }

    private static byte[] BuildSyntheticContainer(bool bodyCompressed)
    {
        var bytes = new List<byte>();
        bytes.AddRange("GBX"u8.ToArray());
        AddInt16(bytes, 6);
        bytes.Add((byte)'B');
        bytes.Add((byte)'U');
        bytes.Add((byte)(bodyCompressed ? 'C' : 'U'));
        bytes.Add((byte)'R');
        AddUInt32(bytes, 0x03043000);
        AddUInt32(bytes, 25);
        AddInt32(bytes, 2);
        AddUInt32(bytes, 0x03043002);
        AddUInt32(bytes, 3);
        AddUInt32(bytes, 0x12345678);
        AddUInt32(bytes, 0x80000002);
        bytes.AddRange([1, 2, 3, 4, 5]);
        AddInt32(bytes, 688);
        AddInt32(bytes, 0);
        if (bodyCompressed)
        {
            AddInt32(bytes, 9);
            AddInt32(bytes, 4);
        }

        bytes.AddRange([0xDE, 0xAD, 0xBE, 0xEF]);
        return [.. bytes];
    }

    private static void AssertHeaderChunk(
        GbxSizeTree.Model.HeaderChunkInfo chunk,
        uint id,
        long bytes,
        bool heavy)
    {
        Assert.Equal(id, chunk.ChunkId);
        Assert.Equal(bytes, chunk.Bytes);
        Assert.Equal(heavy, chunk.Heavy);
    }

    private static void AddInt16(List<byte> destination, short value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(short)];
        BinaryPrimitives.WriteInt16LittleEndian(bytes, value);
        destination.AddRange(bytes.ToArray());
    }

    private static void AddInt32(List<byte> destination, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        destination.AddRange(bytes.ToArray());
    }

    private static void AddUInt32(List<byte> destination, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        destination.AddRange(bytes.ToArray());
    }
}
