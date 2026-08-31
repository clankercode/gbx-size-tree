using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.LZO;
using GBX.NET.ZLib;
using GbxSizeTree.Actions;
using GbxSizeTree.Container;
using GbxSizeTree.Drilldown;
using GbxSizeTree.Model;
using GbxSizeTree.Semantics;

namespace GbxSizeTree.Session;

/// <summary>
/// Re-parses a produced <c>CGameCtnChallenge</c> and verifies the identity and element-count
/// invariants documented in docs/FORMAT-NOTES.md.
/// </summary>
public static class OutputValidator
{
    public static ValidationReport Validate(
        MapFacts before,
        byte[] producedBytes,
        IReadOnlyList<IMapAction> appliedActions,
        IReadOnlyDictionary<string, string> settings,
        out MapFacts after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(producedBytes);
        ArgumentNullException.ThrowIfNull(appliedActions);
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            Gbx.LZO = new Lzo();
            Gbx.ZLib = new ZLib();
            using var stream = new MemoryStream(producedBytes, writable: false);
            var map = Gbx.Parse<CGameCtnChallenge>(stream).Node;
            after = FactsFrom(map, producedBytes);
        }
        catch (Exception ex)
        {
            after = before;
            return new ValidationReport(false,
            [
                new ValidationIssue("parse-produced-map", $"Failed to parse produced map: {ex.Message}"),
            ]);
        }

        var issues = new List<ValidationIssue>();
        CheckEqual(issues, nameof(MapFacts.MapUid), before.MapUid, after.MapUid);
        CheckEqual(issues, nameof(MapFacts.MapName), before.MapName, after.MapName);
        CheckEqual(issues, nameof(MapFacts.AuthorLogin), before.AuthorLogin, after.AuthorLogin);
        CheckEqual(issues, nameof(MapFacts.AuthorNickname), before.AuthorNickname, after.AuthorNickname);
        CheckEqual(issues, nameof(MapFacts.BlockCount), before.BlockCount, after.BlockCount);
        CheckEqual(
            issues,
            nameof(MapFacts.AnchoredObjectCount),
            before.AnchoredObjectCount,
            after.AnchoredObjectCount);
        CheckEqual(issues, nameof(MapFacts.BakedBlockCount), before.BakedBlockCount, after.BakedBlockCount);
        CheckEqual(issues, nameof(MapFacts.FreeBlockCount), before.FreeBlockCount, after.FreeBlockCount);

        foreach (var action in appliedActions)
        {
            issues.AddRange(action.ValidateResult(before, after));
        }

        return issues.Count == 0 ? ValidationReport.Success : new ValidationReport(false, issues);
    }

    private static MapFacts FactsFrom(CGameCtnChallenge map, byte[] producedBytes)
    {
        var counts = ElementCounts.Count(map);
        var body = DecompressedBody.GetBody(producedBytes);
        var scan = new SkippableChunkScanner().Scan(body);
        var lightmapRegion = scan.Regions.FirstOrDefault(static region =>
            region.Kind == RawChunkKind.Skippable && region.ChunkId == 0x0304305B);
        // LightmapFrames is a lazy zlib parse. Read the verified raw chunk layout instead;
        // see docs/FORMAT-NOTES.md, "GBX.NET 2.4.4 API essentials".
        var lightmap = new LightmapDrilldown().Inspect(body, lightmapRegion, map);
        var embeddedEntryCount = 0;

        if (map.EmbeddedZipData is { Length: > 0 })
        {
            using var archive = map.OpenReadEmbeddedZipData();
            embeddedEntryCount = archive.Entries.Count;
        }

        return new MapFacts(
            map.MapUid ?? "",
            map.MapName ?? "",
            map.AuthorLogin ?? "",
            map.AuthorNickname ?? "",
            counts.blocks,
            counts.items,
            counts.baked,
            counts.free,
            lightmap?.HasLightmaps ?? false,
            lightmap?.FrameCount ?? 0,
            lightmap?.Version ?? 0,
            embeddedEntryCount);
    }

    private static void CheckEqual<T>(List<ValidationIssue> issues, string rule, T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            issues.Add(new ValidationIssue(rule, $"Expected {expected}, but produced map has {actual}."));
        }
    }
}
