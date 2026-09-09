using System.Security.Cryptography;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.Game;

namespace GbxSizeTree.Cli.Modes;

public static class DiffMode
{
    public static int Run(IReadOnlyList<string> paths, CliOutputFormat format, bool all, bool? colorOption)
    {
        if (paths.Count != 2) throw new ArgumentException("diff requires exactly two map paths.");
        var report = CompareFiles(paths[0], paths[1], all);
        if (format == CliOutputFormat.Json)
            Console.WriteLine(RenderJson(report));
        else if (format == CliOutputFormat.Html)
            Console.WriteLine(DiffRenderer.RenderHtml(report, paths[0], paths[1]));
        else if (format == CliOutputFormat.Markdown)
            Console.WriteLine(DiffRenderer.RenderMarkdown(report, paths[0], paths[1]));
        else
            DiffRenderer.Render(DiffRenderer.BuildConsole(colorOption), report, paths[0], paths[1]);
        return ExitCodes.Ok;
    }

    // Keep the JSON envelope and string-valued changes compatible; snapshots carry typed detail.
    public static string RenderJson(DiffReport report) => JsonSerializer.Serialize(new DiffJsonReport(
        report.LeftBytes, report.RightBytes,
        Legacy(report.Blocks, x => x.Key),
        Legacy(report.BakedBlocks, x => x.Key),
        Legacy(report.Items, x => x.Key),
        report.Embedded.Select(c => new Change(c.Left?.ToValue(), c.Right?.ToValue(), (c.Right ?? c.Left)?.Path)),
        report.Embedded,
        report.Chunks,
        report.LeftBakedSnapshots, report.RightBakedSnapshots,
        report.LeftBlockSnapshots, report.RightBlockSnapshots,
        report.LeftItemSnapshots, report.RightItemSnapshots,
        report.LeftEmbeddedSnapshots, report.RightEmbeddedSnapshots,
        report.MapUid, report.MapName, report.AuthorLogin, report.AuthorNickname, report.Password
    ), DiffJsonContext.Default.DiffJsonReport);

    private static IEnumerable<Change> Legacy<T>(IEnumerable<ValueChange<T>> changes, Func<T, string> format) where T : class =>
        changes.Select(c => new Change(c.Left is null ? null : format(c.Left), c.Right is null ? null : format(c.Right)));

    /// <summary>Compares parsed map placements and, with all, original serialized content.</summary>
    public static DiffReport CompareFiles(string left, string right, bool all = false)
    {
        if (!all) return Compare(Read(left, false), Read(right, false), false);
        var leftBytes = File.ReadAllBytes(left);
        var rightBytes = File.ReadAllBytes(right);
        var leftContent = MapContentComparison.Capture(leftBytes);
        var rightContent = MapContentComparison.Capture(rightBytes);
        try
        {
            return Compare(ReadBytes(leftBytes), ReadBytes(rightBytes), true);
        }
        catch (Exception) when (IsMapParseFailure())
        {
            return ContentOnlyReport(leftBytes.Length, rightBytes.Length, leftContent, rightContent);
        }

        bool IsMapParseFailure() => true;
    }

    private static Snapshot ReadBytes(byte[] bytes)
    {
        Gbx.LZO = new GBX.NET.LZO.Lzo();
        Gbx.ZLib = new GBX.NET.ZLib.ZLib();
        using var stream = new MemoryStream(bytes, writable: false);
        var map = Gbx.Parse<CGameCtnChallenge>(stream).Node;
        return Capture(map, bytes.Length, MapContentComparison.Capture(bytes));
    }

    private static DiffReport ContentOnlyReport(long leftBytes, long rightBytes,
        IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
    {
        var chunks = MapContentComparison.Compare(left, right).ToList();
        chunks.Insert(0, new Change(
            "GBX.NET map parse failed; semantic fields unavailable",
            "GBX.NET map parse failed; semantic fields unavailable",
            "content-only-fallback"));
        return new DiffReport(leftBytes, rightBytes, [], [], [], [], chunks, null, null, null, null, null);
    }

    private static Snapshot Read(string path, bool all)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("map not found", path);
        var bytes = File.ReadAllBytes(path);
        Gbx.LZO = new GBX.NET.LZO.Lzo();
        Gbx.ZLib = new GBX.NET.ZLib.ZLib();
        using var stream = new MemoryStream(bytes, writable: false);
        var map = Gbx.Parse<CGameCtnChallenge>(stream).Node;
        return Capture(map, bytes.Length, all ? MapContentComparison.Capture(bytes) : new Dictionary<string, string>());
    }

    /// <summary>
    /// Compares semantic placements; all also compares the chunks serialized by GBX.NET.
    /// In-memory objects have no original container bytes, and properties without chunks are not serialized.
    /// Use MapContentComparison.Compare for original bytes, including data GBX.NET does not preserve.
    /// </summary>
    public static DiffReport CompareMaps(CGameCtnChallenge left, CGameCtnChallenge right, bool all = false) =>
        Compare(Capture(left, 0, SerializedContent(left, all)), Capture(right, 0, SerializedContent(right, all)), all);

