using System.IO.Compression;
using GBX.NET;
using GBX.NET.Engines.Game;
using GbxSizeTree.Abstractions;
using GbxSizeTree.Container;
using GbxSizeTree.Model;

namespace GbxSizeTree.Drilldown;

/// <summary>
/// Inspects the embedded-items ZIP and the GBX container prefix documented in
/// <c>docs/FORMAT-NOTES.md</c> without decompressing an embedded GBX body.
/// </summary>
public sealed class EmbeddedZipDrilldown : IEmbeddedZipDrilldown
{
    public EmbeddedZipInfo? Inspect(CGameCtnChallenge map)
    {
        ArgumentNullException.ThrowIfNull(map);

        if (map.EmbeddedZipData is not { Length: > 0 } zipData)
        {
            return null;
        }

        var referencedIdents = CollectReferencedIdents(map);
        var referenceSet = new HashSet<string>(referencedIdents, StringComparer.OrdinalIgnoreCase);
        var entries = new List<EmbeddedEntryInfo>();
        long entriesUncompressedBytes = 0;
        var vertexCountBudget = EmbeddedItemVertexCounter.TotalLimit;

        using var archive = map.OpenReadEmbeddedZipData();
        var expectedModels = EmbeddedItemIdentityPreserver.AlignExpectedModels(
            archive.Entries, map.ExpectedEmbeddedItemModels);
        for (var entryIndex = 0; entryIndex < archive.Entries.Count; entryIndex++)
        {
            var entry = archive.Entries[entryIndex];
            var isGbx = entry.FullName.EndsWith(".gbx", StringComparison.OrdinalIgnoreCase);
            var isItem = entry.FullName.EndsWith(".Item.Gbx", StringComparison.OrdinalIgnoreCase);
            EmbeddedItemVertexCounter.Result? vertices = null;
            if (isItem
                && entry.Length <= EmbeddedItemVertexCounter.EntryLimit
                && entry.Length <= vertexCountBudget)
            {
                vertexCountBudget -= entry.Length;
                vertices = EmbeddedItemVertexCounter.TryCount(entry);
            }
            entriesUncompressedBytes += entry.Length;
            entries.Add(new EmbeddedEntryInfo(
                Path: entry.FullName,
                CompressedBytes: entry.CompressedLength,
                UncompressedBytes: entry.Length,
                Method: entry.CompressedLength == entry.Length ? "Stored" : "Deflate",
                IsGbx: isGbx,
                HasCompressedGbxBody: isGbx && HasCompressedGbxBody(entry),
                RecompressibleSavingsEstimate: isGbx ? null : 0,
                IsReferenced: IsReferenced(entry.FullName, referenceSet, expectedModels[entryIndex]),
                VertexCount: vertices?.Count,
                VertexCountEstimated: vertices?.Estimated ?? false));
        }

        return new EmbeddedZipInfo(
            ZipBytes: zipData.LongLength,
            EntriesUncompressedBytes: entriesUncompressedBytes,
            Entries: entries,
            ReferencedIdents: referencedIdents);
    }

    private static IReadOnlyList<string> CollectReferencedIdents(CGameCtnChallenge map)
    {
        var idents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (map.AnchoredObjects is not null)
        {
            foreach (var item in map.AnchoredObjects)
            {
                AddIdent(idents, item.ItemModel.Id);
            }
        }

        if (map.Blocks is not null)
        {
            foreach (var block in map.Blocks)
            {
                AddIdent(idents, block.Name);
            }
        }

        var bakedBlocks = map.GetBakedBlocks();
        if (bakedBlocks is not null)
        {
            foreach (var block in bakedBlocks)
            {
                AddIdent(idents, block.Name);
            }
        }

        return idents.Order(StringComparer.Ordinal).ToArray();
    }

    private static void AddIdent(HashSet<string> idents, string? ident)
    {
        if (!string.IsNullOrWhiteSpace(ident))
        {
            idents.Add(ident);
        }
    }

    private static bool IsReferenced(
        string path,
        HashSet<string> referencedIdents,
        Ident? expectedModel)
    {
        if (expectedModel is { } model && referencedIdents.Contains(model.Id))
        {
            return true;
        }

        var fileName = Path.GetFileName(path);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(stem))
        {
            return true;
        }

        // Placed custom items reference embeds by zip-relative path WITHOUT the leading
        // "Items/"/"Blocks/" segment, with backslashes: "MySet\gate.Item.Gbx"
        // (verified on the sample; docs/FORMAT-NOTES.md). Try path forms first, stems last.
        var normalized = path.Replace('\\', '/').TrimStart('/');
        var slash = normalized.IndexOf('/');
        var withoutRoot = slash > 0 ? normalized[(slash + 1)..] : normalized;
        foreach (var candidate in (ReadOnlySpan<string>)
            [normalized, normalized.Replace('/', '\\'), withoutRoot, withoutRoot.Replace('/', '\\')])
        {
            if (referencedIdents.Contains(candidate))
            {
                return true;
            }
        }

        if (referencedIdents.Contains(stem))
        {
            return true;
        }

        var secondStem = Path.GetFileNameWithoutExtension(stem);
        return !string.Equals(stem, secondStem, StringComparison.Ordinal)
            && referencedIdents.Contains(secondStem);
    }

    private static bool HasCompressedGbxBody(ZipArchiveEntry entry)
    {
        Span<byte> prefix = stackalloc byte[16];
        using var stream = entry.Open();
        var bytesRead = 0;
        while (bytesRead < prefix.Length)
        {
            var read = stream.Read(prefix[bytesRead..]);
            if (read == 0)
            {
                break;
            }

            bytesRead += read;
        }

        // docs/FORMAT-NOTES.md: GBX magic is bytes 0..2 and body compression is byte 7.
        return bytesRead >= 8
            && prefix[0] == (byte)'G'
            && prefix[1] == (byte)'B'
            && prefix[2] == (byte)'X'
            && prefix[7] == (byte)'C';
    }

}
