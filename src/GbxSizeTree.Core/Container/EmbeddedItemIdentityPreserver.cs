using System.IO.Compression;
using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.GameData;
using GBX.NET.Serialization;
using GBX.NET.Serialization.Chunking;

namespace GbxSizeTree.Container;

/// <summary>
/// Preserves the ordered identity-to-ZIP-entry mapping in map chunk 0x03043054.
/// GBX.NET 2.4.4 otherwise rebuilds this list from ZIP paths during every save,
/// which breaks maps whose placed item identities intentionally use aliases.
/// </summary>
internal static class EmbeddedItemIdentityPreserver
{
    private const uint ChunkId = 0x03043054;

    internal sealed record Snapshot(
        int Version,
        IReadOnlyList<EntryIdentity> Entries,
        List<string>? Textures)
    {
        public static Snapshot Empty { get; } = new(0, [], null);
    }

    internal sealed record EntryIdentity(string Path, Ident Model);

    public static Snapshot Capture(byte[] sourceBytes)
    {
        ArgumentNullException.ThrowIfNull(sourceBytes);

        var body = DecompressedBody.GetBody(sourceBytes);
        var region = new SkippableChunkScanner().Scan(body).Regions.SingleOrDefault(
            static region => region.Kind == Model.RawChunkKind.Skippable && region.ChunkId == ChunkId);
        if (region is null)
        {
            return Snapshot.Empty;
        }

        var payload = body.AsSpan(
            checked((int)region.PayloadOffset),
            checked((int)region.PayloadLength)).ToArray();
        using var payloadStream = new MemoryStream(payload, writable: false);
        using var reader = new GbxReader(payloadStream);
        var version = reader.ReadInt32();
        Ident[] expectedModels = [];
        byte[] embeddedZipData = [];
        List<string>? textures = null;

        reader.ReadEncapsulated(inner =>
        {
            expectedModels = inner.ReadArrayIdent();
            embeddedZipData = inner.ReadData();
            if (version >= 1)
            {
                textures = inner.ReadListString();
            }
        });

        var itemPaths = ReadItemModelPaths(embeddedZipData);
        if (itemPaths.Count != expectedModels.Length)
        {
            throw new InvalidDataException(
                $"Embedded item identity map has {expectedModels.Length} identities but " +
                $"{itemPaths.Count} readable item model entries.");
        }

        return new Snapshot(
            version,
            itemPaths.Select((path, index) => new EntryIdentity(path, expectedModels[index])).ToArray(),
            textures);
    }

    /// <summary>
    /// Projects the original ordered mapping onto the final ZIP and makes GBX.NET write the
    /// resulting chunk payload verbatim. Existing actions may recompress or delete entries;
    /// adding or renaming item entries is deliberately rejected because no original alias exists.
    /// </summary>
    public static IReadOnlyList<Ident> PrepareForSave(CGameCtnChallenge map, Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Entries.Count == 0)
        {
            return [];
        }

        var currentZipData = map.EmbeddedZipData ?? [];
        var currentPaths = ReadItemModelPaths(currentZipData);
        var originalByPath = snapshot.Entries
            .GroupBy(static entry => entry.Path, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => new Queue<Ident>(group.Select(static entry => entry.Model)),
                StringComparer.Ordinal);
        var projectedModels = new List<Ident>(currentPaths.Count);

        foreach (var path in currentPaths)
        {
            if (!originalByPath.TryGetValue(path, out var models) || models.Count == 0)
            {
                throw new InvalidDataException(
                    $"Cannot preserve embedded item identity for added or renamed ZIP entry '{path}'.");
            }
            projectedModels.Add(models.Dequeue());
        }

        var chunk = map.GetChunk<CGameCtnChallenge.Chunk03043054>()
            ?? throw new InvalidDataException("Map has embedded item data but chunk 0x03043054 is missing.");
        ((ISkippableChunk)chunk).Data = BuildPayload(
            snapshot.Version,
            projectedModels,
            currentZipData,
            snapshot.Textures);

        return projectedModels;
    }

    public static IReadOnlyList<Ident?> AlignExpectedModels(
        IReadOnlyList<ZipArchiveEntry> entries,
        IReadOnlyList<Ident>? expectedModels)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (expectedModels is null || expectedModels.Count == 0)
        {
            return Enumerable.Repeat<Ident?>(null, entries.Count).ToArray();
        }

        var result = new Ident?[entries.Count];
        var modelIndex = 0;
        for (var entryIndex = 0; entryIndex < entries.Count; entryIndex++)
        {
            if (!IsItemModel(entries[entryIndex]))
            {
                continue;
            }

            if (modelIndex >= expectedModels.Count)
            {
                return Enumerable.Repeat<Ident?>(null, entries.Count).ToArray();
            }
            result[entryIndex] = expectedModels[modelIndex++];
        }

        return modelIndex == expectedModels.Count
            ? result
            : Enumerable.Repeat<Ident?>(null, entries.Count).ToArray();
    }

    private static List<string> ReadItemModelPaths(byte[] zipData)
    {
        if (zipData.Length == 0)
        {
            return [];
        }

        using var stream = new MemoryStream(zipData, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        return archive.Entries.Where(IsItemModel).Select(static entry => entry.FullName).ToList();
    }

    private static bool IsItemModel(ZipArchiveEntry entry)
    {
        try
        {
            using var stream = entry.Open();
            return Gbx.ParseHeaderNode(stream) is CGameItemModel { Ident: not null };
        }
        catch
        {
            // Match GBX.NET's chunk writer: unreadable/non-model ZIP entries do not consume
            // an identity-list slot.
            return false;
        }
    }

    private static byte[] BuildPayload(
        int version,
        IReadOnlyList<Ident> models,
        byte[] zipData,
        List<string>? textures)
    {
        using var stream = new MemoryStream();
        using var writer = new GbxWriter(stream);
        writer.Write(version);
        writer.WriteEncapsulated(inner =>
        {
            inner.WriteList(models.ToList());
            inner.WriteData(zipData);
            if (version >= 1)
            {
                inner.WriteList(textures);
            }
        });
        return stream.ToArray();
    }
}
