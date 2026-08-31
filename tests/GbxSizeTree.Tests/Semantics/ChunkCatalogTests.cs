using GBX.NET.Engines.Game;
using GbxSizeTree.Semantics;
using GbxSizeTree.Model;

namespace GbxSizeTree.Tests.Semantics;

public sealed class ChunkCatalogTests
{
    [Theory]
    [InlineData(0x03043002u, SizeCategory.Metadata, false, false)]
    [InlineData(0x03043003u, SizeCategory.Header, false, false)]
    [InlineData(0x03043004u, SizeCategory.Header, false, false)]
    [InlineData(0x03043005u, SizeCategory.Metadata, false, false)]
    [InlineData(0x03043007u, SizeCategory.Thumbnail, false, false)]
    [InlineData(0x03043008u, SizeCategory.Metadata, false, false)]
    [InlineData(0x03043011u, SizeCategory.Metadata, false, false)]
    [InlineData(0x0304301Fu, SizeCategory.Blocks, false, false)]
    [InlineData(0x03043022u, SizeCategory.Metadata, false, false)]
    [InlineData(0x0304302Au, SizeCategory.Metadata, false, false)]
    [InlineData(0x03043040u, SizeCategory.Items, true, true)]
    [InlineData(0x03043042u, SizeCategory.Metadata, true, false)]
    [InlineData(0x03043043u, SizeCategory.Metadata, true, true)]
    [InlineData(0x03043044u, SizeCategory.ScriptMetadata, true, true)]
    [InlineData(0x03043048u, SizeCategory.BakedBlocks, true, false)]
    [InlineData(0x03043049u, SizeCategory.MediaTracker, false, false)]
    [InlineData(0x0304304Bu, SizeCategory.Metadata, true, false)]
    [InlineData(0x03043050u, SizeCategory.Metadata, true, false)]
    [InlineData(0x03043051u, SizeCategory.Metadata, true, false)]
    [InlineData(0x03043052u, SizeCategory.Metadata, true, false)]
    [InlineData(0x03043053u, SizeCategory.Metadata, true, false)]
    [InlineData(0x03043054u, SizeCategory.EmbeddedItems, true, true)]
    [InlineData(0x03043055u, SizeCategory.Other, true, false)]
    [InlineData(0x03043056u, SizeCategory.Metadata, true, false)]
    [InlineData(0x03043059u, SizeCategory.Metadata, true, false)]
    [InlineData(0x0304305Bu, SizeCategory.Lightmap, true, false)]
    [InlineData(0x0304305Du, SizeCategory.Other, true, false)]
    [InlineData(0x0304305Eu, SizeCategory.Metadata, true, false)]
    [InlineData(0x0304305Fu, SizeCategory.FreeBlocks, true, false)]
    [InlineData(0x03043062u, SizeCategory.PerElementArrays, true, false)]
    [InlineData(0x03043063u, SizeCategory.PerElementArrays, true, false)]
    [InlineData(0x03043065u, SizeCategory.Metadata, true, false)]
    [InlineData(0x03043067u, SizeCategory.Metadata, true, false)]
    [InlineData(0x03043068u, SizeCategory.PerElementArrays, true, false)]
    [InlineData(0x03043069u, SizeCategory.PerElementArrays, true, false)]
    [InlineData(0x0304306Bu, SizeCategory.Metadata, true, false)]
    [InlineData(0x0304306Cu, SizeCategory.Metadata, true, false)]
    public void Describe_KnownChunk_ReturnsExpectedSemantics(
        uint id,
        SizeCategory category,
        bool skippable,
        bool encapsulated)
    {
        var chunk = ChunkCatalog.Describe(id);

        Assert.Equal(category, chunk.Category);
        Assert.Equal(skippable, chunk.Skippable);
        Assert.Equal(encapsulated, chunk.Encapsulated);
    }

    [Fact]
    public void All_ContainsEveryDocumentedHeaderAndBodyChunk()
    {
        uint[] ids =
        [
            0x03043002, 0x03043003, 0x03043004, 0x03043005, 0x03043007, 0x03043008,
            0x0304300D, 0x03043011, 0x03043018, 0x03043019, 0x0304301F, 0x03043022,
            0x03043024, 0x03043025, 0x03043029, 0x0304302A, 0x03043034, 0x03043036,
            0x0304303E, 0x03043040, 0x03043042, 0x03043043,
            0x03043044, 0x03043048, 0x03043049, 0x0304304B, 0x0304304F, 0x03043050,
            0x03043051, 0x03043052, 0x03043053, 0x03043054, 0x03043055, 0x03043056,
            0x03043057, 0x03043059, 0x0304305A, 0x0304305B, 0x0304305D, 0x0304305E,
            0x0304305F, 0x03043060, 0x03043061, 0x03043062, 0x03043063, 0x03043064,
            0x03043065, 0x03043067, 0x03043068, 0x03043069, 0x0304306B, 0x0304306C,
        ];

        // All 18 small body chunks from the 2026-09-01 Ghidra pass are cataloged
        // (docs/FORMAT-NOTES.md); --unknown-chunks now reports 0 on the sample map.
        Assert.True(ChunkCatalog.IsKnown(0x0304305D));

        Assert.Equal(ids.Length, ChunkCatalog.All.Count);
        Assert.All(ids, id => Assert.Contains(ChunkCatalog.All, chunk => chunk.Id == id));
    }

    [Fact]
    public void Describe_UnknownChunk_ReturnsSynthesizedMetadata()
    {
        var chunk = ChunkCatalog.Describe(0xDEADBEEF);

        Assert.Equal(0xDEADBEEFu, chunk.Id);
        Assert.Equal("Unknown 0xDEADBEEF", chunk.Name);
        Assert.Equal(SizeCategory.Other, chunk.Category);
        Assert.Equal("unrecognized chunk", chunk.Description);
        Assert.False(chunk.Skippable);
        Assert.False(chunk.Encapsulated);
    }

    [Theory]
    [InlineData(1_096_594, 35_374, "block", "35,374 blocks - 31 B/block")]
    [InlineData(1_096_594, 0, "block", "0 blocks - n/a")]
    public void PerElementDetail_FormatsInvariantDetail(
        long chunkBytes,
        int elements,
        string unit,
        string expected)
    {
        Assert.Equal(expected, ElementCounts.PerElementDetail(chunkBytes, elements, unit));
    }

    [Fact]
    public void Count_NullMap_ReturnsZeroes()
    {
        Assert.Equal((0, 0, 0, 0), ElementCounts.Count(null!));
    }

    [Fact]
    public void Count_EmptyMap_ReturnsZeroes()
    {
        Assert.Equal((0, 0, 0, 0), ElementCounts.Count(new CGameCtnChallenge()));
    }

    [Fact]
    public void Count_PopulatedMap_ReturnsCollectionAndFreeBlockCounts()
    {
        var map = new CGameCtnChallenge
        {
            Blocks =
            [
                new CGameCtnBlock(),
                new CGameCtnBlock { IsFree = true },
                new CGameCtnBlock { IsFree = true },
            ],
            BakedBlocks = [new CGameCtnBlock(), new CGameCtnBlock()],
            AnchoredObjects =
            [
                new CGameCtnAnchoredObject(),
                new CGameCtnAnchoredObject(),
                new CGameCtnAnchoredObject(),
                new CGameCtnAnchoredObject(),
            ],
        };

        Assert.Equal((blocks: 3, baked: 2, items: 4, free: 2), ElementCounts.Count(map));
    }
}
