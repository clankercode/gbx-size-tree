using System.Globalization;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.Script;
using GBX.NET.Serialization;
using GBX.NET.Serialization.Chunking;
using GbxSizeTree.Cli.Modes;
using Spectre.Console.Testing;
using static GBX.NET.Engines.Script.CScriptTraitsMetadata;

namespace GbxSizeTree.Tests.Cli;

public sealed class EmbeddedMetadataSnapshotTests
{
    [Fact]
    public void Capture_AllTraitsNotJustFirstTwenty_AndSameNameValueEdits()
    {
        var map = new CGameCtnChallenge { ScriptMetadata = new() };
        for (var i = 0; i < 45; i++) map.ScriptMetadata.Declare($"key{i:D2}", i);
        var before = MapMetadataSnapshot.Capture(map);
        Assert.Equal(44, before.Values["script.traits/key44/value"]!.Integer);
        map.ScriptMetadata.Declare("key44", 999);
        var change = Assert.Single(MapMetadataSnapshot.Compare(before, MapMetadataSnapshot.Capture(map)));
        Assert.Equal("script.traits/key44/value", change.Path);
        Assert.Equal(999, change.Right!.Integer);
    }

    [Fact]
    public void Capture_RecursiveArraysStructsAndEmptyTypes()
    {
        var map = new CGameCtnChallenge { ScriptMetadata = new() };
        map.ScriptMetadata.Declare("nested", CreateStruct("Config").WithInteger("number", 17).Build());
        map.ScriptMetadata.Declare("array", new[] { 1, 2, 3 });
        map.ScriptMetadata.Declare("empty", Array.Empty<float>());
        var values = MapMetadataSnapshot.Capture(map).Values;
        Assert.Equal("Config", values["script.traits/nested/type/name"]!.Text);
        Assert.Equal(17, values["script.traits/nested/members/number/value"]!.Integer);
        Assert.Equal(3, values["script.traits/array/items/2/value"]!.Integer);
        Assert.Equal("Real", values["script.traits/empty/type/value/type"]!.Text);
        Assert.Equal(0, values["script.traits/empty/count"]!.Integer);
    }

    [Fact]
    public void Capture_DictionariesAreInsertionOrderIndependentAndRetainKeys()
    {
        var left = new CGameCtnChallenge { ScriptMetadata = new() };
        var right = new CGameCtnChallenge { ScriptMetadata = new() };
        left.ScriptMetadata.Declare("dict", new Dictionary<string, int> { ["z"] = 1, ["a"] = 2 });
        right.ScriptMetadata.Declare("dict", new Dictionary<string, int> { ["a"] = 2, ["z"] = 1 });
        Assert.Empty(MapMetadataSnapshot.Compare(MapMetadataSnapshot.Capture(left), MapMetadataSnapshot.Capture(right)));
        Assert.Contains(MapMetadataSnapshot.Capture(left).Values, p => p.Key.EndsWith("/key/value") && p.Value?.Text == "a");
    }

    [Fact]
    public void Capture_ArbitraryLabelsAndValuesAreUnambiguousAndSafeAcrossSharedSurfaces()
    {
        var left = new CGameCtnChallenge { ScriptMetadata = new() };
        var right = new CGameCtnChallenge { ScriptMetadata = new() };
        right.ScriptMetadata.Declare("a/b~<script>|\n\u001b", "<script>alert('x')</script>|\n\u001b[31m");
        right.ScriptMetadata.Declare("a~1b", true);
        var report = DiffMode.CompareMaps(left, right);
        Assert.Contains(report.MetadataChanges, c => c.Path == "script.traits/a~1b~0<script>|\n\u001b/value");
        Assert.Contains(report.MetadataChanges, c => c.Path == "script.traits/a~01b/value" && c.Right!.Boolean == true);
        using var json = JsonDocument.Parse(DiffMode.RenderJson(report));
        Assert.Contains(json.RootElement.GetProperty("MetadataChanges").EnumerateArray(), c => c.GetProperty("Path").GetString()!.Contains("~1b"));
        var html = DiffRenderer.RenderHtml(report, "old", "new");
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("script.traits", DiffRenderer.RenderMarkdown(report, "old", "new"));
        var console = new TestConsole();
        DiffRenderer.Render(console, report, "old", "new");
        Assert.DoesNotContain("\u001b[31m", console.Output);
    }

    [Fact]
    public void Capture_RealAndVectorComponentsKeepRoundTripPrecisionAndSpecialBits()
    {
        var map = new CGameCtnChallenge { ScriptMetadata = new() };
        var value = float.BitIncrement(1f);
        map.ScriptMetadata.Declare("real", value);
        map.ScriptMetadata.Declare("vec", new Vec3(value, -0f, float.PositiveInfinity));
        map.ScriptMetadata.Declare("nan", BitConverter.Int32BitsToSingle(unchecked((int)0x7FC01234)));
        var values = MapMetadataSnapshot.Capture(map).Values;
        Assert.Equal(value.ToString("R", CultureInfo.InvariantCulture), values["script.traits/real/value"]!.Text);
        Assert.Equal("-0", values["script.traits/vec/value/y"]!.Text);
        Assert.Equal("Infinity", values["script.traits/vec/value/z"]!.Text);
        Assert.Equal(0x7FC01234, values["script.traits/nan/value/bits"]!.Integer);
    }

