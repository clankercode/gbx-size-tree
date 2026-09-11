using System.Buffers.Binary;
using System.IO.Compression;
using GBX.NET;
using GBX.NET.Serialization;
using GbxSizeTree.Measure;
using GbxSizeTree.Model;

namespace GbxSizeTree.Container;

/// <summary>Validated, byte-preserving single-entry removal trials; never a map resave.</summary>
internal sealed class EmbeddedZipRemovalPlan
{
    internal sealed record Entry(string Path, long ZipCompressedBytes, long ZipRawBytes);
    private readonly byte[] decompressedFile;
    private readonly int bodyOffset;
    private readonly int zipStart;
    private readonly int zipLength;
    private readonly RawZipRemoval archive;
    private readonly int chunkOffset;
    private readonly int payloadEnd;
    private readonly int zipLengthOffset;
    private readonly byte[] textureTail;
    private readonly EmbeddedItemIdentityPreserver.Snapshot identities;

    // Lazy: only the legacy byte-array paths materialize a standalone body copy.
    public byte[] OriginalBody => field ??= BodySpan.ToArray();
    public IReadOnlyList<Entry> Entries { get; }

    internal ReadOnlySpan<byte> BodySpan => decompressedFile.AsSpan(bodyOffset);
    internal int BodyLength => decompressedFile.Length - bodyOffset;

    // The ZIP lives inside the body; trials read the slice instead of a second copy.
    private ReadOnlySpan<byte> Zip => BodySpan.Slice(zipStart, zipLength);

    private EmbeddedZipRemovalPlan(byte[] decompressedFile, int bodyOffset, int zipStart, int zipLength, RawZipRemoval archive,
        int chunkOffset, int payloadEnd, int zipLengthOffset, byte[] textureTail,
        EmbeddedItemIdentityPreserver.Snapshot identities)
    {
        this.decompressedFile = decompressedFile;
        this.bodyOffset = bodyOffset;
        this.zipStart = zipStart;
        this.zipLength = zipLength;
        this.archive = archive;
        this.chunkOffset = chunkOffset;
        this.payloadEnd = payloadEnd;
        this.zipLengthOffset = zipLengthOffset;
        this.textureTail = textureTail;
        this.identities = identities;
        Entries = archive.Entries;
    }

