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
    private readonly byte[] zip;
    private readonly RawZipRemoval archive;
    private readonly int chunkOffset;
    private readonly int payloadEnd;
    private readonly int zipLengthOffset;
    private readonly byte[] textureTail;
    private readonly EmbeddedItemIdentityPreserver.Snapshot identities;

    public byte[] OriginalBody { get; }
    public IReadOnlyList<Entry> Entries { get; }

    private EmbeddedZipRemovalPlan(byte[] body, byte[] zip, RawZipRemoval archive,
        int chunkOffset, int payloadEnd, int zipLengthOffset, byte[] textureTail,
        EmbeddedItemIdentityPreserver.Snapshot identities)
    {
        OriginalBody = body;
        this.zip = zip;
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
        var body = DecompressedBody.GetBody(file);
        if (body.LongLength != bodyLength)
        {
            throw new InvalidDataException("Decompressed body does not match its declared length.");
        }

        // Also count hidden signatures: a scanner resynchronization alone cannot prove uniqueness.
        ReadOnlySpan<byte> signature = [0x54, 0x30, 0x04, 0x03, 0x50, 0x49, 0x4B, 0x53];
        var signatureOffset = body.AsSpan().IndexOf(signature);
        var duplicateSignature = signatureOffset >= 0
            && body.AsSpan(signatureOffset + 1).IndexOf(signature) >= 0;
        var regions = new SkippableChunkScanner().Scan(body).Regions
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
        using var stream = new MemoryStream(body.AsSpan(start, end - start).ToArray(), writable: false);
        using var reader = new GbxReader(stream);
        var version = reader.ReadInt32();
        byte[] zip = [];
        var zipOffset = 0;
        var tailOffset = 0;
        reader.ReadEncapsulated(inner =>
        {
            inner.ReadArrayIdent();
            zipOffset = checked(start + (int)stream.Position);
            var zipLength = ReadInt(body, zipOffset);
            if (zipLength < 0 || zipLength > end - zipOffset - 4)
            {
                throw new InvalidDataException("Embedded ZIP length exceeds its chunk span.");
            }
            zip = inner.ReadData();
            tailOffset = checked(start + (int)stream.Position);
            if (version == 1)
            {
                var textureCount = ReadInt(body, tailOffset);
                if (textureCount < 0 || textureCount > (end - tailOffset - 4) / 4)
                {
                    throw new InvalidDataException("Embedded texture count exceeds its chunk span.");
                }
                inner.ReadListString();
            }
            if (stream.Position != stream.Length)
            {
                throw new InvalidDataException("Unrecognized bytes in embedded chunk encapsulation.");
            }
        });
        if (zipOffset < start + 12 || tailOffset > end || ReadInt(body, zipOffset) != zip.Length
            || !body.AsSpan(zipOffset + 4, zip.Length).SequenceEqual(zip))
        {
            throw new InvalidDataException("Embedded ZIP byte span could not be verified.");
        }
        var archive = RawZipRemoval.Parse(zip, options);
        var identities = EmbeddedItemIdentityPreserver.Capture(file);
        return new(body, zip, archive, checked((int)region.Offset), end, zipOffset,
            body.AsSpan(tailOffset, end - tailOffset).ToArray(), identities);
    }

    public byte[] Remove(string path)
    {
        var trialZip = archive.Remove(zip, path);
        var removesIdentity = identities.Entries.Any(x => string.Equals(x.Path, path, StringComparison.Ordinal));
        byte[] prefix;
        if (!removesIdentity)
        {
            prefix = OriginalBody.AsSpan(chunkOffset + 12, zipLengthOffset - chunkOffset - 12).ToArray();
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
            prefix = stream.ToArray();
        }
        var payloadLength = checked(prefix.Length + 4 + trialZip.Length + textureTail.Length);
        var result = new byte[checked(chunkOffset + 12 + payloadLength + OriginalBody.Length - payloadEnd)];
        OriginalBody.AsSpan(0, chunkOffset + 12).CopyTo(result);
        WriteInt(result, chunkOffset + 8, payloadLength);
        prefix.CopyTo(result, chunkOffset + 12);
        WriteInt(result, chunkOffset + 20, payloadLength - 12);
        var cursor = chunkOffset + 12 + prefix.Length;
        WriteInt(result, cursor, trialZip.Length);
        trialZip.CopyTo(result, cursor + 4);
        textureTail.CopyTo(result, cursor + 4 + trialZip.Length);
        OriginalBody.AsSpan(payloadEnd).CopyTo(result.AsSpan(chunkOffset + 12 + payloadLength));
        return result;
    }

    private static int ReadInt(byte[] bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));
    private static void WriteInt(byte[] bytes, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), value);
}

