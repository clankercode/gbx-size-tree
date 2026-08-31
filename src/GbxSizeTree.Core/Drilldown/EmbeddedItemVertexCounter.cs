using System.Buffers.Binary;
using System.IO.Compression;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;

namespace GbxSizeTree.Drilldown;

/// <summary>Counts render vertices in an embedded item without making report generation fragile.</summary>
internal static class EmbeddedItemVertexCounter
{
    internal const long EntryLimit = 16 * 1024 * 1024;
    internal const long TotalLimit = 64 * 1024 * 1024;

    internal readonly record struct Result(int Count, bool Estimated);

    public static Result? TryCount(ZipArchiveEntry entry)
    {
        if (!entry.FullName.EndsWith(".Item.Gbx", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var bytes = TryRead(entry);
        if (bytes is null)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var parsed = Gbx.Parse<CGameItemModel>(stream,
                new GbxReadSettings { IgnoreExceptionsInBody = true });
            if (parsed.Body.Exception is null && TryCountRuntimeMesh(parsed.Node) is { } runtimeCount)
            {
                return new(runtimeCount, Estimated: false);
            }

            if (parsed.Body.Exception is null && TryCountCustomMesh(parsed.Node) is { } customCount)
            {
                return new(customCount, Estimated: false);
            }

            if (parsed.Body.Exception is null && TryCountEditorMesh(parsed.Node) is { } editorCount)
            {
                return new(editorCount, Estimated: false);
            }
        }
        catch
        {
            // Unsupported legacy items can still expose vertex-stream chunk headers.
        }

        return CountUncompressedVertexStreamChunks(bytes) is { } recovered
            ? new(recovered, Estimated: true)
            : null;
    }

    private static byte[]? TryRead(ZipArchiveEntry entry)
    {
        try
        {
            if (entry.Length is < 0 or > EntryLimit)
            {
                return null;
            }

            var bytes = GC.AllocateUninitializedArray<byte>((int)entry.Length);
            using var stream = entry.Open();
            stream.ReadExactly(bytes);
            return bytes;
        }
        catch
        {
            return null;
        }
    }

    private static int? TryCountRuntimeMesh(CGameItemModel item)
    {
        try
        {
            return CountSolidVertices(
                (item.EntityModel as CGameCommonItemEntityModel)?.StaticObject?.Mesh);
        }
        catch
        {
            return null;
        }
    }

    private static int? TryCountCustomMesh(CGameItemModel item)
    {
        try
        {
            return CountSolidVertices((item.VisModelCustom as CGameObjectVisModel)?.MeshShaded);
        }
        catch
        {
            return null;
        }
    }

    private static int? TryCountEditorMesh(CGameItemModel item)
    {
        try
        {
            return CountCrystalVertices(
                (item.EntityModelEdition as CGameCommonItemEntityModelEdition)?.MeshCrystal);
        }
        catch
        {
            return null;
        }
    }

    private static int? CountSolidVertices(CPlugSolid2Model? mesh)
    {
        if (mesh?.Visuals is not { Length: > 0 } visuals)
        {
            return null;
        }

        var counts = visuals
            .Where(visual => visual is not null)
            .SelectMany(visual => visual.VertexStreams)
            .Distinct<CPlugVertexStream>(ReferenceEqualityComparer.Instance)
            .Select(stream => stream.Positions?.Length
                ?? stream.Normals?.Length
                ?? stream.UVs.Values.FirstOrDefault()?.Length)
            .Where(count => count.HasValue)
            .Select(count => count!.Value)
            .ToList();

        return counts.Count == 0 ? null : checked(counts.Sum());
    }

    private static int? CountCrystalVertices(CPlugCrystal? mesh)
    {
        if (mesh is null)
        {
            return null;
        }

        var crystals = mesh.Layers
            .OfType<CPlugCrystal.GeometryLayer>()
            .Where(layer => layer.IsEnabled && layer.IsVisible && layer.Crystal is not null)
            .Select(layer => layer.Crystal!)
            .Distinct<CPlugCrystal.Crystal>(ReferenceEqualityComparer.Instance)
            .ToList();

        return crystals.Count == 0
            ? null
            : checked(crystals.Sum(crystal => crystal.Positions.Length));
    }

    /// <summary>
    /// Legacy items occasionally stop GBX.NET while reading a vertex stream. For uncompressed
    /// bodies, a new vertex-stream node has an index, class id, chunk id, version, and count in
    /// sequence. Validating that framing provides an explicitly estimated recovery count.
    /// </summary>
    private static int? CountUncompressedVertexStreamChunks(ReadOnlySpan<byte> gbx)
    {
        const uint vertexStreamChunkId = 0x09056000;
        if (gbx.Length < 12 || gbx[0] != 'G' || gbx[1] != 'B' || gbx[2] != 'X' || gbx[7] != 'U')
        {
            return null;
        }

        long total = 0;
        var found = false;
        for (var offset = 8; offset <= gbx.Length - 12; offset++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(gbx[offset..]) != vertexStreamChunkId)
            {
                continue;
            }

            var classId = BinaryPrimitives.ReadUInt32LittleEndian(gbx[(offset - 4)..]);
            var nodeIndex = BinaryPrimitives.ReadInt32LittleEndian(gbx[(offset - 8)..]);
            var version = BinaryPrimitives.ReadInt32LittleEndian(gbx[(offset + 4)..]);
            var count = BinaryPrimitives.ReadInt32LittleEndian(gbx[(offset + 8)..]);
            if (classId != vertexStreamChunkId
                || nodeIndex is <= 0 or > 1_000_000
                || version is < 0 or > 16
                || count is <= 0 or > 10_000_000)
            {
                continue;
            }

            total += count;
            if (total > int.MaxValue)
            {
                return null;
            }

            found = true;
            offset += 11;
        }

        return found ? (int)total : null;
    }
}
