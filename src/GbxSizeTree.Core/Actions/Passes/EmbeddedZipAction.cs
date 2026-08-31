using System.IO.Compression;
using System.Security.Cryptography;
using GBX.NET;
using GbxSizeTree.Model;

namespace GbxSizeTree.Actions.Passes;

/// <summary>
/// Rebuilds the embedded-items ZIP using the optimization documented in
/// <c>docs/FORMAT-NOTES.md</c>, including decompression of compressed inner GBX bodies.
/// </summary>
public sealed class EmbeddedZipAction : IMapAction
{
    private static readonly DateTimeOffset FixedEntryTime =
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private CacheEntry? cache;

    public string Id => "embed-zip";
    public string Title => "Optimize embedded items ZIP";
    public ActionTier Tier => ActionTier.Lossless;
    public string Consequence => "";
    public string HowToManually => "";
    public bool DefaultOn => true;
    public int Order => 20;

    public ActionApplicability Detect(ActionDetectContext ctx)
    {
        if (ctx.Analysis.Body?.EmbeddedZip is not { Entries.Count: > 0 })
        {
            return ActionApplicability.No("no embedded item data");
        }

        if (ctx.Map is null)
        {
            return new ActionApplicability(
                Applies: true,
                EstimatedSavingsBytes: 0,
                Kind: EstimateKind.Heuristic,
                Reason: "a measured estimate requires the parsed map");
        }

        if (ctx.Map.EmbeddedZipData is not { Length: > 0 } source)
        {
            return ActionApplicability.No("no embedded item data");
        }

        var rebuilt = Rebuild(source, IncludeStoredVariant(ctx.Settings));
        cache = new CacheEntry(SourceKey.Create(source), IncludeStoredVariant(ctx.Settings), rebuilt);
        var savings = Math.Max(0, source.LongLength - rebuilt.Bytes.LongLength);

        return new ActionApplicability(
            Applies: savings > 0,
            EstimatedSavingsBytes: savings,
            Kind: EstimateKind.Measured,
            Reason: savings > 0 ? "embedded ZIP can be recompressed smaller" : "already optimal",
            Detail: BuildDetail(rebuilt, source.LongLength));
    }

    public ActionResult Apply(ActionApplyContext ctx)
    {
        if (ctx.Map.EmbeddedZipData is not { Length: > 0 } source)
        {
            return ActionResult.NoChange("no embedded item data");
        }

        var includeStored = IncludeStoredVariant(ctx.Settings);
        var key = SourceKey.Create(source);
        var rebuilt = cache is { } cached && cached.Key == key && cached.IncludeStored == includeStored
            ? cached.Result
            : Rebuild(source, includeStored);

        cache = new CacheEntry(key, includeStored, rebuilt);
        if (rebuilt.Bytes.LongLength >= source.LongLength)
        {
            return ActionResult.NoChange("embedded ZIP is already optimal");
        }

        ctx.Map.EmbeddedZipData = rebuilt.Bytes;
        return new ActionResult(
            Changed: true,
            Summary: $"optimized embedded ZIP by {source.LongLength - rebuilt.Bytes.LongLength:N0} bytes",
            Notes:
            [
                $"entries: {rebuilt.EntryCount}",
                $"inner GBX bodies decompressed: {rebuilt.DecompressedBodyCount}",
                $"bytes: {source.LongLength:N0} -> {rebuilt.Bytes.LongLength:N0}",
                $"winner: {rebuilt.Winner}",
            ]);
    }

    public IEnumerable<ValidationIssue> ValidateResult(MapFacts before, MapFacts after)
    {
        if (after.EmbeddedEntryCount != before.EmbeddedEntryCount)
        {
            yield return new ValidationIssue(
                "embed-zip.entry-count",
                $"expected {before.EmbeddedEntryCount} embedded entries, but found {after.EmbeddedEntryCount}");
        }
    }

    private static RebuildResult Rebuild(byte[] source, bool includeStoredVariant)
    {
        var entries = ReadEntries(source, out var decompressedBodyCount);
        var deflated = WriteZip(entries, CompressionLevel.SmallestSize);
        if (!includeStoredVariant)
        {
            return new RebuildResult(deflated, entries.Count, decompressedBodyCount, "deflate");
        }

        var stored = WriteZip(entries, CompressionLevel.NoCompression);
        return stored.LongLength < deflated.LongLength
            ? new RebuildResult(stored, entries.Count, decompressedBodyCount, "stored")
            : new RebuildResult(deflated, entries.Count, decompressedBodyCount, "deflate");
    }

    private static List<ZipEntryData> ReadEntries(byte[] source, out int decompressedBodyCount)
    {
        var entries = new List<ZipEntryData>();
        decompressedBodyCount = 0;

        using var input = new MemoryStream(source, writable: false);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            using var entryStream = entry.Open();
            using var dataStream = new MemoryStream();
            entryStream.CopyTo(dataStream);
            var data = dataStream.ToArray();

            if (entry.FullName.EndsWith(".gbx", StringComparison.OrdinalIgnoreCase)
                && HasCompressedGbxBody(data))
            {
                using var compressed = new MemoryStream(data, writable: false);
                using var decompressed = new MemoryStream();
                Gbx.Decompress(compressed, decompressed);
                data = decompressed.ToArray();
                decompressedBodyCount++;
            }

            entries.Add(new ZipEntryData(entry.FullName, data));
        }

        return entries;
    }

    private static byte[] WriteZip(IReadOnlyList<ZipEntryData> entries, CompressionLevel level)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in entries)
            {
                var entry = archive.CreateEntry(item.FullName, level);
                entry.LastWriteTime = FixedEntryTime;
                using var entryStream = entry.Open();
                entryStream.Write(item.Bytes);
            }
        }

        return output.ToArray();
    }

    private static bool HasCompressedGbxBody(ReadOnlySpan<byte> data) =>
        data.Length > 7
        && data[0] == 'G'
        && data[1] == 'B'
        && data[2] == 'X'
        && data[7] == 'C';

    private static bool IncludeStoredVariant(IReadOnlyDictionary<string, string> settings) =>
        settings.TryGetValue("stored", out var value)
        && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static string BuildDetail(RebuildResult result, long beforeBytes) =>
        $"{result.EntryCount} entries; {result.DecompressedBodyCount} inner GBX bodies decompressed; "
        + $"{beforeBytes:N0} -> {result.Bytes.LongLength:N0} bytes; {result.Winner} winner";

    private sealed record ZipEntryData(string FullName, byte[] Bytes);
    private sealed record RebuildResult(byte[] Bytes, int EntryCount, int DecompressedBodyCount, string Winner);
    private sealed record CacheEntry(SourceKey Key, bool IncludeStored, RebuildResult Result);

    private readonly record struct SourceKey(int Length, string Sha256)
    {
        public static SourceKey Create(byte[] source) =>
            new(source.Length, Convert.ToHexString(SHA256.HashData(source)));
    }
}
