using System.Buffers.Binary;
using GbxSizeTree.Container;

namespace GbxSizeTree.Tests.Container;

public sealed class WebpProbeTests
{
    [Fact]
    public void TryReadDimensions_Vp8X_ReadsCanvasSize()
    {
        var payload = new byte[10];
        WriteUInt24(payload.AsSpan(4, 3), 639);
        WriteUInt24(payload.AsSpan(7, 3), 359);

        Assert.Equal((640, 360), WebpProbe.TryReadDimensions(BuildWebp("VP8X"u8, payload)));
    }

    [Fact]
    public void TryReadDimensions_Vp8_ReadsFrameSize()
    {
        var payload = new byte[10];
        payload[3] = 0x9D;
        payload[4] = 0x01;
        payload[5] = 0x2A;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6, 2), 1024);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8, 2), 512);

        Assert.Equal((1024, 512), WebpProbe.TryReadDimensions(BuildWebp("VP8 "u8, payload)));
    }

    [Fact]
    public void TryReadDimensions_Vp8L_ReadsFrameSize()
    {
        var payload = new byte[5];
        payload[0] = 0x2F;
        var bits = 255U | (127U << 14);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(1, 4), bits);

        Assert.Equal((256, 128), WebpProbe.TryReadDimensions(BuildWebp("VP8L"u8, payload)));
    }

    [Fact]
    public void TryReadDimensions_InvalidOrTruncatedData_ReturnsNull()
    {
        Assert.Null(WebpProbe.TryReadDimensions([]));
        Assert.Null(WebpProbe.TryReadDimensions("RIFF"u8));
        Assert.Null(WebpProbe.TryReadDimensions(
            [0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50]));
    }

    [Fact]
    public void TryReadDimensions_InvalidRiffSizeOrVp8LVersion_ReturnsNull()
    {
        var vp8X = BuildWebp("VP8X"u8, new byte[10]);
        BinaryPrimitives.WriteUInt32LittleEndian(vp8X.AsSpan(4, 4), 0);

        var vp8LPayload = new byte[5];
        vp8LPayload[0] = 0x2F;
        BinaryPrimitives.WriteUInt32LittleEndian(vp8LPayload.AsSpan(1, 4), 1U << 29);

        Assert.Null(WebpProbe.TryReadDimensions(vp8X));
        Assert.Null(WebpProbe.TryReadDimensions(BuildWebp("VP8L"u8, vp8LPayload)));
    }

    private static byte[] BuildWebp(ReadOnlySpan<byte> chunkType, ReadOnlySpan<byte> payload)
    {
        var paddedLength = payload.Length + (payload.Length & 1);
        var bytes = new byte[20 + paddedLength];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), (uint)(bytes.Length - 8));
        "WEBP"u8.CopyTo(bytes.AsSpan(8, 4));
        chunkType.CopyTo(bytes.AsSpan(12, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), (uint)payload.Length);
        payload.CopyTo(bytes.AsSpan(20));
        return bytes;
    }

    private static void WriteUInt24(Span<byte> destination, int value)
    {
        destination[0] = (byte)value;
        destination[1] = (byte)(value >> 8);
        destination[2] = (byte)(value >> 16);
    }
}
