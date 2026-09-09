using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.Serialization.Chunking;
using static GBX.NET.Engines.Script.CScriptTraitsMetadata;

namespace GbxSizeTree.Cli.Modes;

internal sealed class EmbeddedMetadataCapture
{
    private const int MaxCollection = 4096;
    private const int MaxDepth = 32;
    private const int MaxText = 65536;
    private readonly SortedDictionary<string, MapMetadataValue?> values;
    private readonly HashSet<object> active = new(ReferenceEqualityComparer.Instance);
    private int remainingNodes = 16384;
    private int remainingText = 2 * 1024 * 1024;
    private int remainingPaths = 2 * 1024 * 1024;

    private bool ReservePaths(string path, int rows = 8)
    {
        // Reserve suffixes and intermediate paths before expanding a subtree.
        var cost = (long)(path.Length + 128) * rows;
        if (cost <= remainingPaths) { remainingPaths -= (int)cost; return true; }
        remainingPaths = 0;
        Unavailable(path, "path budget (2097152)");
        return false;
    }

    private EmbeddedMetadataCapture(SortedDictionary<string, MapMetadataValue?> values) => this.values = values;

    public static void Capture(CGameCtnChallenge map, SortedDictionary<string, MapMetadataValue?> values)
    {
        var capture = new EmbeddedMetadataCapture(values);
        if (map.Chunks.Any(c => c.Id == 0x03043044 && c is ISkippableChunk { Data: not null }))
            capture.Unavailable("script.traits", "opaque chunk 03043044");
        else if (map.ScriptMetadata is { } script)
        {
            if (script.Chunks.Any(c => c is ISkippableChunk { Data: not null }))
                capture.Unavailable("script.traits", "opaque script metadata");
            else capture.Named("script.traits", script.Traits, 0);
        }
        if (map.Chunks.Any(c => c.Id == 0x03043054 && c is ISkippableChunk { Data: not null }))
        {
            capture.Unavailable("embedded.identities", "opaque chunk 03043054");
            capture.Unavailable("embedded.textures", "opaque chunk 03043054");
            return;
        }
        if (map.ExpectedEmbeddedItemModels is { } identities)
        {
            if (capture.Collection("embedded.identities", identities.Count))
                for (var i = 0; i < identities.Count; i++)
                {
                    var path = "embedded.identities/" + Index(i);
                    capture.Text(path + "/id", identities[i].Id);
                    capture.Text(path + "/collection", identities[i].Collection.ToString());
                    capture.Text(path + "/author", identities[i].Author);
                }
        }
        if (GetTextures(map) is { } textures && capture.Collection("embedded.textures", textures.Count))
            for (var i = 0; i < textures.Count; i++) capture.Text("embedded.textures/" + Index(i), textures[i]);
    }

    // GBX.NET 2.4.4 stores the parsed 03043054 list privately; this getter is not lazy.
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Textures")]
    private static extern List<string>? GetTextures(CGameCtnChallenge map);

    private static string Index(int index) => index.ToString(CultureInfo.InvariantCulture);
    private static string Escape(string label) => label.Replace("~", "~0").Replace("/", "~1");
    private void Unavailable(string path, string reason) => values[path + "/status"] = new(Text: "unavailable: " + reason);

    private bool Enter(string path, object? value, int depth)
    {
        if (!ReservePaths(path)) return false;
        if (depth > MaxDepth) { Unavailable(path, "depth limit (32)"); return false; }
        if (--remainingNodes < 0) { Unavailable(path, "node limit (16384)"); return false; }
        if (value is null) { Unavailable(path, "null data"); return false; }
        if (!active.Add(value)) { Unavailable(path, "cycle"); return false; }
        return true;
    }

    private bool Collection(string path, int count)
    {
        values[path + "/count"] = new(Integer: count);
        if (count <= MaxCollection) return true;
        Unavailable(path, "collection limit (4096)");
        return false;
    }

    private void Text(string path, string? text)
    {
        if (text is null) { values[path] = null; return; }
        if (text.Length > MaxText) { Unavailable(path, "text limit (65536)"); return; }
        if ((remainingText -= text.Length) < 0) { Unavailable(path, "text budget (2097152)"); return; }
        values[path] = new(Text: text);
    }

    private void Real(string path, float value)
    {
        Text(path, value.ToString("R", CultureInfo.InvariantCulture));
        // NaN payloads cannot be represented by round-trip decimal text.
        if (float.IsNaN(value)) values[path + "/bits"] = new(Integer: BitConverter.SingleToUInt32Bits(value));
    }

    private void Named(string path, IDictionary<string, ScriptTrait>? traits, int depth)
    {
        if (!Enter(path, traits, depth)) return;
        try
        {
            if (!Collection(path, traits!.Count)) return;
            if (traits.Keys.Any(k => k.Length > MaxText)) { Unavailable(path, "label limit (65536)"); return; }
            foreach (var pair in traits.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (remainingPaths <= 0) { Unavailable(path, "path budget (2097152)"); break; }
                if (remainingNodes <= 0 || remainingText < pair.Key.Length) { Unavailable(path, "capture budget"); break; }
                remainingText -= pair.Key.Length;
                Trait(path + "/" + Escape(pair.Key), pair.Value, depth + 1);
            }
        }
        finally { active.Remove(traits!); }
    }

