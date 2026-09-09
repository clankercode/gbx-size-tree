using System.Security.Cryptography;
using GbxSizeTree.Container;
using GbxSizeTree.Model;

namespace GbxSizeTree.Cli.Modes;

/// <summary>Compares serialized GBX content without interpreting map nodes.</summary>
public static class MapContentComparison
{
    /// <summary>
    /// Full container and body hashes cover bytes that the diagnostic chunk scanner cannot identify.
    /// Unsupported container layouts or decompression failures throw rather than imply equality.
    /// </summary>
    public static IReadOnlyList<Change> Compare(byte[] left, byte[] right) => Compare(Capture(left), Capture(right));

    internal static IReadOnlyDictionary<string, string> Capture(byte[] bytes)
    {
        var layout = GbxContainerReader.Read(bytes);
        var bodyOffset = checked((int)layout.BodyOffset);
        var body = layout.BodyCompressed ? DecompressedBody.GetBody(bytes) : bytes.AsMemory(bodyOffset);
        var chunks = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["content:container"] = Describe(bytes.AsSpan(0, bodyOffset), "complete container prefix"),
            ["content:decompressed-body"] = Describe(body.Span, "complete decompressed body; chunk identification is not exhaustive"),
        };
        if (layout.BodyCompressed)
            chunks["content:stored-body"] = Describe(bytes.AsSpan(bodyOffset), "complete compressed body encoding");

        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var chunk in layout.HeaderChunks)
            Add($"header:{chunk.ChunkId:X8}", bytes.AsSpan(checked((int)chunk.FileOffset), checked((int)chunk.Bytes)),
                $"header payload; heavy={chunk.Heavy}");
        foreach (var region in new SkippableChunkScanner().Scan(body).Regions)
        {
            var detail = region.Kind == RawChunkKind.Terminator ? "terminator candidate"
                : $"{region.Kind} scanner candidate; identification not exhaustive";
            Add($"body:{region.ChunkId:X8}", body.Span.Slice(checked((int)region.PayloadOffset), checked((int)region.PayloadLength)),
                detail, region.Length);
        }
        return chunks;

        void Add(string key, ReadOnlySpan<byte> payload, string detail, long? regionBytes = null)
        {
            var occurrence = occurrences.GetValueOrDefault(key) + 1;
            occurrences[key] = occurrence;
            // The first occurrence keeps the legacy key, including when a duplicate is removed.
            var occurrenceKey = occurrence == 1 ? key : $"{key}#{occurrence}";
            chunks.Add(occurrenceKey, Describe(payload, detail, regionBytes));
        }
    }

    internal static IReadOnlyList<Change> Compare(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right) =>
        left.Keys.Union(right.Keys, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal)
            .Where(k => !left.TryGetValue(k, out var a) || !right.TryGetValue(k, out var b) || a != b)
            .Select(k => new Change(left.GetValueOrDefault(k), right.GetValueOrDefault(k), k)).ToArray();

    private static string Describe(ReadOnlySpan<byte> payload, string detail, long? regionBytes = null) =>
        FormattableString.Invariant($"{regionBytes ?? payload.Length} bytes; sha256={Convert.ToHexString(SHA256.HashData(payload))}; {detail}");
}