    private static IReadOnlyDictionary<string, string> SerializedContent(CGameCtnChallenge map, bool all)
    {
        if (!all) return new Dictionary<string, string>();
        Gbx.LZO = new GBX.NET.LZO.Lzo();
        Gbx.ZLib = new GBX.NET.ZLib.ZLib();
        using var stream = new MemoryStream();
        new Gbx<CGameCtnChallenge>(map).Save(stream);
        return MapContentComparison.Capture(stream.ToArray());
    }

    private static Snapshot Capture(CGameCtnChallenge map, long fileBytes, IReadOnlyDictionary<string, string> chunks) => new(
        fileBytes, chunks,
        map.Blocks?.Select(BlockSnapshot.From).OrderBy(x => x.PhysicalPosition).ThenBy(x => x.Key, StringComparer.Ordinal).ToArray() ?? [],
        map.BakedBlocks?.Select(BlockSnapshot.From).OrderBy(x => x.PhysicalPosition).ThenBy(x => x.Key, StringComparer.Ordinal).ToArray() ?? [],
        map.AnchoredObjects?.Select(ItemSnapshot.From).OrderBy(x => x.PhysicalPosition).ThenBy(x => x.Key, StringComparer.Ordinal).ToArray() ?? [],
        ReadEmbeds(map), map.MapUid ?? "", map.MapName ?? "", map.AuthorLogin ?? "", map.AuthorNickname ?? "", map.Password is not null);

    private static DiffReport Compare(Snapshot a, Snapshot b, bool all) => new(
        a.FileBytes, b.FileBytes, InstanceDiff(a.Blocks, b.Blocks, x => x.PhysicalPosition, x => x.Key),
        all ? InstanceDiff(a.BakedBlocks, b.BakedBlocks, x => x.PhysicalPosition, x => x.Key) : [],
        InstanceDiff(a.Items, b.Items, x => x.PhysicalPosition, x => x.Key), EmbeddedDiff(a.Embedded, b.Embedded), all ? MapContentComparison.Compare(a.Chunks, b.Chunks) : [],
        Different(a.MapUid, b.MapUid), Different(a.MapName, b.MapName),
        Different(a.AuthorLogin, b.AuthorLogin), Different(a.AuthorNickname, b.AuthorNickname),
        Different(a.Password.ToString(), b.Password.ToString()))
    {
        LeftBakedSnapshots = all ? a.BakedBlocks : [], RightBakedSnapshots = all ? b.BakedBlocks : [],
        LeftBlockSnapshots = a.Blocks, RightBlockSnapshots = b.Blocks,
        LeftItemSnapshots = a.Items, RightItemSnapshots = b.Items,
        LeftEmbeddedSnapshots = a.Embedded.Values.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray(),
        RightEmbeddedSnapshots = b.Embedded.Values.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray(),
    };

    private static Change? Different(string a, string b) => a == b ? null : new(a, b);

    private static Dictionary<string, EmbeddedSnapshot> ReadEmbeds(CGameCtnChallenge map)
    {
        var result = new Dictionary<string, EmbeddedSnapshot>(StringComparer.Ordinal);
        if (map.EmbeddedZipData is not { Length: > 0 }) return result;
        using var archive = map.OpenReadEmbeddedZipData();
        foreach (var entry in archive.Entries)
        {
            using var input = entry.Open();
            var snapshot = new EmbeddedSnapshot(entry.FullName, Convert.ToHexString(SHA256.HashData(input)), entry.CompressedLength, entry.Length);
            if (!result.TryAdd(entry.FullName, snapshot))
                throw new InvalidDataException($"Duplicate embedded ZIP path: {entry.FullName}");
        }
        return result;
    }

    private static IReadOnlyList<ValueChange<T>> InstanceDiff<T>(IEnumerable<T> a, IEnumerable<T> b,
        Func<T, SpatialPosition?> position, Func<T, string> key) where T : class
    {
        var left = a.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var right = b.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var changes = new List<ValueChange<T>>();
        foreach (var value in left.Keys.Union(right.Keys))
        {
            var delta = right.GetValueOrDefault(value) - left.GetValueOrDefault(value);
            for (var i = 0; i < Math.Abs(delta); i++)
                changes.Add(delta < 0 ? new(value, null) : new(null, value));
        }
        return changes.OrderBy(c => position((c.Right ?? c.Left)!))
            .ThenBy(c => key((c.Right ?? c.Left)!), StringComparer.Ordinal)
            .ThenBy(c => c.Left is null ? 1 : 0).ToArray();
    }

    private static IReadOnlyList<ValueChange<EmbeddedSnapshot>> EmbeddedDiff(
        IReadOnlyDictionary<string, EmbeddedSnapshot> a, IReadOnlyDictionary<string, EmbeddedSnapshot> b) =>
        a.Keys.Union(b.Keys, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal)
            .Where(k => !a.TryGetValue(k, out var av) || !b.TryGetValue(k, out var bv) || av != bv)
            .Select(k => new ValueChange<EmbeddedSnapshot>(a.GetValueOrDefault(k), b.GetValueOrDefault(k))).ToArray();

    private sealed record Snapshot(long FileBytes, IReadOnlyDictionary<string, string> Chunks,
        BlockSnapshot[] Blocks, BlockSnapshot[] BakedBlocks, ItemSnapshot[] Items,
        IReadOnlyDictionary<string, EmbeddedSnapshot> Embedded,
        string MapUid, string MapName, string AuthorLogin, string AuthorNickname, bool Password);
}
