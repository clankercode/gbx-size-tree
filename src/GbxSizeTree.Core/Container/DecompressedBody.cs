using GBX.NET;
using GBX.NET.LZO;

namespace GbxSizeTree.Container;

/// <summary>
/// Extracts an uncompressed GBX body through the streaming API specified in
/// <c>docs/FORMAT-NOTES.md</c>, without parsing nodes.
/// </summary>
public static class DecompressedBody
{
    /// <summary>Decompresses the GBX at <paramref name="path"/> and returns only its body bytes.</summary>
    public static byte[] GetBody(string path)
    {
        Gbx.LZO = new Lzo();
        using var output = new MemoryStream();
        Gbx.Decompress(path, output);
        return ExtractBody(output.ToArray());
    }

    /// <summary>Decompresses in-memory GBX bytes and returns only their body bytes.</summary>
    public static byte[] GetBody(byte[] fileBytes)
    {
        ArgumentNullException.ThrowIfNull(fileBytes);
        Gbx.LZO = new Lzo();
        using var input = new MemoryStream(fileBytes, writable: false);
        using var output = new MemoryStream();
        Gbx.Decompress(input, output);
        return ExtractBody(output.ToArray());
    }

    private static byte[] ExtractBody(byte[] decompressedFile)
    {
        var layout = GbxContainerReader.Read(decompressedFile);
        if (layout.BodyCompressed)
        {
            throw new InvalidDataException("GBX.NET returned a container whose body is still compressed.");
        }

        return decompressedFile.AsSpan(checked((int)layout.BodyOffset)).ToArray();
    }
}
