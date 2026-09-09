using System.IO.Compression;
using GbxSizeTree.Semantics;

namespace GbxSizeTree.Cli.Modes;

internal static class EmbeddedPropertyReport
{
    public static IReadOnlyList<EmbeddedPropertyEntryDiff> Capture(
        byte[]? leftZipData,
        byte[]? rightZipData,
        IReadOnlyList<ValueChange<EmbeddedSnapshot>> embeddedChanges)
    {
        var modified = embeddedChanges
            .Where(change => change.Left is not null && change.Right is not null
                && !string.Equals(change.Left.Sha256, change.Right.Sha256, StringComparison.Ordinal))
            .OrderBy(change => change.Left!.Path, StringComparer.Ordinal)
            .ToArray();
        if (modified.Length == 0)
        {
            return [];
        }

        if (leftZipData is null || rightZipData is null)
        {
            return modified.Select(change => Unavailable(
                change.Left!,
                change.Right!,
                "Embedded ZIP data is unavailable for bounded property comparison.")).ToArray();
        }

        try
        {
            using var leftArchive = Open(leftZipData);
            using var rightArchive = Open(rightZipData);
            var leftEntries = EntriesByPath(leftArchive);
            var rightEntries = EntriesByPath(rightArchive);
            return modified.Select(change => CompareEntry(change.Left!, change.Right!, leftEntries, rightEntries)).ToArray();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
        {
            return modified.Select(change => Unavailable(
                change.Left!,
                change.Right!,
                $"Embedded ZIP property comparison failed ({ex.GetType().Name}).")).ToArray();
        }
    }

    private static EmbeddedPropertyEntryDiff CompareEntry(
        EmbeddedSnapshot left,
        EmbeddedSnapshot right,
        IReadOnlyDictionary<string, ZipArchiveEntry> leftEntries,
        IReadOnlyDictionary<string, ZipArchiveEntry> rightEntries)
    {
        if (!leftEntries.TryGetValue(left.Path, out var leftEntry)
            || !rightEntries.TryGetValue(right.Path, out var rightEntry))
        {
            return Unavailable(left, right, "The modified ZIP entry could not be reopened by its original full path.");
        }

        try
        {
            var properties = EmbeddedPropertySnapshot.Compare(
                EmbeddedPropertySnapshot.Capture(leftEntry),
                EmbeddedPropertySnapshot.Capture(rightEntry));
            return new(left.Path, left.Sha256, right.Sha256, properties);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
        {
            return Unavailable(left, right, $"ZIP entry property comparison failed ({ex.GetType().Name}).");
        }
    }

    private static EmbeddedPropertyEntryDiff Unavailable(
        EmbeddedSnapshot left,
        EmbeddedSnapshot right,
        string message)
    {
        var leftIssue = new EmbeddedPropertyIssue("unavailable", "Input", message);
        var rightIssue = new EmbeddedPropertyIssue("unavailable", "Input", message);
        return new(left.Path, left.Sha256, right.Sha256,
            new EmbeddedPropertyDiff([], true, [leftIssue], [rightIssue]));
    }

    private static ZipArchive Open(byte[] bytes) =>
        new(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);

    private static IReadOnlyDictionary<string, ZipArchiveEntry> EntriesByPath(ZipArchive archive)
    {
        var result = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            if (!result.TryAdd(entry.FullName, entry))
            {
                throw new InvalidDataException($"Duplicate embedded ZIP path: {entry.FullName}");
            }
        }
        return result;
    }
}
