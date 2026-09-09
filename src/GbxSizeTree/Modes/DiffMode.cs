using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using GBX.NET;
using GBX.NET.Engines.Game;
using GbxSizeTree.Cli;
using GbxSizeTree.Container;
using GbxSizeTree.Semantics;

namespace GbxSizeTree.Cli.Modes;

public static class DiffMode
{
    [UnconditionalSuppressMessage("Trimming", "IL2026")]
    public static int Run(IReadOnlyList<string> paths, CliOutputFormat format, bool all, bool? colorOption)
    {
        if (paths.Count != 2) throw new ArgumentException("diff requires exactly two map paths.");
        var left = Read(paths[0]);
        var right = Read(paths[1]);
        var result = Compare(left, right, all);
        if (format == CliOutputFormat.Json)
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        else if (format == CliOutputFormat.Html)
            Console.WriteLine(DiffRenderer.RenderHtml(ToReport(result), paths[0], paths[1]));
        else if (format == CliOutputFormat.Markdown)
            Console.WriteLine(DiffRenderer.RenderMarkdown(ToReport(result), paths[0], paths[1]));
        else
            DiffRenderer.Render(DiffRenderer.BuildConsole(colorOption), ToReport(result), paths[0], paths[1]);
        return ExitCodes.Ok;
    }

    private static Snapshot Read(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("map not found", path);
        var bytes = File.ReadAllBytes(path);
        Gbx.LZO ??= new GBX.NET.LZO.Lzo();
        Gbx.ZLib ??= new GBX.NET.ZLib.ZLib();
        using var stream = new MemoryStream(bytes, writable: false);
        var map = Gbx.Parse<CGameCtnChallenge>(stream).Node;
        var layout = GbxContainerReader.Read(bytes);
        var chunks = layout.HeaderChunks.ToDictionary(c => $"header:{c.ChunkId:X8}", c => c.Bytes);
        var body = DecompressedBody.GetBody(bytes);
        foreach (var region in new SkippableChunkScanner().Scan(body).Regions)
            chunks[$"body:{region.ChunkId:X8}"] = region.Length;
        var blocks = map.Blocks?.Select(CreateBlockSnapshot).OrderBy(Locality).ThenBy(x => x.Key, StringComparer.Ordinal).ToArray() ?? [];
        var baked = map.BakedBlocks?.Select(CreateBlockSnapshot).OrderBy(Locality).ThenBy(x => x.Key, StringComparer.Ordinal).ToArray() ?? [];
        var items = map.AnchoredObjects?.Select(CreateItemSnapshot).OrderBy(Locality).ThenBy(x => x.Key, StringComparer.Ordinal).ToArray() ?? [];
        var embeds = ReadEmbeds(map);
        return new Snapshot(bytes.Length, chunks, blocks, baked, items, embeds,
            map.MapUid ?? "", map.MapName ?? "", map.AuthorLogin ?? "", map.AuthorNickname ?? "", map.Password is not null);
    }

    private static DiffResult Compare(Snapshot a, Snapshot b, bool all) => new(
        a.FileBytes, b.FileBytes, SetDiff(a.Blocks.Select(x => x.Key), b.Blocks.Select(x => x.Key)),
        all ? SetDiff(a.BakedBlocks.Select(x => x.Key), b.BakedBlocks.Select(x => x.Key)) : [],
        SetDiff(a.Items.Select(x => x.Key), b.Items.Select(x => x.Key)), SetDiff(a.Embedded, b.Embedded), all ? SetDiff(a.Chunks, b.Chunks) : [],
        all ? a.BakedBlocks : [], all ? b.BakedBlocks : [], a.Blocks, b.Blocks, a.Items, b.Items,
        ToEmbedSnapshots(a.Embedded), ToEmbedSnapshots(b.Embedded),
        a.MapUid != b.MapUid ? new Change(a.MapUid, b.MapUid) : null,
        a.MapName != b.MapName ? new Change(a.MapName, b.MapName) : null,
        a.AuthorLogin != b.AuthorLogin ? new Change(a.AuthorLogin, b.AuthorLogin) : null,
        a.AuthorNickname != b.AuthorNickname ? new Change(a.AuthorNickname, b.AuthorNickname) : null,
        a.Password != b.Password ? new Change(a.Password.ToString(), b.Password.ToString()) : null);

