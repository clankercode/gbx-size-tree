using GbxSizeTree.Model;

namespace GbxSizeTree.Tests.Fixtures;

/// <summary>
/// A hand-built MapAnalysis exercising every model shape — renderer and JSON tests use this
/// so they need neither GBX.NET nor a map file.
/// </summary>
public static class FakeAnalysis
{
    public static MapAnalysis Build()
    {
        var thumb = new ThumbnailInfo(ChunkBytes: 1_000, JpegBytes: 950, Width: 512, Height: 512,
            CommentLength: 0, StrippableMetadataBytes: 120);

        var header = new HeaderAnalysis(
            TotalBytes: 2_000,
            Chunks:
            [
                new HeaderChunkInfo(0x03043007, "Thumbnail", 1_000, Heavy: true, FileOffset: 100),
                new HeaderChunkInfo(0x03043005, "XML", 900, Heavy: true, FileOffset: 1_100),
            ],
            Thumbnail: thumb,
            XmlLength: 900);

        var lightmap = new LightmapInfo(HasLightmaps: true, Version: 8, FrameCount: 3,
            Frames:
            [
                new LightmapFrameInfo(0, [300L, 200L, 100L])
                {
                    BlobDimensions = [new(512, 512), new(512, 512), new(512, 512)],
                },
                new LightmapFrameInfo(1, [300L, 200L, 100L])
                {
                    BlobDimensions = [new(512, 512), new(512, 512), new(512, 512)],
                },
                new LightmapFrameInfo(2, [300L, 200L, 100L])
                {
                    BlobDimensions = [new(512, 512), new(512, 512), new(512, 512)],
                },
            ],
            WebpBytesTotal: 1_800, ZlibCompressedBytes: 500, ZlibUncompressedBytes: 900, ChunkBytes: 2_350);

        var zip = new EmbeddedZipInfo(ZipBytes: 3_000, EntriesUncompressedBytes: 6_000,
            Entries:
            [
                new EmbeddedEntryInfo("Items/A.Item.Gbx", 2_000, 4_000, "Deflate",
                    IsGbx: true, HasCompressedGbxBody: true, RecompressibleSavingsEstimate: 500, IsReferenced: true),
                new EmbeddedEntryInfo("Items/Orphan.Item.Gbx", 1_000, 2_000, "Deflate",
                    IsGbx: true, HasCompressedGbxBody: false, RecompressibleSavingsEstimate: null, IsReferenced: false),
            ],
            ReferencedIdents: ["A"]);

        var chunks = new List<BodyChunkInfo>
        {
            new(0x0304301F, "Blocks", SizeCategory.Blocks, "block placements", 4_000,
                SizeConfidence.WriterDelta, BodyOffset: 0, Skippable: false, Order: 0),
            new(0x0304305B, "Lightmap", SizeCategory.Lightmap, "baked shadows", 2_350,
                SizeConfidence.ExactOnDisk, BodyOffset: 4_000, Skippable: true, Order: 1,
                PrecompressedPayloadBytes: 2_300),
            new(0x03043054, "Embedded items", SizeCategory.EmbeddedItems, "custom items zip", 3_100,
                SizeConfidence.ExactOnDisk, BodyOffset: 6_350, Skippable: true, Order: 2,
                PrecompressedPayloadBytes: 3_000),
        };

        var body = new BodyAnalysis(
            UncompressedBytes: 10_000, CompressedBytes: 7_000, Ratio: 0.7,
            Chunks: chunks, UnattributedBytes: 550,
            EmbeddedZip: zip, Lightmap: lightmap,
            ScriptMetadata: new ScriptMetadataInfo(400, 3, ["MapType", "Foo", "Bar"]),
            MediaTracker: new MediaTrackerInfo(200, 1, "intro clip"));

        var facts = new MapFacts("FAKEUID", "Fake Map", "fakeauthor",
            BlockCount: 100, AnchoredObjectCount: 50, BakedBlockCount: 10, FreeBlockCount: 2,
            HasLightmaps: true, LightmapFrameCount: 3, LightmapVersion: 8, EmbeddedEntryCount: 2);

        var tree = new SizeNode("file", "Fake Map.Map.Gbx", SizeCategory.Other, 12_000, 12_000, null,
            SizeConfidence.ExactOnDisk, null,
            [
                new SizeNode("header", "Header", SizeCategory.Header, 2_000, 2_000, null,
                    SizeConfidence.ExactOnDisk, null,
                    [
                        SizeNode.Leaf("header.thumbnail", "Thumbnail", SizeCategory.Thumbnail, 1_000,
                            SizeConfidence.ExactOnDisk, "JPEG 512x512", onDisk: 1_000),
                        SizeNode.Leaf("header.xml", "XML metadata", SizeCategory.Metadata, 900,
                            SizeConfidence.ExactOnDisk, onDisk: 900),
                    ]),
                new SizeNode("body", "Body", SizeCategory.Other, 10_000, 7_000, null,
                    SizeConfidence.ExactOnDisk, null,
                    [
                        SizeNode.Leaf("body.blocks", "Blocks", SizeCategory.Blocks, 4_000,
                            SizeConfidence.WriterDelta, "100 blocks", estOnDisk: 1_800),
                        SizeNode.Leaf("body.lightmap", "Lightmap", SizeCategory.Lightmap, 2_350,
                            SizeConfidence.ExactOnDisk, "3 frames", estOnDisk: 2_300),
                        SizeNode.Leaf("body.embedded", "Embedded items", SizeCategory.EmbeddedItems, 3_100,
                            SizeConfidence.ExactOnDisk, "2 entries", estOnDisk: 2_900),
                        SizeNode.Leaf("body.residual", "Unattributed residual", SizeCategory.Residual, 550,
                            SizeConfidence.Estimated),
                    ]),
            ]);

        return new MapAnalysis("Fake Map.Map.Gbx", 12_000, header, body, facts,
            [new AnalysisWarning("fake", "this analysis is synthetic")], tree);
    }
}
