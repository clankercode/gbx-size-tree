using System.Buffers.Binary;
using GbxSizeTree.Container;
using GbxSizeTree.Tests.Fixtures;

namespace GbxSizeTree.Tests.Container;

public sealed class ThumbnailChunkReaderTests
{
    [Fact]
    public void Read_EmptyPayload_ReturnsNull()
    {
        Assert.Null(ThumbnailChunkReader.Read(ReadOnlyMemory<byte>.Empty));
    }

    [Fact]
    public void Read_SyntheticPayload_ExtractsJpegDimensionsCommentAndMetadata()
    {
        var jpeg = BuildJpeg(width: 48, height: 32);
        var payload = BuildThumbnailPayload(jpeg, "abc"u8);

        var thumbnail = Assert.IsType<GbxSizeTree.Model.ThumbnailInfo>(ThumbnailChunkReader.Read(payload));
        var extracted = ThumbnailChunkReader.GetJpeg(payload);

        Assert.Equal(payload.Length, thumbnail.ChunkBytes);
        Assert.Equal(jpeg.Length, thumbnail.JpegBytes);
        Assert.Equal(48, thumbnail.Width);
        Assert.Equal(32, thumbnail.Height);
        Assert.Equal(3, thumbnail.CommentLength);
        Assert.Equal(11, thumbnail.StrippableMetadataBytes);
        Assert.True(extracted.Span.SequenceEqual(jpeg));
    }

    [Fact]
    public void Read_TruncatedPayload_ThrowsClearException()
    {
        var payload = BuildThumbnailPayload(BuildJpeg(48, 32), ReadOnlySpan<byte>.Empty);

        var error = Assert.Throws<InvalidDataException>(
            () => ThumbnailChunkReader.Read(payload.AsMemory(0, payload.Length - 1)));

        Assert.Contains("Truncated thumbnail chunk", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0xC0)]
    [InlineData(0xC1)]
    [InlineData(0xC2)]
    public void JpegProbe_ReadsDimensionsFromSupportedSofMarkers(int marker)
    {
        var jpeg = BuildJpeg(width: 48, height: 32, sofMarker: checked((byte)marker));

        var result = JpegProbe.Read(jpeg);

        Assert.Equal(48, result.Width);
        Assert.Equal(32, result.Height);
        Assert.Equal(11, result.StrippableMetadataBytes);
    }

    [Fact]
    public void JpegProbe_SkipsStuffedAndRestartMarkersInScanData()
    {
        var jpeg = BuildJpeg(width: 48, height: 32).ToList();
        jpeg.RemoveRange(jpeg.Count - 2, 2);
        jpeg.AddRange([0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00]);
        jpeg.AddRange([0x11, 0xFF, 0x00, 0xE0, 0x22, 0xFF, 0xD0, 0x33, 0xFF, 0xD9]);

        var result = JpegProbe.Read(jpeg.ToArray());

        Assert.Equal(48, result.Width);
        Assert.Equal(32, result.Height);
        Assert.Equal(11, result.StrippableMetadataBytes);
    }

    [Fact]
    public void Read_SampleThumbnail_ReturnsVerifiedAccountingAndActualDimensions()
    {
        SampleMap.SkipUnlessAvailable();
        var file = File.ReadAllBytes(SampleMap.Path);
        var layout = GbxContainerReader.Read(file);
        var chunk = Assert.Single(layout.HeaderChunks, value => value.ChunkId == 0x03043007);
        var payload = file.AsMemory(checked((int)chunk.FileOffset), checked((int)chunk.Bytes));

        var thumbnail = Assert.IsType<GbxSizeTree.Model.ThumbnailInfo>(ThumbnailChunkReader.Read(payload));

        Assert.Equal(58_889, thumbnail.ChunkBytes);
        Assert.Equal(58_825, thumbnail.JpegBytes);
        Assert.Equal(1_024, thumbnail.Width);
        Assert.Equal(1_024, thumbnail.Height);
        Assert.Equal(0, thumbnail.CommentLength);
        Assert.Equal(
            58_889,
            4 + 4 + (15 + 16 + 10 + 11) + thumbnail.JpegBytes + 4 + thumbnail.CommentLength);
    }

    private static byte[] BuildJpeg(int width, int height, byte sofMarker = 0xC2)
    {
        var bytes = new List<byte>
        {
            0xFF, 0xD8,
            0xFF, 0xE0, 0x00, 0x04, 0x12, 0x34,
            0xFF, 0xFE, 0x00, 0x03, 0x41,
            0xFF, sofMarker, 0x00, 0x08, 0x08,
            (byte)(height >> 8), (byte)height,
            (byte)(width >> 8), (byte)width,
            0x01,
            0xFF, 0xD9,
        };
        return [.. bytes];
    }

    private static byte[] BuildThumbnailPayload(ReadOnlySpan<byte> jpeg, ReadOnlySpan<byte> comment)
    {
        var bytes = new List<byte>();
        AddInt32(bytes, 1);
        AddInt32(bytes, jpeg.Length);
        bytes.AddRange("<Thumbnail.jpg>"u8.ToArray());
        bytes.AddRange(jpeg.ToArray());
        bytes.AddRange("</Thumbnail.jpg>"u8.ToArray());
        bytes.AddRange("<Comments>"u8.ToArray());
        AddInt32(bytes, comment.Length);
        bytes.AddRange(comment.ToArray());
        bytes.AddRange("</Comments>"u8.ToArray());
        return [.. bytes];
    }

    private static void AddInt32(List<byte> destination, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        destination.AddRange(bytes.ToArray());
    }
}
