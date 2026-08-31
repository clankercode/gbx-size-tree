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

        var currentZipData = map.EmbeddedZipData ?? [];
        var currentPaths = ReadItemModelPaths(currentZipData);
        if (snapshot.Entries.Count == 0)
        {
            if (currentPaths.Count > 0)
            {
                throw new InvalidDataException(
                    "Cannot preserve embedded item identities because item-model ZIP entries were added.");
            }
            return [];
        }

        var originalPaths = snapshot.Entries.Select(static entry => entry.Path).ToArray();
        if (currentPaths.SequenceEqual(originalPaths, StringComparer.Ordinal))
        {
            return WritePayload(map, snapshot, snapshot.Entries.Select(static entry => entry.Model).ToArray());
        }

        if (originalPaths.Distinct(StringComparer.Ordinal).Count() != originalPaths.Length)
        {
            throw new InvalidDataException(
                "Cannot safely change an embedded ZIP containing duplicate item-model paths.");
        }

        var originalByPath = snapshot.Entries
            .Select(static (entry, index) => (entry.Path, entry.Model, Index: index))
            .ToDictionary(static entry => entry.Path, StringComparer.Ordinal);
        var projectedModels = new List<Ident>(currentPaths.Count);
        var previousOriginalIndex = -1;

        foreach (var path in currentPaths)
        {
            if (!originalByPath.TryGetValue(path, out var original)
                || original.Index <= previousOriginalIndex)
            {
                throw new InvalidDataException(
                    $"Cannot preserve embedded item identity for added, renamed, or reordered ZIP entry '{path}'.");
            }
            projectedModels.Add(original.Model);
            previousOriginalIndex = original.Index;
        }

        return WritePayload(map, snapshot, projectedModels);
    }

    private static IReadOnlyList<Ident> WritePayload(
        CGameCtnChallenge map,
        Snapshot snapshot,
        IReadOnlyList<Ident> projectedModels)
    {
        var chunk = map.GetChunk<CGameCtnChallenge.Chunk03043054>()
            ?? throw new InvalidDataException("Map has embedded item data but chunk 0x03043054 is missing.");
        ((ISkippableChunk)chunk).Data = BuildPayload(
            snapshot.Version,
            projectedModels,
            map.EmbeddedZipData ?? [],
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
