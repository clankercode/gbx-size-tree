using GbxSizeTree.Model;

namespace GbxSizeTree.Core.Semantics;

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

        new ChunkMeta(0x03043011, "Challenge parameters", SizeCategory.Metadata, "Collector list and challenge parameter references", false, false),
        new ChunkMeta(0x0304301F, "Blocks", SizeCategory.Blocks, "Map identity and placed block data", false, false),
        new ChunkMeta(0x0304302A, "Boolean metadata", SizeCategory.Metadata, "Legacy challenge boolean value", false, false),
        new ChunkMeta(0x03043040, "Items", SizeCategory.Items, "Anchored object placement data", true, true),
        new ChunkMeta(0x03043042, "Author information", SizeCategory.Metadata, "Body author information", true, false),
        new ChunkMeta(0x03043043, "Zone genealogies", SizeCategory.Metadata, "Encapsulated zone genealogy data", true, true),
        new ChunkMeta(0x03043044, "Script metadata", SizeCategory.ScriptMetadata, "Encapsulated script traits metadata", true, true),
        new ChunkMeta(0x03043048, "Baked blocks", SizeCategory.BakedBlocks, "Baked blocks and additional clip data", true, false),
        new ChunkMeta(0x03043049, "MediaTracker", SizeCategory.MediaTracker, "MediaTracker clips and trigger data", false, false),
        new ChunkMeta(0x0304304B, "Objectives", SizeCategory.Metadata, "Objective text strings", true, false),
        new ChunkMeta(0x03043050, "Offzones", SizeCategory.Metadata, "Map offzone data", true, false),
        new ChunkMeta(0x03043051, "Title and build", SizeCategory.Metadata, "Title identifier and build version", true, false),
        new ChunkMeta(0x03043052, "Decoration base height", SizeCategory.Metadata, "Decoration base height", true, false),
        new ChunkMeta(0x03043053, "Bot paths", SizeCategory.Metadata, "Bot navigation paths", true, false),
        new ChunkMeta(0x03043054, "Embedded items", SizeCategory.EmbeddedItems, "Encapsulated embedded item ZIP and texture names", true, true),
        new ChunkMeta(0x03043055, "TMUnlimiter", SizeCategory.Other, "TMUnlimiter extension data", true, false),
        new ChunkMeta(0x03043056, "Day time", SizeCategory.Metadata, "Map day-time setting", true, false),
        new ChunkMeta(0x03043059, "World distortion", SizeCategory.Metadata, "World distortion settings", true, false),
        new ChunkMeta(0x0304305B, "Lightmap", SizeCategory.Lightmap, "Lightmap WebP frames and compressed cache", true, false),
        new ChunkMeta(0x0304305F, "Free blocks", SizeCategory.FreeBlocks, "Positions and rotations for free blocks", true, false),
        new ChunkMeta(0x03043062, "Difficulty colors", SizeCategory.PerElementArrays, "Difficulty color byte per map element", true, false),
        new ChunkMeta(0x03043063, "Animation phase offsets", SizeCategory.PerElementArrays, "Animation phase byte per map element", true, false),
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
}
