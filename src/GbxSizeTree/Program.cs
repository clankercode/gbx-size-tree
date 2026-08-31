using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.LZO;
using GBX.NET.ZLib;

// P0 spike: prove parse + publish + native LZO under single-file on linux and wine.
Gbx.LZO = new Lzo();
Gbx.ZLib = new ZLib();

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: gbx-size-tree <file.Map.Gbx>");
    return 1;
}

var path = args[0];
var fileSize = new FileInfo(path).Length;
var gbx = Gbx.Parse<CGameCtnChallenge>(path);
var map = gbx.Node;

Console.WriteLine($"file: {path}");
Console.WriteLine($"file size: {fileSize:N0} B");
Console.WriteLine($"body: {gbx.Body.UncompressedSize:N0} -> {gbx.Body.CompressedSize:N0} ({gbx.Body.CompressionRatio:P1})");
Console.WriteLine($"blocks: {map.Blocks?.Count ?? 0:N0}, items: {map.AnchoredObjects?.Count ?? 0:N0}");
Console.WriteLine($"embedded zip: {map.EmbeddedZipData?.Length ?? 0:N0} B");
Console.WriteLine($"lightmap zlib: {map.LightmapCacheData?.Data.Length ?? 0:N0} B (uncompressed {map.LightmapCacheData?.UncompressedSize ?? 0:N0} B)");
Console.WriteLine($"thumbnail: {map.Thumbnail?.Length ?? 0:N0} B");
return 0;