    public static EmbeddedZipRemovalPlan Create(byte[] file, EmbeddedFileContributionOptions options)
    {
        var layout = GbxContainerReader.Read(file);
        if (layout.Version != 6 || layout.ClassId != 0x03043000 || layout.RefTableCompressed)
        {
            throw new NotSupportedException("Only version-6 maps with an uncompressed reference table are supported.");
        }
        var bodyLength = layout.BodyUncompressedSize ?? file.Length - layout.BodyOffset;
        if (bodyLength > options.MaxBodyBytes || file.LongLength - layout.BodyOffset > options.MaxBodyBytes)
        {
            throw new NotSupportedException("Body exceeds the measurement byte budget.");
        }
        var decompressed = DecompressedBody.GetDecompressedFile(file);
        var body = decompressed.Body;
        if (body.Length != bodyLength)
        {
            throw new InvalidDataException("Decompressed body does not match its declared length.");
        }

        // Also count hidden signatures: a scanner resynchronization alone cannot prove uniqueness.
        ReadOnlySpan<byte> signature = [0x54, 0x30, 0x04, 0x03, 0x50, 0x49, 0x4B, 0x53];
        var signatureOffset = body.IndexOf(signature);
        var duplicateSignature = signatureOffset >= 0
            && body[(signatureOffset + 1)..].IndexOf(signature) >= 0;
        var regions = new SkippableChunkScanner().Scan(decompressed.BodyMemory).Regions
            .Where(x => x.Kind == RawChunkKind.Skippable && x.ChunkId == 0x03043054).ToArray();
        if (signatureOffset < 0 || duplicateSignature || regions.Length != 1 || signatureOffset != regions[0].Offset)
        {
            throw new InvalidDataException("Embedded ZIP chunk span is missing or ambiguous.");
        }
        var region = regions[0];
        var start = checked((int)region.PayloadOffset);
        var end = checked(start + (int)region.PayloadLength);
        if (region.PayloadLength < 16 || ReadInt(body, start) is not (0 or 1)
            || ReadInt(body, start + 4) != 0 || ReadInt(body, start + 8) != region.PayloadLength - 12)
        {
            throw new InvalidDataException("Unsupported or malformed embedded chunk encapsulation.");
        }
        if (ReadInt(body, start + 12) < 0 || ReadInt(body, start + 12) > options.MaxZipEntries)
        {
            throw new NotSupportedException("Embedded identity count exceeds the measurement budget.");
        }
        using var stream = new MemoryStream(decompressed.Bytes, decompressed.BodyOffset + start, end - start, writable: false);
        using var reader = new GbxReader(stream);
        var version = reader.ReadInt32();
        Ident[] idents = [];
        List<string>? textures = null;
        var zipOffset = 0;
        var zipLength = 0;
        var tailOffset = 0;
        reader.ReadEncapsulated(inner =>
        {
            idents = inner.ReadArrayIdent();
            zipOffset = checked(start + (int)stream.Position);
            zipLength = ReadInt(decompressed.Bytes, decompressed.BodyOffset + zipOffset);
            if (zipLength < 0 || zipLength > end - zipOffset - 4)
            {
                throw new InvalidDataException("Embedded ZIP length exceeds its chunk span.");
            }
            inner.SkipData(4 + zipLength);
            tailOffset = checked(start + (int)stream.Position);
            if (version == 1)
            {
                var textureCount = ReadInt(decompressed.Bytes, decompressed.BodyOffset + tailOffset);
                if (textureCount < 0 || textureCount > (end - tailOffset - 4) / 4)
                {
                    throw new InvalidDataException("Embedded texture count exceeds its chunk span.");
                }
                textures = inner.ReadListString();
            }
            if (stream.Position != stream.Length)
            {
                throw new InvalidDataException("Unrecognized bytes in embedded chunk encapsulation.");
            }
        });
        if (zipOffset < start + 12 || tailOffset > end || zipOffset + 4 + zipLength != tailOffset)
        {
            throw new InvalidDataException("Embedded ZIP byte span could not be verified.");
        }
        var archive = RawZipRemoval.Parse(decompressed.Bytes, decompressed.BodyOffset + zipOffset + 4, zipLength, options);
        // Identities come from this same pass: a second decompression would double the transient load.
        var identities = EmbeddedItemIdentityPreserver.CaptureParsed(version, idents, textures,
            decompressed.Bytes, decompressed.BodyOffset + zipOffset + 4, zipLength);
        return new(decompressed.Bytes, decompressed.BodyOffset, zipOffset + 4, zipLength, archive,
            checked((int)region.Offset), end, zipOffset,
            body.Slice(tailOffset, end - tailOffset).ToArray(), identities);
    }

    public byte[] Remove(string path)
    {
        var buffer = new byte[BodyLength];
        var length = Remove(path, buffer);
        Array.Resize(ref buffer, length);
        return buffer;
    }

    /// <summary>Builds the single-entry removal trial into <paramref name="destination"/>; returns its length.</summary>
    public int Remove(string path, Span<byte> destination)
    {
        var trialZipLength = archive.TrialLength(path);
        var removesIdentity = identities.Entries.Any(x => string.Equals(x.Path, path, StringComparison.Ordinal));
        int prefixLength;
        if (!removesIdentity)
        {
            prefixLength = zipLengthOffset - chunkOffset - 12;
            BodySpan.Slice(chunkOffset + 12, prefixLength).CopyTo(destination[(chunkOffset + 12)..]);
        }
        else
        {
            using var stream = new MemoryStream();
            using (var writer = new GbxWriter(stream))
            {
                writer.Write(identities.Version);
                writer.WriteEncapsulated(inner => inner.WriteList(identities.Entries
                    .Where(x => !string.Equals(x.Path, path, StringComparison.Ordinal))
                    .Select(x => x.Model).ToList()));
            }
            prefixLength = checked((int)stream.Length);
            stream.GetBuffer().AsSpan(0, prefixLength).CopyTo(destination[(chunkOffset + 12)..]);
        }
        var payloadLength = checked(prefixLength + 4 + trialZipLength + textureTail.Length);
        var total = checked(chunkOffset + 12 + payloadLength + BodyLength - payloadEnd);
        BodySpan[..(chunkOffset + 12)].CopyTo(destination);
        WriteInt(destination, chunkOffset + 8, payloadLength);
        WriteInt(destination, chunkOffset + 20, payloadLength - 12);
        var cursor = chunkOffset + 12 + prefixLength;
        WriteInt(destination, cursor, trialZipLength);
        var written = archive.Remove(Zip, path, destination[(cursor + 4)..]);
        if (written != trialZipLength)
        {
            throw new InvalidDataException("Embedded ZIP removal trial length changed during construction.");
        }
        cursor += 4 + trialZipLength;
        textureTail.CopyTo(destination[cursor..]);
        cursor += textureTail.Length;
        BodySpan[payloadEnd..].CopyTo(destination[cursor..]);
        return total;
    }