    [Fact]
    public void Capture_CyclesDepthAndOversizeAreExplicitlyUnavailable()
    {
        var type = new ScriptArrayType(new ScriptType(EScriptType.Void), new ScriptType(EScriptType.Array));
        var cycle = new ScriptArrayTrait(type, new List<ScriptTrait>());
        cycle.Value.Add(cycle);
        var map = new CGameCtnChallenge { ScriptMetadata = new() };
        map.ScriptMetadata.Traits["cycle"] = cycle;
        map.ScriptMetadata.Declare("large", Enumerable.Range(0, 10000));
        map.ScriptMetadata.Declare("text", new string('x', 65537));
        map.ScriptMetadata.Traits["unknown"] = new ScriptTrait<long>(new ScriptType(EScriptType.Class), 1);
        var values = MapMetadataSnapshot.Capture(map).Values;
        Assert.Contains(values, p => p.Key.Contains("cycle") && p.Value?.Text == "unavailable: cycle");
        Assert.Contains(values, p => p.Key.Contains("large") && p.Value?.Text == "unavailable: collection limit (4096)");
        Assert.Contains(values, p => p.Key.Contains("text") && p.Value?.Text == "unavailable: text limit (65536)");
        Assert.Contains(values, p => p.Key.Contains("unknown") && p.Value?.Text == "unavailable: unsupported trait");
        ScriptTrait deep = new ScriptTrait<int>(new ScriptType(EScriptType.Integer), 1);
        for (var i = 0; i < 50; i++) deep = new ScriptArrayTrait(type, new List<ScriptTrait> { deep });
        map.ScriptMetadata.Traits.Clear();
        map.ScriptMetadata.Traits["deep"] = deep;
        Assert.Contains(MapMetadataSnapshot.Capture(map).Values, p => p.Value?.Text == "unavailable: depth limit (32)");
    }

    [Fact]
    public void Capture_OpaqueMetadataIsUnavailableRatherThanAbsentOrStale()
    {
        var map = new CGameCtnChallenge { ScriptMetadata = new() };
        map.ScriptMetadata.Declare("stale", 7);
        ((ISkippableChunk)map.Chunks.Create<CGameCtnChallenge.Chunk03043044>()).Data = [255];
        ((ISkippableChunk)map.Chunks.Create<CGameCtnChallenge.Chunk03043054>()).Data = [255];
        var values = MapMetadataSnapshot.Capture(map).Values;
        Assert.Equal("unavailable: opaque chunk 03043044", values["script.traits/status"]!.Text);
        Assert.Equal("unavailable: opaque chunk 03043054", values["embedded.textures/status"]!.Text);
        Assert.Equal("unavailable: opaque chunk 03043054", values["embedded.identities/status"]!.Text);
        Assert.DoesNotContain(values.Keys, k => k.Contains("stale"));
        map.Chunks.Remove(0x03043054);
        var self = DiffMode.CompareMaps(map, map);
        Assert.Empty(self.MetadataChanges);
        Assert.Contains(self.Warnings, w => w.Contains("opaque chunk 03043044"));
    }

    [Fact]
    public void Capture_ExpandedPathsConsumeBudget()
    {
        var map = new CGameCtnChallenge { ScriptMetadata = new() };
        map.ScriptMetadata.Declare(new string('x', 8192), Enumerable.Range(0, 4096));
        var values = MapMetadataSnapshot.Capture(map).Values;
        Assert.True(values.Keys.Sum(k => (long)k.Length) < 3 * 1024 * 1024);
        Assert.Contains(values, p => p.Value?.Text?.Contains("path budget") == true);
    }

    [Fact]
    public void Capture_GlobalNodeAndTextBudgetsAreBoundedAndExplicit()
    {
        var map = new CGameCtnChallenge { ScriptMetadata = new() };
        for (var i = 0; i < 10; i++) map.ScriptMetadata.Declare($"array{i}", Enumerable.Range(0, 4096));
        var values = MapMetadataSnapshot.Capture(map).Values;
        Assert.Contains(values, p => p.Value?.Text?.Contains("budget") == true || p.Value?.Text?.Contains("node limit") == true);
        Assert.True(values.Count < 40000);
        map.ScriptMetadata.Traits.Clear();
        for (var i = 0; i < 100; i++) map.ScriptMetadata.Declare($"text{i:D3}", new string('x', 65536));
        Assert.Contains(MapMetadataSnapshot.Capture(map).Values, p => p.Value?.Text?.Contains("budget") == true);
    }

    [Fact]
    public void Capture_OriginalSerializedIdentitiesAndTexturesWithoutOpeningZip()
    {
        Gbx.LZO = new GBX.NET.LZO.Lzo();
        var map = new CGameCtnChallenge();
        var chunk = map.Chunks.Create<CGameCtnChallenge.Chunk03043054>();
        using var payload = new MemoryStream();
        using (var writer = new GbxWriter(payload))
        {
            writer.Write(1);
            writer.WriteEncapsulated(w =>
            {
                w.WriteList(new List<Ident> { new("original/alias.Item.Gbx", "Stadium", "maker") });
                w.WriteData(new byte[] { 255 });
                w.WriteList(new List<string> { "original/texture.dds" });
            });
        }
        ((ISkippableChunk)chunk).Data = payload.ToArray();
        using var serialized = new MemoryStream();
        new Gbx<CGameCtnChallenge>(map).Save(serialized);
        serialized.Position = 0;
        var parsed = Gbx.Parse<CGameCtnChallenge>(serialized).Node;
        var values = MapMetadataSnapshot.Capture(parsed).Values;
        Assert.Equal("original/alias.Item.Gbx", values["embedded.identities/0/id"]!.Text);
        Assert.Equal("maker", values["embedded.identities/0/author"]!.Text);
        Assert.Equal("Stadium", values["embedded.identities/0/collection"]!.Text);
        Assert.Equal("original/texture.dds", values["embedded.textures/0"]!.Text);
    }
}
