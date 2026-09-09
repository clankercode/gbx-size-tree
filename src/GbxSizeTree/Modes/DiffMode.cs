using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using GBX.NET;
using GBX.NET.Engines.Game;
using GbxSizeTree.Analysis;
using GbxSizeTree.Container;
using GbxSizeTree.Semantics;

namespace GbxSizeTree.Cli.Modes;

public static class DiffMode
{
    [UnconditionalSuppressMessage("Trimming", "IL2026")]
    public static int Run(IReadOnlyList<string> paths, bool json, bool all, bool? colorOption)
    {
        if (paths.Count != 2) throw new ArgumentException("diff requires exactly two map paths.");
        var left = Read(paths[0]);
        var right = Read(paths[1]);
        var result = Compare(left, right, all);
        if (json) Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        else DiffRenderer.Render(DiffRenderer.BuildConsole(colorOption), ToReport(result), paths[0], paths[1]);
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
        var blocks = map.Blocks?.Select(BlockKey).ToArray() ?? [];
        var baked = map.BakedBlocks?.Select(BlockKey).ToArray() ?? [];
        var items = map.AnchoredObjects?.Select(ItemKey).ToArray() ?? [];
        var embeds = ReadEmbeds(map);
        return new Snapshot(bytes.Length, chunks, blocks, baked, items, embeds,
            map.MapUid ?? "", map.MapName ?? "", map.AuthorLogin ?? "", map.AuthorNickname ?? "", map.Password is not null);
    }

    private static DiffResult Compare(Snapshot a, Snapshot b, bool all) => new(
        a.FileBytes, b.FileBytes, SetDiff(a.Blocks, b.Blocks), SetDiff(a.BakedBlocks, b.BakedBlocks),
        SetDiff(a.Items, b.Items), SetDiff(a.Embedded.Keys, b.Embedded.Keys), all ? SetDiff(a.Chunks, b.Chunks) : [],
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
            using var input = entry.Open();
            using var sha = SHA256.Create();
            result[entry.FullName] = Convert.ToHexString(sha.ComputeHash(input));
        }
        return result;
    }

    private static string BlockKey(GBX.NET.Engines.Game.CGameCtnBlock block) =>
        $"{block.Name}|coord={block.Coord}|dir={block.Direction}|variant={block.Variant}|subvariant={block.SubVariant}";

    private static string ItemKey(GBX.NET.Engines.Game.CGameCtnAnchoredObject item) =>
        $"{item.ItemModel?.Id ?? "<unknown>"}|position={item.AbsolutePositionInMap}|rotation={item.YawPitchRoll}";
    private static IReadOnlyList<Change> SetDiff(IEnumerable<string> a, IEnumerable<string> b) =>
        a.Except(b).Select(x => new Change(x, null)).Concat(b.Except(a).Select(x => new Change(null, x))).ToArray();
    private static IReadOnlyList<Change> SetDiff(IReadOnlyDictionary<string, long> a, IReadOnlyDictionary<string, long> b) =>
        a.Keys.Union(b.Keys).Where(k => !a.TryGetValue(k, out var av) || !b.TryGetValue(k, out var bv) || av != bv)
            .Select(k => new Change(a.TryGetValue(k, out var av) ? $"{av} bytes" : null, b.TryGetValue(k, out var bv) ? $"{bv} bytes" : null, k)).ToArray();

    private static DiffReport ToReport(DiffResult result) => new(result.LeftBytes, result.RightBytes,
        result.Blocks.Select(c => new global::GbxSizeTree.Cli.Modes.Change(c.Left, c.Right, c.Key)).ToArray(),
        result.BakedBlocks.Select(c => new global::GbxSizeTree.Cli.Modes.Change(c.Left, c.Right, c.Key)).ToArray(),
        result.Items.Select(c => new global::GbxSizeTree.Cli.Modes.Change(c.Left, c.Right, c.Key)).ToArray(),
        result.Embedded.Select(c => new global::GbxSizeTree.Cli.Modes.Change(c.Left, c.Right, c.Key)).ToArray(),
        result.Chunks.Select(c => new global::GbxSizeTree.Cli.Modes.Change(c.Left, c.Right, c.Key)).ToArray(),
        ToExternal(result.MapUid), ToExternal(result.MapName), ToExternal(result.AuthorLogin), ToExternal(result.AuthorNickname), ToExternal(result.Password));

    private static global::GbxSizeTree.Cli.Modes.Change? ToExternal(Change? change) =>
        change is null ? null : new(change.Left, change.Right, change.Key);

    private sealed record Snapshot(long FileBytes, IReadOnlyDictionary<string,long> Chunks, string[] Blocks, string[] BakedBlocks, string[] Items, IReadOnlyDictionary<string,string> Embedded, string MapUid, string MapName, string AuthorLogin, string AuthorNickname, bool Password);
    private sealed record DiffResult(long LeftBytes, long RightBytes, IReadOnlyList<Change> Blocks, IReadOnlyList<Change> BakedBlocks, IReadOnlyList<Change> Items, IReadOnlyList<Change> Embedded, IReadOnlyList<Change> Chunks, Change? MapUid, Change? MapName, Change? AuthorLogin, Change? AuthorNickname, Change? Password);
    private sealed record Change(string? Left, string? Right, string? Key = null);
}