    private static int ReadInt(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, 4));
    private static void WriteInt(Span<byte> bytes, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.Slice(offset, 4), value);
}

/// <summary>Classic single-disk ZIP surgery retaining compressed local records and central metadata.</summary>
internal sealed class RawZipRemoval
{
    private sealed record Record(int LocalOffset, int LocalLength, int CentralOffset, int CentralLength);
    private readonly Record[] records;
    private readonly int endOffset;
    private readonly int zipLength;
    public IReadOnlyList<EmbeddedZipRemovalPlan.Entry> Entries { get; }

    private RawZipRemoval(Record[] records, int endOffset, int zipLength, IReadOnlyList<EmbeddedZipRemovalPlan.Entry> entries)
    {
        this.records = records;
        this.endOffset = endOffset;
        this.zipLength = zipLength;
        Entries = entries;
    }

    public static RawZipRemoval Parse(byte[] body, int zipStart, int zipLength, EmbeddedFileContributionOptions options)
    {
        var zip = body.AsSpan(zipStart, zipLength);
        var endings = new List<int>();
        for (var i = Math.Max(0, zip.Length - 65557); i <= zip.Length - 22; i++)
        {
            if (U32(zip, i) == 0x06054B50 && i + 22 + U16(zip, i + 20) == zip.Length)
            {
                endings.Add(i);
            }
        }
        if (endings.Count != 1)
        {
            throw new InvalidDataException("ZIP end record is missing or ambiguous.");
        }
        var end = endings[0];
        var count = U16(zip, end + 10);
        var central = checked((int)U32(zip, end + 16));
        if (U16(zip, end + 4) != 0 || U16(zip, end + 6) != 0 || count == ushort.MaxValue
            || U16(zip, end + 8) != count || count > options.MaxZipEntries
            || (long)central + U32(zip, end + 12) != end)
        {
            throw new NotSupportedException("Unsupported ZIP framing or entry budget exceeded (ZIP64/multi-disk are not supported).");
        }
        using var stream = new MemoryStream(body, zipStart, zipLength, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        if (archive.Entries.Count != count || archive.Entries.Select(x => x.FullName).Distinct(StringComparer.Ordinal).Count() != count)
        {
            throw new InvalidDataException("ZIP entry paths or counts are ambiguous.");
        }
        var records = new Record[count];
        var entries = new EmbeddedZipRemovalPlan.Entry[count];
        var cursor = central;
        var localCursor = 0;
        long rawTotal = 0;
        for (var i = 0; i < count; i++)
        {
            Require(zip, cursor, 46);
            var flags = U16(zip, cursor + 8);
            var method = U16(zip, cursor + 10);
            var compressed = U32(zip, cursor + 20);
            var raw = U32(zip, cursor + 24);
            var nameLength = U16(zip, cursor + 28);
            var extraLength = U16(zip, cursor + 30);
            var recordLength = 46 + nameLength + extraLength + U16(zip, cursor + 32);
            Require(zip, cursor, recordLength);
            rawTotal = checked(rawTotal + raw);
            if (U32(zip, cursor) != 0x02014B50 || (flags & ~0x080E) != 0 || method is not (0 or 8)
                || compressed == uint.MaxValue || raw == uint.MaxValue || U16(zip, cursor + 34) != 0
                || U32(zip, cursor + 42) != localCursor || rawTotal > options.MaxZipRawBytes)
            {
                throw new NotSupportedException("Unsupported ZIP entry framing, ordering, encryption, or raw byte budget.");
            }
            ValidateExtra(zip, cursor + 46 + nameLength, extraLength);
            Require(zip, localCursor, 30);
            var localName = U16(zip, localCursor + 26);
            var localExtra = U16(zip, localCursor + 28);
            var localLength = checked(30 + localName + localExtra + (int)compressed);
            Require(zip, localCursor, localLength);
            if (U32(zip, localCursor) != 0x04034B50 || U16(zip, localCursor + 6) != flags
                || U16(zip, localCursor + 8) != method || localName != nameLength
                || !zip.Slice(localCursor + 30, localName).SequenceEqual(zip.Slice(cursor + 46, nameLength))
                || (method == 0 && compressed != raw))
            {
                throw new InvalidDataException("ZIP local and central records disagree.");
            }
            ValidateExtra(zip, localCursor + 30 + localName, localExtra);
            if ((flags & 8) != 0)
            {
                var descriptor = checked(localCursor + localLength);
                var signed = U32(zip, descriptor) == 0x08074B50;
                var data = descriptor + (signed ? 4 : 0);
                if (U32(zip, data) != U32(zip, cursor + 16) || U32(zip, data + 4) != compressed || U32(zip, data + 8) != raw)
                {
                    throw new InvalidDataException("ZIP data descriptor disagrees with the central record.");
                }
                localLength += signed ? 16 : 12;
            }
            else if (U32(zip, localCursor + 14) != U32(zip, cursor + 16)
                || U32(zip, localCursor + 18) != compressed || U32(zip, localCursor + 22) != raw)
            {
                throw new InvalidDataException("ZIP local sizes or CRC disagree with the central record.");
            }
            records[i] = new(localCursor, localLength, cursor, recordLength);
            entries[i] = new(archive.Entries[i].FullName, compressed, raw);
            localCursor = checked(localCursor + localLength);
            cursor += recordLength;
        }
        if (cursor != end || localCursor != central)
        {
            throw new InvalidDataException("ZIP record spans overlap or contain unrecognized bytes.");
        }
        return new(records, end, zipLength, entries);
    }

    public int TrialLength(string path)
    {
        var removed = records[IndexOf(path)];
        return zipLength - removed.LocalLength - removed.CentralLength;
    }

    private int IndexOf(string path) =>
        Enumerable.Range(0, Entries.Count).Single(i => string.Equals(Entries[i].Path, path, StringComparison.Ordinal));

    /// <summary>Writes the single-entry removal to <paramref name="destination"/>; returns the trial ZIP length.</summary>
    public int Remove(ReadOnlySpan<byte> zip, string path, Span<byte> destination)
    {
        var index = IndexOf(path);
        var removed = records[index];
        var cursor = 0;
        zip[..removed.LocalOffset].CopyTo(destination);
        cursor += removed.LocalOffset;
        var centralStart = records[^1].LocalOffset + records[^1].LocalLength;
        var middleLength = centralStart - removed.LocalOffset - removed.LocalLength;
        zip.Slice(removed.LocalOffset + removed.LocalLength, middleLength).CopyTo(destination[cursor..]);
        cursor += middleLength;
        var newCentral = cursor;
        for (var i = 0; i < records.Length; i++)
        {
            if (i == index)
            {
                continue;
            }
            var record = records[i];
            var bytes = zip.Slice(record.CentralOffset, record.CentralLength).ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(42), record.LocalOffset - (i > index ? removed.LocalLength : 0));
            bytes.CopyTo(destination[cursor..]);
            cursor += record.CentralLength;
        }
        var centralLength = cursor - newCentral;
        var end = zip[endOffset..].ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(end.AsSpan(8), checked((ushort)(records.Length - 1)));
        BinaryPrimitives.WriteUInt16LittleEndian(end.AsSpan(10), checked((ushort)(records.Length - 1)));
        BinaryPrimitives.WriteInt32LittleEndian(end.AsSpan(12), centralLength);
        BinaryPrimitives.WriteInt32LittleEndian(end.AsSpan(16), newCentral);
        end.CopyTo(destination[cursor..]);
        cursor += end.Length;
        return cursor;
    }

    private static void ValidateExtra(ReadOnlySpan<byte> zip, int offset, int length)
    {
        var end = offset + length;
        while (offset < end)
        {
            if (end - offset < 4 || U16(zip, offset) == 1)
            {
                throw new NotSupportedException("Malformed or ZIP64 extra field.");
            }
            offset += 4 + U16(zip, offset + 2);
        }
        if (offset != end)
        {
            throw new InvalidDataException("ZIP extra field exceeds its record.");
        }
    }

    private static void Require(ReadOnlySpan<byte> bytes, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > bytes.Length - length)
        {
            throw new InvalidDataException("Truncated ZIP record.");
        }
    }
    private static ushort U16(ReadOnlySpan<byte> bytes, int offset)
    {
        Require(bytes, offset, 2);
        return BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, 2));
    }
    private static uint U32(ReadOnlySpan<byte> bytes, int offset)
    {
        Require(bytes, offset, 4);
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));
    }
}
