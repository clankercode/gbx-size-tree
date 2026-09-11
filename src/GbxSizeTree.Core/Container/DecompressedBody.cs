using GBX.NET;
using GBX.NET.LZO;

namespace GbxSizeTree.Container;

/// <summary>
/// Extracts an uncompressed GBX body through the streaming API specified in
/// <c>docs/FORMAT-NOTES.md</c>, without parsing nodes.
/// </summary>
public static class DecompressedBody
{
    /// <summary>Decompressed file buffer with the offset where its body begins; the body is not copied.</summary>
    internal readonly record struct DecompressedFile(byte[] Bytes, int BodyOffset)
    {
        internal ReadOnlySpan<byte> Body => Bytes.AsSpan(BodyOffset);
        internal ReadOnlyMemory<byte> BodyMemory => Bytes.AsMemory(BodyOffset);
        internal int BodyLength => Bytes.Length - BodyOffset;
    }

    /// <summary>Decompresses the GBX at <paramref name="path"/> and returns only its body bytes.</summary>
    public static byte[] GetBody(string path)
    {
        Gbx.LZO = new Lzo();
        using var output = new MemoryStream();
        Gbx.Decompress(path, output);
        var bytes = output.GetBuffer();
        var length = checked((int)output.Length);
        var bodyOffset = BodyOffset(bytes, length);
        return bytes.AsSpan(bodyOffset, length - bodyOffset).ToArray();
    }

    /// <summary>Decompresses in-memory GBX bytes and returns only their body bytes.</summary>
    public static byte[] GetBody(byte[] fileBytes) => GetDecompressedFile(fileBytes).Body.ToArray();

    internal static DecompressedFile GetDecompressedFile(byte[] fileBytes)
    {
        ArgumentNullException.ThrowIfNull(fileBytes);
        Gbx.LZO = new Lzo();
        using var input = new MemoryStream(fileBytes, writable: false);
        // Pre-size to the declared decompressed length so the stream never doubles its buffer.
        var layout = GbxContainerReader.Read(fileBytes);
        var capacity = layout.BodyUncompressedSize is { } size ? checked((int)(layout.BodyOffset + size)) : 0;
        using var output = capacity > 0 ? new MemoryStream(capacity) : new MemoryStream();
        Gbx.Decompress(input, output);
        // Exact-capacity streams expose the decompressed file directly; otherwise copy once.
        var buffer = output.GetBuffer();
        var length = checked((int)output.Length);
        var bytes = buffer.Length == length ? buffer : buffer.AsSpan(0, length).ToArray();
        return new(bytes, BodyOffset(bytes, length));
    }

    private static int BodyOffset(byte[] decompressedFile, int length)
    {
        var layout = GbxContainerReader.Read(decompressedFile.AsSpan(0, length));
        if (layout.BodyCompressed)
        {
            throw new InvalidDataException("GBX.NET returned a container whose body is still compressed.");
        }

        return checked((int)layout.BodyOffset);
    }
}
