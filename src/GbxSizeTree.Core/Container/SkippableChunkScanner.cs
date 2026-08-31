using System.Buffers.Binary;
using GbxSizeTree.Abstractions;
using GbxSizeTree.Model;

namespace GbxSizeTree.Container;

/// <summary>
/// Scans decompressed Gbx bodies using the anchored skippable-chunk framing documented in
/// docs/FORMAT-NOTES.md § Body chunk framing.
/// </summary>
public sealed class SkippableChunkScanner : IRawBodyScanner
{
    private const uint SkipMagic = 0x534B4950;
    private const uint TerminatorId = 0xFACADE01;

    public RawBodyScan Scan(ReadOnlyMemory<byte> decompressedBody)
    {
        var body = decompressedBody.Span;
        var regions = new List<RawChunkRegion>();
        var warnings = new List<AnalysisWarning>();
        var offset = 0;
        long unattributedBytes = 0;
        var resynchronized = false;

        while (offset < body.Length)
        {
            if (body.Length - offset >= sizeof(uint))
            {
                var chunkId = ReadUInt32(body, offset);

                if (chunkId == TerminatorId)
                {
                    regions.Add(new RawChunkRegion(
                        chunkId,
                        RawChunkKind.Terminator,
                        offset,
                        sizeof(uint),
                        offset + sizeof(uint),
                        0,
                        null));
                    offset += sizeof(uint);
                    continue;
                }

                if (TryReadSkippable(body, offset, out var region, out var encapsulationIssue))
                {
                    regions.Add(region);
                    if (encapsulationIssue is not null)
                    {
                        warnings.Add(new AnalysisWarning(
                            "raw-body-invalid-encapsulation",
                            $"Chunk 0x{chunkId:X8} at offset {offset} has an invalid encapsulation header: {encapsulationIssue}."));
                    }

                    offset += checked((int)region.Length);
                    continue;
                }

                if (HasSkipMagic(body, offset))
                {
                    warnings.Add(new AnalysisWarning(
                        "raw-body-invalid-skippable",
                        $"Chunk 0x{chunkId:X8} at offset {offset} has an invalid declared payload size."));
                }
            }
            else
            {
                warnings.Add(new AnalysisWarning(
                    "raw-body-trailing-bytes",
                    $"The decompressed body ends with {body.Length - offset} byte(s), too few for a chunk id."));
            }

            var gapStart = offset;
            var foundAnchor = TryFindNextSkippableAnchor(body, gapStart, out var nextAnchor);
            var gapEnd = foundAnchor ? nextAnchor : body.Length;
            var gapLength = gapEnd - gapStart;
            var gapChunkId = body.Length - gapStart >= sizeof(uint) ? ReadUInt32(body, gapStart) : 0;
            var payloadOffset = gapStart + Math.Min(sizeof(uint), gapLength);
            var payloadLength = Math.Max(0, gapLength - sizeof(uint));

            regions.Add(new RawChunkRegion(
                gapChunkId,
                RawChunkKind.NonSkippable,
                gapStart,
                gapLength,
                payloadOffset,
                payloadLength,
                null));
            unattributedBytes += gapLength;
            resynchronized = true;

            warnings.Add(new AnalysisWarning(
                foundAnchor ? "raw-body-resynchronized" : "raw-body-unattributed-tail",
                foundAnchor
                    ? $"Byte-scanned past {gapLength} non-skippable byte(s) at offset {gapStart}."
                    : $"No validated skippable anchor was found after offset {gapStart}; attributed the remaining {gapLength} byte(s) as one non-skippable region."));

            offset = gapEnd;
        }

        return new RawBodyScan(
            regions,
            decompressedBody.Length,
            unattributedBytes,
            resynchronized,
            warnings);
    }

    private static bool TryReadSkippable(
        ReadOnlySpan<byte> body,
        int offset,
        out RawChunkRegion region,
        out string? encapsulationIssue)
    {
        region = null!;
        encapsulationIssue = null;

        if (!HasSkipMagic(body, offset) || body.Length - offset < 12)
        {
            return false;
        }

        var payloadLength = ReadInt32(body, offset + 8);
        var regionLength = 12L + payloadLength;
        if (payloadLength < 0 || regionLength > body.Length - offset)
        {
            return false;
        }

        var chunkId = ReadUInt32(body, offset);
        var payloadOffset = offset + 12;
        region = new RawChunkRegion(
            chunkId,
            RawChunkKind.Skippable,
            offset,
            regionLength,
            payloadOffset,
            payloadLength,
            ReadEncapsulatedInnerSize(
                body,
                chunkId,
                payloadOffset,
                payloadLength,
                out encapsulationIssue));
        return true;
    }

    private static bool TryFindNextSkippableAnchor(
        ReadOnlySpan<byte> body,
        int gapStart,
        out int nextAnchor)
    {
        for (var candidate = gapStart + 1; candidate <= body.Length - 12; candidate++)
        {
            var candidateId = ReadUInt32(body, candidate);
            if (!HasPlausibleClassPrefix(candidateId)
                || !TryReadSkippable(body, candidate, out var candidateRegion, out _))
            {
                continue;
            }

            var followingOffset = checked(candidate + (int)candidateRegion.Length);
            if (IsValidFollowingAnchor(body, followingOffset))
            {
                nextAnchor = candidate;
                return true;
            }
        }

        nextAnchor = body.Length;
        return false;
    }

    private static bool IsValidFollowingAnchor(ReadOnlySpan<byte> body, int offset)
    {
        if (offset == body.Length)
        {
            return true;
        }

        if (body.Length - offset < sizeof(uint))
        {
            return false;
        }

        if (ReadUInt32(body, offset) == TerminatorId)
        {
            return true;
        }

        var chunkId = ReadUInt32(body, offset);
        return HasPlausibleClassPrefix(chunkId)
            && TryReadSkippable(body, offset, out _, out _);
    }

    private static int? ReadEncapsulatedInnerSize(
        ReadOnlySpan<byte> body,
        uint chunkId,
        int payloadOffset,
        int payloadLength,
        out string? issue)
    {
        issue = null;
        if (!IsEncapsulatedChunk(chunkId))
        {
            return null;
        }

        if (payloadLength < 12)
        {
            issue = $"payload has {payloadLength} byte(s), fewer than the 12-byte version/marker/size prefix";
            return null;
        }

        var zeroMarker = ReadInt32(body, payloadOffset + 4);
        if (zeroMarker != 0)
        {
            issue = $"expected zero marker but found {zeroMarker}";
            return null;
        }

        var innerSize = ReadInt32(body, payloadOffset + 8);
        if (innerSize < 0)
        {
            issue = $"inner size is negative ({innerSize})";
            return null;
        }

        var availableInnerBytes = payloadLength - 12;
        if (innerSize > availableInnerBytes)
        {
            issue = $"inner size {innerSize} exceeds the {availableInnerBytes} available payload byte(s)";
            return null;
        }

        return innerSize;
    }

    private static bool HasSkipMagic(ReadOnlySpan<byte> body, int offset) =>
        body.Length - offset >= 8 && ReadUInt32(body, offset + 4) == SkipMagic;

    private static bool HasPlausibleClassPrefix(uint chunkId) =>
        chunkId >> 24 is 0x03 or 0x2E;

    private static bool IsEncapsulatedChunk(uint chunkId) =>
        chunkId is 0x03043040 or 0x03043043 or 0x03043044 or 0x03043054;

    private static uint ReadUInt32(ReadOnlySpan<byte> body, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(body[offset..]);

    private static int ReadInt32(ReadOnlySpan<byte> body, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(body[offset..]);
}