    private static Dictionary<string, string> ReadEmbeds(CGameCtnChallenge map)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (map.EmbeddedZipData is not { Length: > 0 }) return result;
        using var archive = map.OpenReadEmbeddedZipData();
        foreach (var entry in archive.Entries)
        {
            using var input = entry.Open(); using var sha = SHA256.Create();
            var hash = Convert.ToHexString(sha.ComputeHash(input));
            var ratio = entry.Length == 0 ? 0 : (double)entry.CompressedLength / entry.Length;
            result[entry.FullName] = $"{hash}|compressed={entry.CompressedLength}|uncompressed={entry.Length}|ratio={ratio:0.####}";
        }
        return result;
    }

    private static BlockSnapshot CreateBlockSnapshot(CGameCtnBlock block) => new(BlockKey(block), block.Coord.ToString());
    private static ItemSnapshot CreateItemSnapshot(CGameCtnAnchoredObject item) => new(ItemKey(item), item.AbsolutePositionInMap.ToString());
    private static int Locality(BlockSnapshot x) => LocalityKey(x.Coord);
    private static int Locality(ItemSnapshot x) => LocalityKey(x.Position);
    private static int LocalityKey(string value)
    {
        var n = Regex.Matches(value, "-?\\d+").Cast<Match>().Select(x => int.Parse(x.Value)).Take(3).ToArray();
        var x = n.ElementAtOrDefault(0); var y = n.ElementAtOrDefault(1); var z = n.ElementAtOrDefault(2);
        return (x & 0x3ff) | ((z & 0x3ff) << 10) ^ ((y & 0x3ff) << 20);
    }
    private static string BlockKey(CGameCtnBlock block) => $"{block.Name}|coord={block.Coord}|dir={block.Direction}|variant={block.Variant}|subvariant={block.SubVariant}";
    private static string ItemKey(CGameCtnAnchoredObject item) => $"{item.ItemModel?.Id ?? "<unknown>"}|position={item.AbsolutePositionInMap}|rotation={item.YawPitchRoll}";
    private static IReadOnlyList<Change> SetDiff(IEnumerable<string> a, IEnumerable<string> b) => a.Except(b).Select(x => new Change(x, null)).Concat(b.Except(a).Select(x => new Change(null, x))).ToArray();
    private static IReadOnlyList<Change> SetDiff(IReadOnlyDictionary<string, long> a, IReadOnlyDictionary<string, long> b) => a.Keys.Union(b.Keys).Where(k => !a.TryGetValue(k, out var av) || !b.TryGetValue(k, out var bv) || av != bv).Select(k => new Change(a.TryGetValue(k, out var av) ? $"{av} bytes" : null, b.TryGetValue(k, out var bv) ? $"{bv} bytes" : null, k)).ToArray();
    private static IReadOnlyList<Change> SetDiff(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b) => a.Keys.Union(b.Keys).Where(k => !a.TryGetValue(k, out var av) || !b.TryGetValue(k, out var bv) || av != bv).Select(k => new Change(a.TryGetValue(k, out var av) ? av : null, b.TryGetValue(k, out var bv) ? bv : null, k)).ToArray();
    private static IReadOnlyList<EmbedSnapshot> ToEmbedSnapshots(IReadOnlyDictionary<string,string> embeds) => embeds.Select(x => { var p=x.Value.Split('|'); return new EmbedSnapshot(x.Key,p[0],p.ElementAtOrDefault(1),p.ElementAtOrDefault(2),p.ElementAtOrDefault(3)); }).ToArray();

    private static DiffReport ToReport(DiffResult result) => new(result.LeftBytes, result.RightBytes, result.Blocks.Select(c => new Change(c.Left,c.Right,c.Key)).ToArray(), result.BakedBlocks.Select(c => new Change(c.Left,c.Right,c.Key)).ToArray(), result.Items.Select(c => new Change(c.Left,c.Right,c.Key)).ToArray(), result.Embedded.Select(c => new Change(c.Left,c.Right,c.Key)).ToArray(), result.Chunks.Select(c => new Change(c.Left,c.Right,c.Key)).ToArray(), ToExternal(result.MapUid),ToExternal(result.MapName),ToExternal(result.AuthorLogin),ToExternal(result.AuthorNickname),ToExternal(result.Password));
    private static Change? ToExternal(Change? c) => c is null ? null : new(c.Left,c.Right,c.Key);
    private sealed record Snapshot(long FileBytes,IReadOnlyDictionary<string,long> Chunks,BlockSnapshot[] Blocks,BlockSnapshot[] BakedBlocks,ItemSnapshot[] Items,IReadOnlyDictionary<string,string> Embedded,string MapUid,string MapName,string AuthorLogin,string AuthorNickname,bool Password);
    private sealed record BlockSnapshot(string Key,string Coord); private sealed record ItemSnapshot(string Key,string Position); private sealed record EmbedSnapshot(string Path,string Sha256,string? Compressed,string? Uncompressed,string? Ratio);
    private sealed record DiffResult(long LeftBytes,long RightBytes,IReadOnlyList<global::GbxSizeTree.Cli.Modes.Change> Blocks,IReadOnlyList<global::GbxSizeTree.Cli.Modes.Change> BakedBlocks,IReadOnlyList<global::GbxSizeTree.Cli.Modes.Change> Items,IReadOnlyList<global::GbxSizeTree.Cli.Modes.Change> Embedded,IReadOnlyList<global::GbxSizeTree.Cli.Modes.Change> Chunks,BlockSnapshot[] LeftBakedSnapshots,BlockSnapshot[] RightBakedSnapshots,BlockSnapshot[] LeftBlockSnapshots,BlockSnapshot[] RightBlockSnapshots,ItemSnapshot[] LeftItemSnapshots,ItemSnapshot[] RightItemSnapshots,IReadOnlyList<EmbedSnapshot> LeftEmbeddedSnapshots,IReadOnlyList<EmbedSnapshot> RightEmbeddedSnapshots,global::GbxSizeTree.Cli.Modes.Change? MapUid,global::GbxSizeTree.Cli.Modes.Change? MapName,global::GbxSizeTree.Cli.Modes.Change? AuthorLogin,global::GbxSizeTree.Cli.Modes.Change? AuthorNickname,global::GbxSizeTree.Cli.Modes.Change? Password);
}
