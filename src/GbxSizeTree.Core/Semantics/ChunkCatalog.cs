using GbxSizeTree.Model;

namespace GbxSizeTree.Semantics;

/// <summary>
/// Describes the verified CGameCtnChallenge header and TM2020 body chunks documented in
/// docs/FORMAT-NOTES.md.
/// </summary>
public static class ChunkCatalog
{
    private static readonly IReadOnlyCollection<ChunkMeta> Entries = Array.AsReadOnly(new[]
    {
        new ChunkMeta(0x03043002, "Legacy description", SizeCategory.Metadata, "Legacy map description and game version", false, false),
        new ChunkMeta(0x03043003, "Common header", SizeCategory.Header, "Map identity, UID, and lightmap version", false, false),
        new ChunkMeta(0x03043004, "Header version", SizeCategory.Header, "Map header format version", false, false),
        new ChunkMeta(0x03043005, "XML metadata", SizeCategory.Metadata, "XML map metadata string", false, false),
        new ChunkMeta(0x03043007, "Thumbnail", SizeCategory.Thumbnail, "JPEG thumbnail, dimensions, and comments", false, false),
        new ChunkMeta(0x03043008, "Author", SizeCategory.Metadata, "Map author metadata", false, false),

        // Small TM2020 body chunks: named via the pinned GBX.NET 2.4.4 XML docs plus the
        // 2026-09-01 Ghidra pass over Trackmania.exe's CGameCtnChallenge serializer
        // (summarized in docs/FORMAT-NOTES.md). Ghidra corrects GBX.NET on 0x018 (not laps
        // in TM2020) and 0x036 (medal times + comments, not a realtime-thumbnail camera).
        new ChunkMeta(0x0304300D, "Vehicle", SizeCategory.Metadata, "Player vehicle (player-model) identity", false, false),
        new ChunkMeta(0x03043011, "Challenge parameters", SizeCategory.Metadata, "Collector list and challenge parameter references", false, false),
        new ChunkMeta(0x03043018, "Legacy u32 pair", SizeCategory.Metadata, "Two u32s (GBX.NET calls this laps; TM2020 semantics unproven)", true, false),
        new ChunkMeta(0x03043019, "Texture mod", SizeCategory.Metadata, "Texture mod pack reference", true, false),
        new ChunkMeta(0x0304301F, "Blocks", SizeCategory.Blocks, "Map identity and placed block data", false, false),
        new ChunkMeta(0x03043022, "Map flags", SizeCategory.Metadata, "Flags word (encodes deco base-height offset)", false, false),
        new ChunkMeta(0x03043024, "Custom music", SizeCategory.Metadata, "Custom music pack reference", false, false),
        new ChunkMeta(0x03043025, "Map coord origin/target", SizeCategory.Metadata, "Map coordinate origin and target", false, false),
        new ChunkMeta(0x03043029, "Password hash (legacy)", SizeCategory.Metadata, "Hashed map password and checksum", true, false),
        new ChunkMeta(0x0304302A, "Boolean metadata", SizeCategory.Metadata, "Legacy challenge boolean value", false, false),
        new ChunkMeta(0x03043034, "Byte buffer (decals?)", SizeCategory.Metadata, "Length-prefixed byte buffer (GBX.NET: decals); empty on TM2020 saves", true, false),
        new ChunkMeta(0x03043036, "Medal times + comments", SizeCategory.Metadata, "Author/gold/silver/bronze times, laps, and map comments", true, false),
        new ChunkMeta(0x0304303E, "Car marks buffer", SizeCategory.Metadata, "Car marks (skid) buffer", true, false),
        new ChunkMeta(0x03043040, "Items", SizeCategory.Items, "Anchored object placement data", true, true),
        new ChunkMeta(0x03043042, "Author information", SizeCategory.Metadata, "Body author information", true, false),
        new ChunkMeta(0x03043043, "Zone genealogies", SizeCategory.Metadata, "Encapsulated zone genealogy data", true, true),
        new ChunkMeta(0x03043044, "Script metadata", SizeCategory.ScriptMetadata, "Encapsulated script traits metadata", true, true),
        new ChunkMeta(0x03043048, "Baked blocks", SizeCategory.BakedBlocks, "Baked blocks and additional clip data", true, false),
        new ChunkMeta(0x03043049, "MediaTracker", SizeCategory.MediaTracker, "MediaTracker clips and trigger data", false, false),
        new ChunkMeta(0x0304304B, "Objectives", SizeCategory.Metadata, "Objective text strings", true, false),
        new ChunkMeta(0x0304304F, "Byte flag", SizeCategory.Metadata, "Version 3 + one byte; meaning not yet identified", true, false),
        new ChunkMeta(0x03043050, "Offzones", SizeCategory.Metadata, "Map offzone data", true, false),
        new ChunkMeta(0x03043051, "Title and build", SizeCategory.Metadata, "Title identifier and build version", true, false),
        new ChunkMeta(0x03043052, "Decoration base height", SizeCategory.Metadata, "Decoration base height", true, false),
        new ChunkMeta(0x03043053, "Bot paths", SizeCategory.Metadata, "Bot navigation paths", true, false),
        new ChunkMeta(0x03043054, "Embedded items", SizeCategory.EmbeddedItems, "Encapsulated embedded item ZIP and texture names", true, true),
        new ChunkMeta(0x03043055, "TMUnlimiter", SizeCategory.Other, "TMUnlimiter extension data", true, false),
        new ChunkMeta(0x03043056, "Day time", SizeCategory.Metadata, "Map day-time setting", true, false),
        new ChunkMeta(0x03043057, "Path records", SizeCategory.Metadata, "Path-like record list (polyline + ident); empty on typical maps", true, false),
        new ChunkMeta(0x03043059, "World distortion", SizeCategory.Metadata, "World distortion settings", true, false),
        new ChunkMeta(0x0304305A, "Nested-challenge grid", SizeCategory.Metadata, "Sub-challenge grid and element list; empty on typical maps", true, false),
        new ChunkMeta(0x0304305B, "Lightmap", SizeCategory.Lightmap, "Lightmap WebP frames and compressed cache", true, false),
        new ChunkMeta(0x0304305D, "Sparse byte octree", SizeCategory.Other, "Sparse 3D byte volume over the map grid (terrain/occupancy-style)", true, false),
        new ChunkMeta(0x0304305E, "Legacy macroblocks (stub)", SizeCategory.Metadata, "Always-empty legacy macroblock list; discarded on load", true, false),
        new ChunkMeta(0x0304305F, "Free blocks", SizeCategory.FreeBlocks, "Positions and rotations for free blocks", true, false),
        new ChunkMeta(0x03043060, "U32 field", SizeCategory.Metadata, "Version 0 + one u32; meaning not yet identified", true, false),
        new ChunkMeta(0x03043061, "Write-only snapshot", SizeCategory.Metadata, "Nested object snapshot the game discards on load", true, false),
        new ChunkMeta(0x03043062, "Difficulty colors", SizeCategory.PerElementArrays, "Difficulty color byte per map element", true, false),
        new ChunkMeta(0x03043063, "Animation phase offsets", SizeCategory.PerElementArrays, "Animation phase byte per map element", true, false),
        new ChunkMeta(0x03043064, "Media-clip-group stub", SizeCategory.Metadata, "Always-empty media clip group list", true, false),
        new ChunkMeta(0x03043065, "Foreground pack", SizeCategory.Metadata, "Foreground pack description", true, false),
        new ChunkMeta(0x03043067, "Launched checkpoints", SizeCategory.Metadata, "Launched checkpoint data", true, false),
        new ChunkMeta(0x03043068, "Lightmap qualities", SizeCategory.PerElementArrays, "Lightmap quality byte per map element", true, false),
        new ChunkMeta(0x03043069, "Macroblock indexes", SizeCategory.PerElementArrays, "Macroblock indexes, identifiers, and flags", true, false),
        new ChunkMeta(0x0304306B, "Dynamic daylight", SizeCategory.Metadata, "Dynamic daylight settings", true, false),
        new ChunkMeta(0x0304306C, "Color palette", SizeCategory.Metadata, "Map color palette", true, false),
    });

    private static readonly IReadOnlyDictionary<uint, ChunkMeta> ById =
        Entries.ToDictionary(static chunk => chunk.Id);

    /// <summary>Gets all known header and body chunks.</summary>
    public static IReadOnlyCollection<ChunkMeta> All => Entries;

    /// <summary>Describes a chunk id without failing for unrecognized chunks.</summary>
    public static ChunkMeta Describe(uint id) => ById.TryGetValue(id, out var chunk)
        ? chunk
        : new ChunkMeta(id, $"Unknown 0x{id:X8}", SizeCategory.Other, "unrecognized chunk", false, false);

    /// <summary>True when the id is in the catalog (the `--unknown-chunks` debug filter).</summary>
    public static bool IsKnown(uint id) => ById.ContainsKey(id);
}