    private void Type(string path, IScriptType? type, int depth)
    {
        if (!Enter(path, type, depth)) return;
        try
        {
            switch (type)
            {
                case ScriptType scalar:
                    Text(path, scalar.Type.ToString());
                    break;
                case ScriptArrayType array:
                    Text(path, "Array");
                    Type(path + "/key/type", array.KeyType, depth + 1);
                    Type(path + "/value/type", array.ValueType, depth + 1);
                    break;
                case ScriptStructType structure:
                    Text(path, "Struct");
                    Text(path + "/name", structure.Name);
                    Named(path + "/members", structure.Members, depth + 1);
                    break;
                default: Unavailable(path, "unsupported type"); break;
            }
        }
        finally { active.Remove(type!); }
    }

    private void Trait(string path, ScriptTrait? trait, int depth)
    {
        if (!Enter(path, trait, depth)) return;
        try
        {
            Type(path + "/type", trait!.Type, depth + 1);
            switch (trait)
            {
                case ScriptTrait<bool> t: values[path + "/value"] = new(Boolean: t.Value); break;
                case ScriptTrait<int> t: values[path + "/value"] = new(Integer: t.Value); break;
                case ScriptTrait<string> t: Text(path + "/value", t.Value); break;
                case ScriptTrait<float> t: Real(path + "/value", t.Value); break;
                case ScriptTrait<Vec2> t:
                    Real(path + "/value/x", t.Value.X); Real(path + "/value/y", t.Value.Y); break;
                case ScriptTrait<Vec3> t:
                    Real(path + "/value/x", t.Value.X); Real(path + "/value/y", t.Value.Y); Real(path + "/value/z", t.Value.Z); break;
                case ScriptTrait<Int2> t:
                    values[path + "/value/x"] = new(Integer: t.Value.X); values[path + "/value/y"] = new(Integer: t.Value.Y); break;
                case ScriptTrait<Int3> t:
                    values[path + "/value/x"] = new(Integer: t.Value.X); values[path + "/value/y"] = new(Integer: t.Value.Y);
                    values[path + "/value/z"] = new(Integer: t.Value.Z); break;
                case ScriptArrayTrait t when t.Value is not null:
                    if (!Collection(path, t.Value.Count)) break;
                    for (var i = 0; i < t.Value.Count; i++)
                    {
                        if (remainingPaths <= 0) { Unavailable(path, "path budget (2097152)"); break; }
                        if (remainingNodes <= 0) { Unavailable(path, "capture budget"); break; }
                        Trait(path + "/items/" + Index(i), t.Value[i], depth + 1);
                    }
                    break;
                case ScriptStructTrait t: Named(path + "/members", t.Value, depth + 1); break;
                case ScriptDictionaryTrait t when t.Value is not null: Dictionary(path, t.Value, depth + 1); break;
                default: Unavailable(path, "unsupported trait"); break;
            }
        }
        finally { active.Remove(trait!); }
    }

    private void Dictionary(string path, IDictionary<ScriptTrait, ScriptTrait> dictionary, int depth)
    {
        if (!Collection(path, dictionary.Count)) return;
        var entries = new List<(string Key, SortedDictionary<string, MapMetadataValue?> Rows, ScriptTrait Value)>();
        foreach (var pair in dictionary)
        {
            var rows = new SortedDictionary<string, MapMetadataValue?>(StringComparer.Ordinal);
            var keyCapture = new EmbeddedMetadataCapture(rows) { remainingNodes = remainingNodes, remainingText = remainingText, remainingPaths = remainingPaths };
            keyCapture.active.UnionWith(active);
            keyCapture.Trait("key", pair.Key, depth);
            remainingPaths = keyCapture.remainingPaths;
            remainingNodes = keyCapture.remainingNodes;
            remainingText = keyCapture.remainingText;
            if (rows.Keys.Any(k => k.EndsWith("/status", StringComparison.Ordinal)))
            {
                Unavailable(path, "dictionary key unavailable");
                return;
            }
            var canonical = new StringBuilder();
            foreach (var row in rows)
            {
                var scalar = row.Value;
                var text = scalar?.Text ?? scalar?.Integer?.ToString(CultureInfo.InvariantCulture) ?? scalar?.Boolean?.ToString() ?? "";
                canonical.Append(row.Key.Length).Append(':').Append(row.Key).Append(scalar?.Text is not null ? 't' : scalar?.Integer is not null ? 'i' : scalar?.Boolean is not null ? 'b' : 'n');
                canonical.Append(text.Length).Append(':').Append(text);
            }
            entries.Add((canonical.ToString(), rows, pair.Value));
        }
        if (entries.Select(e => e.Key).Distinct(StringComparer.Ordinal).Count() != entries.Count)
        {
            Unavailable(path, "ambiguous dictionary keys");
            return;
        }
        var index = 0;
        foreach (var entry in entries.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            var entryPath = path + "/entries/" + Index(index++);
            foreach (var row in entry.Rows)
            {
                if (!ReservePaths(entryPath + "/" + row.Key, 1)) return;
                values[entryPath + "/" + row.Key] = row.Value;
            }
            if (remainingPaths <= 0) { Unavailable(path, "path budget (2097152)"); break; }
            Trait(entryPath + "/value", entry.Value, depth);
        }
    }
}
