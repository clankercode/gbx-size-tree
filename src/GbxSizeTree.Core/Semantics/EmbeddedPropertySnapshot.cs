using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;
using GBX.NET.LZO;
using GBX.NET.ZLib;

namespace GbxSizeTree.Semantics;

public sealed record EmbeddedPropertyValue(
    string Text,
    bool? Boolean = null,
    long? Integer = null,
    double? Number = null);

public sealed record EmbeddedPropertyIssue(string Code, string Path, string Message);

public sealed record EmbeddedPropertyChange(
    string Path,
    EmbeddedPropertyValue? Left,
    EmbeddedPropertyValue? Right);

public sealed record EmbeddedPropertyDiff(
    IReadOnlyList<EmbeddedPropertyChange> Changes,
    bool? ContentChanged,
    IReadOnlyList<EmbeddedPropertyIssue> LeftIssues,
    IReadOnlyList<EmbeddedPropertyIssue> RightIssues);

public sealed class EmbeddedPropertyLimits
{
    public int MaxDepth { get; init; } = 32;
    public int MaxNodes { get; init; } = 100_000;
    public int MaxValues { get; init; } = 100_000;
    public int MaxValueBytes { get; init; } = 1_048_576;
    public int MaxInputBytes { get; init; } = 67_108_864;
}

public sealed class EmbeddedPropertySnapshotData
{
    internal EmbeddedPropertySnapshotData(
        Dictionary<string, EmbeddedPropertyValue> values,
        List<EmbeddedPropertyIssue> issues,
        List<string> unreadablePaths,
        string? contentHash,
        bool limitReached)
    {
        Values = values;
        Issues = issues;
        UnreadablePaths = unreadablePaths;
        ContentHash = contentHash;
        LimitReached = limitReached;
    }

    public IReadOnlyDictionary<string, EmbeddedPropertyValue> Values { get; }
    public IReadOnlyList<EmbeddedPropertyIssue> Issues { get; }
    public string? ContentHash { get; }

    internal IReadOnlyList<string> UnreadablePaths { get; }
    internal bool LimitReached { get; }
}

/// <summary>
/// Captures a bounded, explicitly typed subset of embedded item properties without reflection.
/// </summary>
public static class EmbeddedPropertySnapshot
{
    private const string Separator = " > ";

    public static EmbeddedPropertySnapshotData Capture(
        byte[] bytes,
        EmbeddedPropertyLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return Capture(new MemoryStream(bytes, writable: false), limits);
    }

    public static EmbeddedPropertySnapshotData Capture(
        Stream stream,
        EmbeddedPropertyLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        limits ??= new EmbeddedPropertyLimits();
        Validate(limits);

        var read = ReadBounded(stream, limits.MaxInputBytes);
        if (read.ExceededLimit)
        {
            return IssueOnly(
                "limit",
                "Input",
                $"Input exceeds the {limits.MaxInputBytes}-byte capture limit.",
                limitReached: true);
        }

        var bytes = read.Bytes;
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (bytes.Length < 3 || !bytes.AsSpan(0, 3).SequenceEqual("GBX"u8))
        {
            return IssueOnly(
                "unsupported-format",
                "Input",
                "Content is not a GBX container; only its complete SHA-256 hash was captured.",
                hash);
        }

        try
        {
            EnsureCodecs();
            using var input = new MemoryStream(bytes, writable: false);
            var node = Gbx.ParseNode(input);
            if (node is null)
            {
                return IssueOnly(
                    "parse",
                    "Input",
                    "GBX parsing returned no main node; the complete SHA-256 hash remains available.",
                    hash);
            }
            return CaptureParsedCore(node, limits, hash);
        }
        catch (Exception ex) when (IsParseFailure(ex))
        {
            return IssueOnly(
                "parse",
                "Input",
                $"GBX property parsing failed ({ex.GetType().Name}); the complete SHA-256 hash remains available.",
                hash);
        }
    }

    public static EmbeddedPropertySnapshotData Capture(
        ZipArchiveEntry entry,
        EmbeddedPropertyLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        limits ??= new EmbeddedPropertyLimits();
        Validate(limits);

        if (entry.Length > limits.MaxInputBytes)
        {
            return IssueOnly(
                "limit",
                "Input",
                $"ZIP entry is {entry.Length} bytes and exceeds the {limits.MaxInputBytes}-byte capture limit.",
                limitReached: true);
        }

        using var stream = entry.Open();
        return Capture(stream, limits);
    }

