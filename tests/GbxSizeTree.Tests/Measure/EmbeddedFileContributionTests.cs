using System.Buffers.Binary;
using System.IO.Compression;
using GBX.NET;
using GBX.NET.LZO;
using GBX.NET.Serialization;
using GbxSizeTree.Container;
using GbxSizeTree.Measure;
using GbxSizeTree.Tests.Fixtures;

namespace GbxSizeTree.Tests.Measure;

public sealed class EmbeddedFileContributionTests
{
    [Theory]
    [InlineData(CompressionLevel.NoCompression)]
    [InlineData(CompressionLevel.SmallestSize)]
    public void Removal_PreservesUnrelatedBodyBytesAndRetainedZipPayload(CompressionLevel level)
    {
        var zip = BuildZip(level, ("remove.bin", Data(4096)), ("keep.bin", Data(8192)));
        var file = BuildFile(zip);
        var untouched = file.ToArray();
        var plan = EmbeddedZipRemovalPlan.Create(file, new());
        var trial = plan.Remove("remove.bin");
        var originalBody = DecompressedBody.GetBody(file);
        var originalRegion = EmbeddedRegion(originalBody);
        var trialRegion = EmbeddedRegion(trial);
        Assert.Equal(originalBody[..(int)originalRegion.Offset], trial[..(int)trialRegion.Offset]);
        Assert.Equal(originalBody[(int)(originalRegion.Offset + originalRegion.Length)..],
            trial[(int)(trialRegion.Offset + trialRegion.Length)..]);
        var (trialZip, textures) = ReadPayload(trial);
        Assert.Equal(new[] { "Textures/preserved.dds", "Textures/preserved.dds" }, textures);
        using var archive = new ZipArchive(new MemoryStream(trialZip), ZipArchiveMode.Read);
        var retained = Assert.Single(archive.Entries);
        Assert.Equal("keep.bin", retained.FullName);
        using var bytes = new MemoryStream();
        retained.Open().CopyTo(bytes);
        Assert.Equal(Data(8192), bytes.ToArray());
        Assert.Equal(LocalRecord(zip, 1), LocalRecord(trialZip, 0));
        Assert.Equal(untouched, file);
    }