/// <summary>Classic single-disk ZIP surgery retaining compressed local records and central metadata.</summary>
internal sealed class RawZipRemoval
{
    private sealed record Record(int LocalOffset, int LocalLength, int CentralOffset, int CentralLength);
    private readonly Record[] records;
    private readonly int endOffset;
    public IReadOnlyList<EmbeddedZipRemovalPlan.Entry> Entries { get; }

    private RawZipRemoval(Record[] records, int endOffset, IReadOnlyList<EmbeddedZipRemovalPlan.Entry> entries)
    {
        this.records = records;
        this.endOffset = endOffset;
        Entries = entries;
    }

    public static RawZipRemoval Parse(byte[] zip, EmbeddedFileContributionOptions options)
    {
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
        using var stream = new MemoryStream(zip, writable: false);
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
                || !zip.AsSpan(localCursor + 30, localName).SequenceEqual(zip.AsSpan(cursor + 46, nameLength))
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
        return new(records, end, entries);
    }

    public byte[] Remove(byte[] zip, string path)
    {
        var index = Enumerable.Range(0, Entries.Count).Single(i => string.Equals(Entries[i].Path, path, StringComparison.Ordinal));
        var removed = records[index];
        using var output = new MemoryStream(zip.Length - removed.LocalLength - removed.CentralLength);
        output.Write(zip.AsSpan(0, removed.LocalOffset));
        var centralStart = records.Length == 0 ? 0 : records[^1].LocalOffset + records[^1].LocalLength;
        output.Write(zip.AsSpan(removed.LocalOffset + removed.LocalLength, centralStart - removed.LocalOffset - removed.LocalLength));
        var newCentral = checked((int)output.Position);
        for (var i = 0; i < records.Length; i++)
        {
            if (i == index)
            {
                continue;
            }
            var record = records[i];
            var bytes = zip.AsSpan(record.CentralOffset, record.CentralLength).ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(42), record.LocalOffset - (i > index ? removed.LocalLength : 0));
            output.Write(bytes);
        }
        var centralLength = checked((int)output.Position - newCentral);
        var end = zip.AsSpan(endOffset).ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(end.AsSpan(8), checked((ushort)(records.Length - 1)));
        BinaryPrimitives.WriteUInt16LittleEndian(end.AsSpan(10), checked((ushort)(records.Length - 1)));
        BinaryPrimitives.WriteInt32LittleEndian(end.AsSpan(12), centralLength);
        BinaryPrimitives.WriteInt32LittleEndian(end.AsSpan(16), newCentral);
        output.Write(end);
        return output.ToArray();
    }

    private static void ValidateExtra(byte[] zip, int offset, int length)
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

    private static void Require(byte[] bytes, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > bytes.Length - length)
        {
            throw new InvalidDataException("Truncated ZIP record.");
        }
    }
    private static ushort U16(byte[] bytes, int offset)
    {
        Require(bytes, offset, 2);
        return BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    }
    private static uint U32(byte[] bytes, int offset)
    {
        Require(bytes, offset, 4);
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    }
}