    public static EmbeddedPropertySnapshotData CaptureParsed(
        CMwNod node,
        EmbeddedPropertyLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        limits ??= new EmbeddedPropertyLimits();
        Validate(limits);
        return CaptureParsedCore(node, limits, contentHash: null);
    }

    public static EmbeddedPropertyDiff Compare(
        EmbeddedPropertySnapshotData left,
        EmbeddedPropertySnapshotData right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        bool? contentChanged = left.ContentHash is not null && right.ContentHash is not null
            ? !string.Equals(left.ContentHash, right.ContentHash, StringComparison.Ordinal)
            : null;

        if (left.LimitReached || right.LimitReached)
        {
            return new EmbeddedPropertyDiff([], contentChanged, left.Issues, right.Issues);
        }

        var changes = new List<EmbeddedPropertyChange>();
        foreach (var path in left.Values.Keys.Union(right.Values.Keys, StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
        {
            if (IsUnreadable(path, left.UnreadablePaths) || IsUnreadable(path, right.UnreadablePaths))
            {
                continue;
            }

            left.Values.TryGetValue(path, out var leftValue);
            right.Values.TryGetValue(path, out var rightValue);
            if (leftValue != rightValue)
            {
                changes.Add(new EmbeddedPropertyChange(path, leftValue, rightValue));
            }
        }

        return new EmbeddedPropertyDiff(changes, contentChanged, left.Issues, right.Issues);
    }

    private static EmbeddedPropertySnapshotData CaptureParsedCore(
        CMwNod node,
        EmbeddedPropertyLimits limits,
        string? contentHash)
    {
        var visitor = new Visitor(limits);
        visitor.VisitRoot(node);
        visitor.AddPartialCoverageIssue();
        return new EmbeddedPropertySnapshotData(
            visitor.Values,
            visitor.Issues,
            visitor.UnreadablePaths,
            contentHash,
            visitor.LimitReached);
    }

    private static EmbeddedPropertySnapshotData IssueOnly(
        string code,
        string path,
        string message,
        string? contentHash = null,
        bool limitReached = false) =>
        new([], [new EmbeddedPropertyIssue(code, path, message)], [path], contentHash, limitReached);

    private static bool IsUnreadable(string path, IReadOnlyList<string> prefixes)
    {
        foreach (var prefix in prefixes)
        {
            if (string.Equals(path, prefix, StringComparison.Ordinal)
                || path.StartsWith(prefix + Separator, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static (byte[] Bytes, bool ExceededLimit) ReadBounded(Stream stream, int maxBytes)
    {
        using var output = new MemoryStream(Math.Min(maxBytes, 81_920));
        var buffer = new byte[Math.Min(maxBytes, 81_920)];
        var remaining = maxBytes;
        while (remaining > 0)
        {
            var read = stream.Read(buffer, 0, Math.Min(buffer.Length, remaining));
            if (read == 0)
            {
                return (output.ToArray(), false);
            }
            output.Write(buffer, 0, read);
            remaining -= read;
        }

        var next = stream.ReadByte();
        return next < 0 ? (output.ToArray(), false) : ([], true);
    }

    private static void EnsureCodecs()
    {
        Gbx.LZO ??= new Lzo();
        try
        {
            _ = Gbx.ZLib;
        }
        catch (Exception ex) when (ex is InvalidOperationException or TypeInitializationException)
        {
            Gbx.ZLib = new ZLib();
        }
    }

    private static bool IsParseFailure(Exception ex) =>
        ex is not OutOfMemoryException
            and not StackOverflowException
            and not AccessViolationException;

    private static void Validate(EmbeddedPropertyLimits limits)
    {
        if (limits.MaxDepth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "MaxDepth cannot be negative.");
        }
        if (limits.MaxNodes <= 0 || limits.MaxValues <= 0
            || limits.MaxValueBytes <= 0 || limits.MaxInputBytes <= 0
            || limits.MaxInputBytes == int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "All size and count limits must be positive and bounded.");
        }
    }

    private sealed class Visitor(EmbeddedPropertyLimits limits)
    {
        private readonly HashSet<CMwNod> ancestors = new(ReferenceEqualityComparer.Instance);
        private int nodeCount;
        private int valueBytes;
        private bool stopped;
        private bool partialCoverageAdded;

        public Dictionary<string, EmbeddedPropertyValue> Values { get; } = new(StringComparer.Ordinal);
        public List<EmbeddedPropertyIssue> Issues { get; } = [];
        public List<string> UnreadablePaths { get; } = [];
        public bool LimitReached { get; private set; }

        public void VisitRoot(CMwNod node) => VisitNode(node, TypeSegment(node), 0);

        public void AddPartialCoverageIssue()
        {
            if (partialCoverageAdded)
            {
                return;
            }
            Issues.Add(new EmbeddedPropertyIssue(
                "partial-coverage",
                "Input",
                "Only explicitly supported item, prefab, static-object, solid, and material properties were inspected."));
            partialCoverageAdded = true;
        }

        private void VisitNode(CMwNod node, string path, int depth)
        {
            if (stopped)
            {
                return;
            }
            if (depth > limits.MaxDepth)
            {
                StopAtLimit(path, $"Traversal exceeded maximum depth {limits.MaxDepth}.");
                return;
            }
            if (++nodeCount > limits.MaxNodes)
            {
                StopAtLimit(path, $"Traversal exceeded maximum node count {limits.MaxNodes}.");
                return;
            }
            if (!ancestors.Add(node))
            {
                Issues.Add(new EmbeddedPropertyIssue("cycle", path, "A reference cycle was stopped at this occurrence."));
                UnreadablePaths.Add(path);
                return;
            }

            try
            {
                switch (node)
                {
                    case CGameItemModel item:
                        VisitItem(item, path, depth);
                        break;
                    case CPlugPrefab prefab:
                        VisitPrefab(prefab, path, depth);
                        break;
                    case CGameCommonItemEntityModel entity:
                        VisitCommonEntity(entity, path, depth);
                        break;
                    case CPlugStaticObjectModel staticObject:
                        VisitStaticObject(staticObject, path, depth);
                        break;
                    case CPlugSolid2Model solid:
                        VisitSolid(solid, path, depth);
                        break;
                    case CPlugMaterialUserInst material:
                        VisitMaterial(material, path);
                        break;
                    default:
                        Unsupported(path, node.GetType().Name);
                        break;
                }
            }
            finally
            {
                ancestors.Remove(node);
            }
        }

        private void VisitItem(CGameItemModel item, string path, int depth)
        {
            AddText(path + Separator + "ItemType", item.ItemType.ToString());
            AddText(path + Separator + "WaypointType", item.WaypointType.ToString());
            AddBoolean(path + Separator + "DisableLightmap", item.DisableLightmap);
            if (item.EntityModel is { } entity)
            {
                VisitNode(entity, path + Separator + "EntityModel" + Separator + TypeSegment(entity), depth + 1);
            }
        }

        private void VisitPrefab(CPlugPrefab prefab, string path, int depth)
        {
            AddText(path + Separator + "Url", prefab.Url);
            AddInteger(path + Separator + "Version", prefab.Version);
            var ents = prefab.Ents ?? [];
            for (var index = 0; index < ents.Length && !stopped; index++)
            {
                if (ents[index].Model is { } model)
                {
                    VisitNode(
                        model,
                        path + Separator + $"Ent#{index + 1}" + Separator + TypeSegment(model),
                        depth + 1);
                }
            }
        }

        private void VisitCommonEntity(CGameCommonItemEntityModel entity, string path, int depth)
        {
            if (entity.StaticObject is { } staticObject)
            {
                VisitNode(
                    staticObject,
                    path + Separator + "StaticObject" + Separator + TypeSegment(staticObject),
                    depth + 1);
            }
            if (entity.VisModel is { } visModel)
            {
                VisitNode(visModel, path + Separator + "VisModel" + Separator + TypeSegment(visModel), depth + 1);
            }
            if (entity.PhyModel is { } phyModel)
            {
                VisitNode(phyModel, path + Separator + "PhyModel" + Separator + TypeSegment(phyModel), depth + 1);
            }
        }

        private void VisitStaticObject(CPlugStaticObjectModel staticObject, string path, int depth)
        {
            AddBoolean(path + Separator + "IsMeshCollidable", staticObject.IsMeshCollidable);
            AddInteger(path + Separator + "Version", staticObject.Version);
            if (staticObject.Mesh is { } mesh)
            {
                VisitNode(mesh, path + Separator + "Mesh" + Separator + TypeSegment(mesh), depth + 1);
            }
            if (staticObject.Shape is { } shape)
            {
                VisitNode(shape, path + Separator + "Shape" + Separator + TypeSegment(shape), depth + 1);
            }
        }

        private void VisitSolid(CPlugSolid2Model solid, string path, int depth)
        {
            AddInteger(path + Separator + "Flags", solid.Flags);
            AddInteger(path + Separator + "DamageZone", solid.DamageZone);
            AddInteger(path + Separator + "FileVersion", solid.FileVersion);
            AddInteger(path + Separator + "VisualType", solid.VisCstType);
            AddText(path + Separator + "MaterialsFolder", solid.MaterialsFolderName);

            var materialIndex = 0;
            foreach (var material in solid.CustomMaterials ?? [])
            {
                if (stopped)
                {
                    break;
                }
                materialIndex++;
                var materialPath = path + Separator + $"Material#{materialIndex}";
                AddText(materialPath + Separator + "Name", material.MaterialName);
                if (material.MaterialUserInst is { } instance)
                {
                    VisitNode(instance, materialPath, depth + 1);
                }
            }
            foreach (var material in solid.MaterialInsts ?? [])
            {
                if (stopped)
                {
                    break;
                }
                materialIndex++;
                VisitNode(material, path + Separator + $"Material#{materialIndex}", depth + 1);
            }
        }

        private void VisitMaterial(CPlugMaterialUserInst material, string path)
        {
            AddText(path + Separator + "Name", material.MaterialName);
            AddText(path + Separator + "Link", material.Link);
            AddText(path + Separator + "Model", material.Model);
            AddText(path + Separator + "BaseTexture", material.BaseTexture);
            AddText(path + Separator + "HidingGroup", material.HidingGroup);
            AddText(path + Separator + "SurfacePhysics", material.SurfacePhysicId.ToString());
            AddText(path + Separator + "SurfaceGameplay", material.SurfaceGameplayId.ToString());
            AddBoolean(path + Separator + "IsNatural", material.IsNatural);
            AddBoolean(path + Separator + "UsesGameMaterial", material.IsUsingGameMaterial);
            AddNumber(path + Separator + "TextureSizeMeters", material.TextureSizeInMeters);
            AddText(path + Separator + "TilingU", material.TilingU.ToString());
            AddText(path + Separator + "TilingV", material.TilingV.ToString());
        }

        private void AddText(string path, string? value)
        {
            if (value is not null)
            {
                Add(path, new EmbeddedPropertyValue(value));
            }
        }

        private void AddBoolean(string path, bool value) =>
            Add(path, new EmbeddedPropertyValue(value ? "true" : "false", Boolean: value));

        private void AddInteger(string path, long value) =>
            Add(path, new EmbeddedPropertyValue(value.ToString(System.Globalization.CultureInfo.InvariantCulture), Integer: value));

        private void AddNumber(string path, double value) =>
            Add(path, new EmbeddedPropertyValue(value.ToString("R", System.Globalization.CultureInfo.InvariantCulture), Number: value));

        private void Add(string path, EmbeddedPropertyValue value)
        {
            if (stopped)
            {
                return;
            }
            if (Values.Count >= limits.MaxValues)
            {
                StopAtLimit(path, $"Capture exceeded maximum value count {limits.MaxValues}.");
                return;
            }

            var bytes = Encoding.UTF8.GetByteCount(value.Text);
            if (bytes > limits.MaxValueBytes - valueBytes)
            {
                StopAtLimit(path, $"Capture exceeded maximum value text size {limits.MaxValueBytes} bytes.");
                return;
            }

            valueBytes += bytes;
            Values[path] = value;
        }

        private void Unsupported(string path, string typeName)
        {
            Issues.Add(new EmbeddedPropertyIssue(
                "unsupported",
                path,
                $"Node type {typeName} is outside the explicit property allowlist."));
            UnreadablePaths.Add(path);
        }

        private void StopAtLimit(string path, string message)
        {
            Issues.Add(new EmbeddedPropertyIssue("limit", path, message));
            UnreadablePaths.Add(path);
            LimitReached = true;
            stopped = true;
        }

        private static string TypeSegment(CMwNod node) => node switch
        {
            CGameItemModel => "Item",
            CPlugPrefab => "Prefab",
            CGameCommonItemEntityModel => "CommonItemEntityModel",
            CPlugStaticObjectModel => "StaticObjectModel",
            CPlugSolid2Model => "Solid2Model",
            CPlugMaterialUserInst => "Material",
            _ => node.GetType().Name
        };
    }
}