    [Fact]
    public void Measure_UsesOriginalBodyBaselineAndIndependentRemovalTrials()
    {
        var file = BuildFile(BuildZip(CompressionLevel.NoCompression,
            ("a.bin", Data(8192)), ("b.bin", Data(8192))));
        var result = new EmbeddedFileContributionMeasurer().Measure(file, ["a.bin", "b.bin"], cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(result.UnavailableReason);
        var plan = EmbeddedZipRemovalPlan.Create(file, new());
        var baseline = new Lzo().Compress(DecompressedBody.GetBody(file)).LongLength;
        Assert.Equal(baseline, result.BaselineCompressedBodyBytes);
        Assert.All(result.Entries, entry =>
        {
            Assert.Null(entry.UnavailableReason);
            Assert.Equal(8192, entry.ZipRawBytes);
            Assert.Equal(8192, entry.ZipCompressedBytes);
            Assert.Equal(baseline - new Lzo().Compress(plan.Remove(entry.Path)).LongLength,
                entry.MarginalCompressedBodyBytes);
        });
        var emptyFile = BuildFile(BuildZip(CompressionLevel.NoCompression));
        var allRemovedSavings = baseline - new Lzo().Compress(DecompressedBody.GetBody(emptyFile)).LongLength;
        Assert.NotEqual(allRemovedSavings, result.Entries.Sum(x => x.MarginalCompressedBodyBytes));
    }

    [Fact]
    public void Measure_DefaultTrialCapMeasuresMoreThanEightEntries()
    {
        var calls = 0;
        var measurer = new EmbeddedFileContributionMeasurer(body =>
        {
            calls++;
            return body.LongLength;
        });
        var paths = Enumerable.Range(0, 10).Select(i => $"asset{i:D2}.bin").ToArray();
        var file = BuildFile(BuildZip(CompressionLevel.NoCompression,
            paths.Select(path => (path, Data(32))).ToArray()));

        var result = measurer.Measure(file, paths, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(11, calls);
        Assert.All(result.Entries, entry =>
        {
            Assert.NotNull(entry.MarginalCompressedBodyBytes);
            Assert.Null(entry.UnavailableReason);
        });
    }

    [Fact]
    public void Measure_ExplicitLowTrialCapIsPreserved()
    {
        var calls = 0;
        var measurer = new EmbeddedFileContributionMeasurer(body =>
        {
            calls++;
            return body.LongLength;
        });
        var paths = Enumerable.Range(0, 10).Select(i => $"asset{i:D2}.bin").ToArray();
        var file = BuildFile(BuildZip(CompressionLevel.NoCompression,
            paths.Select(path => (path, Data(32))).ToArray()));

        var result = measurer.Measure(file, paths, new() { MaxTrials = 3 }, TestContext.Current.CancellationToken);

        Assert.Equal(4, calls);
        Assert.All(result.Entries.Take(3), entry =>
        {
            Assert.NotNull(entry.MarginalCompressedBodyBytes);
            Assert.Null(entry.UnavailableReason);
        });
        Assert.All(result.Entries.Skip(3), entry =>
        {
            Assert.Null(entry.MarginalCompressedBodyBytes);
            Assert.Equal("Removal trial budget exhausted.", entry.UnavailableReason);
        });
    }

    [Fact]
    public void Measure_PreservesNegativeMeasurementsAndBoundsTrialsToRequestedPaths()
    {
        var seen = new List<byte[]>();
        var measurer = new EmbeddedFileContributionMeasurer(body =>
        {
            seen.Add(body.ToArray());
            return seen.Count == 1 ? 10 : 20;
        });
        var file = BuildFile(BuildZip(CompressionLevel.SmallestSize,
            ("a", Data(200)), ("b", Data(300)), ("c", Data(400))));
        var result = measurer.Measure(file, ["b", "b", "missing", "c"], new() { MaxTrials = 1 }, TestContext.Current.CancellationToken);
        Assert.Equal(2, seen.Count);
        Assert.Equal(DecompressedBody.GetBody(file), seen[0]);
        Assert.Equal(-10, result.Entries.Single(x => x.Path == "b").MarginalCompressedBodyBytes);
        Assert.NotNull(result.Entries.Single(x => x.Path == "missing").UnavailableReason);
        var skipped = result.Entries.Single(x => x.Path == "c");
        Assert.NotNull(skipped.UnavailableReason);
        Assert.Equal(400, skipped.ZipRawBytes);
        Assert.Null(skipped.MarginalCompressedBodyBytes);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("malformed")]
    [InlineData("unsupported")]
    [InlineData("budget")]
    public void Measure_UnsafeInputIsExplicitlyUnavailableWithoutCompressing(string kind)
    {
        var file = BuildFile(BuildZip(CompressionLevel.NoCompression, ("a", Data(100))), duplicateChunk: kind == "duplicate");
        if (kind == "malformed")
        {
            var region = EmbeddedRegion(DecompressedBody.GetBody(file));
            file = file[..(25 + (int)region.PayloadOffset + 20)];
        }
        if (kind == "unsupported")
        {
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(9), 0x2E001000);
        }
        var options = new EmbeddedFileContributionOptions { MaxBodyBytes = kind == "budget" ? 1 : 256 * 1024 * 1024 };
        var calls = 0;
        var result = new EmbeddedFileContributionMeasurer(_ => { calls++; return 1; }).Measure(file, ["a"], options, TestContext.Current.CancellationToken);
        Assert.NotNull(result.UnavailableReason);
        Assert.Null(result.BaselineCompressedBodyBytes);
        Assert.Null(Assert.Single(result.Entries).MarginalCompressedBodyBytes);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("duplicate-path")]
    [InlineData("bad-local-size")]
    [InlineData("bad-central-offset")]
    [InlineData("encrypted")]
    [InlineData("zip64")]
    [InlineData("truncated")]
    public void Measure_UnsupportedZipCannotProduceAnOuterContribution(string kind)
    {
        var zip = BuildZip(CompressionLevel.NoCompression, ("a", Data(80)), (kind == "duplicate-path" ? "a" : "b", Data(90)));
        var central = (int)BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(zip.Length - 6));
        if (kind == "bad-local-size")
        {
            BinaryPrimitives.WriteInt32LittleEndian(zip.AsSpan(18), 79);
        }
        if (kind == "bad-central-offset")
        {
            BinaryPrimitives.WriteInt32LittleEndian(zip.AsSpan(central + 42), 1);
        }
        if (kind == "encrypted")
        {
            BinaryPrimitives.WriteUInt16LittleEndian(zip.AsSpan(central + 8), 1);
        }
        if (kind == "zip64")
        {
            BinaryPrimitives.WriteUInt32LittleEndian(zip.AsSpan(central + 24), uint.MaxValue);
        }
        if (kind == "truncated")
        {
            zip = zip[..^1];
        }
        var result = new EmbeddedFileContributionMeasurer().Measure(BuildFile(zip), ["a"],
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(result.UnavailableReason);
        Assert.Null(Assert.Single(result.Entries).MarginalCompressedBodyBytes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Removal_HandlesSignedDataDescriptorsAndLastEntry(int index)
    {
        var zip = BuildZip(CompressionLevel.SmallestSize, ("a", Data(200)), ("b", Data(300)));
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            var central = (int)BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(zip.Length - 6));
            var offset = 0;
            for (var i = 0; i < 2; i++)
            {
                var record = LocalRecord(zip, i);
                BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(6), 8);
                var descriptor = record.AsSpan(14, 12).ToArray();
                record.AsSpan(14, 12).Clear();
                writer.Write(record);
                writer.Write(0x08074B50U);
                writer.Write(descriptor);
                offset += record.Length;
            }
            Assert.Equal(central, offset);
            var tail = zip[central..];
            var cursor = 0;
            for (var i = 0; i < 2; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(tail.AsSpan(cursor + 8), 8);
                var oldOffset = BinaryPrimitives.ReadInt32LittleEndian(tail.AsSpan(cursor + 42));
                BinaryPrimitives.WriteInt32LittleEndian(tail.AsSpan(cursor + 42), oldOffset + i * 16);
                cursor += 46 + BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(cursor + 28))
                    + BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(cursor + 30))
                    + BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(cursor + 32));
            }
            BinaryPrimitives.WriteInt32LittleEndian(tail.AsSpan(cursor + 16), central + 32);
            writer.Write(tail);
        }
        var file = BuildFile(stream.ToArray());
        var trial = EmbeddedZipRemovalPlan.Create(file, new()).Remove(index == 0 ? "a" : "b");
        var (trialZip, _) = ReadPayload(trial);
        using var archive = new ZipArchive(new MemoryStream(trialZip), ZipArchiveMode.Read);
        Assert.Equal(index == 0 ? "b" : "a", Assert.Single(archive.Entries).FullName);
        var empty = EmbeddedZipRemovalPlan.Create(WrapBody(trial), new()).Remove(index == 0 ? "b" : "a");
        var (emptyZip, _) = ReadPayload(empty);
        using var emptyArchive = new ZipArchive(new MemoryStream(emptyZip), ZipArchiveMode.Read);
        Assert.Empty(emptyArchive.Entries);
    }

    [Theory]
    [InlineData("identity-count")]
    [InlineData("zip-length")]
    [InlineData("texture-count")]
    [InlineData("inner-length")]
    [InlineData("hidden-signature")]
    public void Measure_MalformedOrAmbiguousChunkCannotProduceMeasurement(string kind)
    {
        var file = BuildFile(BuildZip(CompressionLevel.NoCompression, ("a", Data(80))));
        var body = DecompressedBody.GetBody(file);
        var region = EmbeddedRegion(body);
        var start = (int)region.PayloadOffset;
        var zipOffset = start + 16;
        var zipLength = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(zipOffset));
        var offset = kind switch
        {
            "identity-count" => start + 12,
            "zip-length" => zipOffset,
            "texture-count" => zipOffset + 4 + zipLength,
            _ => start + 8,
        };
        if (kind == "hidden-signature")
        {
            new byte[] { 0x54, 0x30, 0x04, 0x03, 0x50, 0x49, 0x4B, 0x53 }.CopyTo(body, 16);
        }
        else
        {
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(offset), int.MaxValue);
        }
        var result = new EmbeddedFileContributionMeasurer().Measure(WrapBody(body), ["a"],
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(result.UnavailableReason);
        Assert.Null(Assert.Single(result.Entries).MarginalCompressedBodyBytes);
    }

    [Fact]
    public void Measure_NoRequestedChangesDoesNoWorkAndCancellationPropagates()
    {
        var calls = 0;
        var measurer = new EmbeddedFileContributionMeasurer(_ => { calls++; return 1; });
        var result = measurer.Measure([], [], cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(result.Entries);
        Assert.Null(result.UnavailableReason);
        Assert.Equal(0, calls);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => measurer.Measure([], ["a"], cancellationToken: cancellation.Token));
    }

    [Fact]
    public void Measure_CompressionFailureKeepsZipMetadataAndReportsUnavailable()
    {
        var file = BuildFile(BuildZip(CompressionLevel.NoCompression, ("a", Data(80)), ("b", Data(90))));
        var calls = 0;
        var result = new EmbeddedFileContributionMeasurer(_ => { calls++; throw new InvalidDataException("codec failed"); })
            .Measure(file, ["a", "b"], cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, calls);
        Assert.Equal("codec failed", result.UnavailableReason);
        Assert.All(result.Entries, entry =>
        {
            Assert.NotNull(entry.ZipRawBytes);
            Assert.NotNull(entry.ZipCompressedBytes);
            Assert.Null(entry.MarginalCompressedBodyBytes);
            Assert.Equal("codec failed", entry.UnavailableReason);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Sample_RemovalProjectsAliasesWithSharedIdentifierStrings(int removeIndex)
    {
        SampleMap.SkipUnlessAvailable();
        var source = File.ReadAllBytes(SampleMap.Path);
        var snapshot = EmbeddedItemIdentityPreserver.Capture(source);
        Assert.NotEmpty(snapshot.Entries);
        var (zipBytes, _) = ReadPayload(DecompressedBody.GetBody(source));
        using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        using var itemBytes = new MemoryStream();
        using (var input = archive.GetEntry(snapshot.Entries[0].Path)!.Open())
        {
            input.CopyTo(itemBytes);
        }
        var models = new List<Ident> { new("alias-one", "Stadium", "same-author"), new("alias-two", "Stadium", "same-author") };
        var file = BuildFile(BuildZip(CompressionLevel.SmallestSize,
            ("Items/first.Item.Gbx", itemBytes.ToArray()), ("texture.dds", Data(30)),
            ("Items/second.Item.Gbx", itemBytes.ToArray())), models: models);
        var before = EmbeddedItemIdentityPreserver.Capture(file);
        var trial = EmbeddedZipRemovalPlan.Create(file, new()).Remove(before.Entries[removeIndex].Path);
        var after = EmbeddedItemIdentityPreserver.Capture(WrapBody(trial));
        Assert.Equal(models[1 - removeIndex], Assert.Single(after.Entries).Model);
        Assert.Equal(before.Textures, after.Textures);
    }

    [Fact]
    public void Sample_OriginalRawBodyIsMeasuredWithoutResavingMap()
    {
        SampleMap.SkipUnlessAvailable();
        VerifyRealMap(SampleMap.Path);
    }

    [Theory]
    [InlineData("Sweet 2 burger v205.Map.gbx")]
    [InlineData("Sweet 2 burger v206.Map.Gbx")]
    public void RealMap_PreservesRemainingIdentitiesAndTextures(string name)
    {
        var root = Environment.GetEnvironmentVariable("GBX_SIZE_TREE_SB2");
        Assert.SkipUnless(root is not null && File.Exists(Path.Combine(root, name)), "set GBX_SIZE_TREE_SB2 for user-map regression tests");
        VerifyRealMap(Path.Combine(root!, name));
    }

    private static void VerifyRealMap(string path)
    {
        var file = File.ReadAllBytes(path);
        var original = file.ToArray();
        var plan = EmbeddedZipRemovalPlan.Create(file, new());
        var entry = plan.Entries.First();
        var result = new EmbeddedFileContributionMeasurer().Measure(file, [entry.Path], cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(result.UnavailableReason);
        Assert.Null(Assert.Single(result.Entries).UnavailableReason);
        Assert.Equal(new Lzo().Compress(DecompressedBody.GetBody(file)).LongLength, result.BaselineCompressedBodyBytes);
        var before = EmbeddedItemIdentityPreserver.Capture(file);
        var trialBody = plan.Remove(entry.Path);
        var after = EmbeddedItemIdentityPreserver.Capture(WrapBody(trialBody));
        Assert.Equal(before.Entries.Where(x => x.Path != entry.Path), after.Entries);
        Assert.Equal(before.Textures, after.Textures);
        var beforeRegion = EmbeddedRegion(DecompressedBody.GetBody(file));
        var afterRegion = EmbeddedRegion(trialBody);
        Assert.Equal(DecompressedBody.GetBody(file)[..(int)beforeRegion.Offset], trialBody[..(int)afterRegion.Offset]);
        Assert.Equal(DecompressedBody.GetBody(file)[(int)(beforeRegion.Offset + beforeRegion.Length)..],
            trialBody[(int)(afterRegion.Offset + afterRegion.Length)..]);
        Assert.Equal(original, file);
        TestContext.Current.TestOutputHelper!.WriteLine($"{Path.GetFileName(path)}: {entry.Path}; baseline={result.BaselineCompressedBodyBytes}; marginal={result.Entries[0].MarginalCompressedBodyBytes}; zip={entry.ZipCompressedBytes}; raw={entry.ZipRawBytes}");
    }

    private static GbxSizeTree.Model.RawChunkRegion EmbeddedRegion(byte[] body) =>
        new SkippableChunkScanner().Scan(body).Regions.Single(x => x.ChunkId == 0x03043054);

    private static (byte[] Zip, List<string>? Textures) ReadPayload(byte[] body)
    {
        var region = EmbeddedRegion(body);
        using var stream = new MemoryStream(body.AsSpan((int)region.PayloadOffset, (int)region.PayloadLength).ToArray());
        using var reader = new GbxReader(stream);
        Assert.Equal(1, reader.ReadInt32());
        byte[] zip = [];
        List<string>? textures = null;
        reader.ReadEncapsulated(inner =>
        {
            inner.ReadArrayIdent();
            zip = inner.ReadData();
            textures = inner.ReadListString();
        });
        return (zip, textures);
    }

    private static byte[] BuildFile(byte[] zip, bool duplicateChunk = false, List<Ident>? models = null)
    {
        using var payload = new MemoryStream();
        using (var writer = new GbxWriter(payload))
        {
            writer.Write(1);
            writer.WriteEncapsulated(inner =>
            {
                inner.WriteList(models ?? []);
                inner.WriteData(zip);
                inner.WriteList(new List<string> { "Textures/preserved.dds", "Textures/preserved.dds" });
            });
        }
        using var body = new MemoryStream();
        using (var writer = new BinaryWriter(body, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0x03043011U);
            writer.Write(0x534B4950U);
            writer.Write(57);
            writer.Write(Data(57));
            for (var i = 0; i < (duplicateChunk ? 2 : 1); i++)
            {
                writer.Write(0x03043054U);
                writer.Write(0x534B4950U);
                writer.Write((int)payload.Length);
                writer.Write(payload.ToArray());
            }
            writer.Write(0x03043012U);
            writer.Write(0x534B4950U);
            writer.Write(91);
            writer.Write(Data(91));
            writer.Write(0xFACADE01U);
        }
        return WrapBody(body.ToArray());
    }

    private static byte[] WrapBody(byte[] body)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("GBX"u8);
        writer.Write((short)6);
        writer.Write("BUUR"u8);
        writer.Write(0x03043000U);
        writer.Write(0);
        writer.Write(1);
        writer.Write(0);
        writer.Write(body);
        return stream.ToArray();
    }

    private static byte[] BuildZip(CompressionLevel level, params (string Path, byte[] Data)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, data) in entries)
            {
                var entry = archive.CreateEntry(path, level);
                entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var output = entry.Open();
                output.Write(data);
            }
        }
        return stream.ToArray();
    }

    private static byte[] Data(int size)
    {
        var bytes = new byte[size];
        new Random(42).NextBytes(bytes);
        return bytes;
    }

    private static byte[] LocalRecord(byte[] zip, int index)
    {
        var offset = 0;
        for (var i = 0; ; i++)
        {
            var length = 30 + BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(offset + 26))
                + BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(offset + 28))
                + (int)BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(offset + 18));
            if (i == index)
            {
                return zip.AsSpan(offset, length).ToArray();
            }
            offset += length;
        }
    }
}
