using GBX.NET.Engines.Game;
using GbxSizeTree.Model;

namespace GbxSizeTree.Abstractions;

public interface IEmbeddedZipDrilldown
{
    /// <summary>Null when the map has no embedded data.</summary>
    EmbeddedZipInfo? Inspect(CGameCtnChallenge map);
}

public interface ILightmapDrilldown
{
    /// <summary>
    /// Reads lightmap facts from the RAW chunk bytes (never the lazy parsed properties, which
    /// force a slow zlib parse). Null when the map has no 0x0304305B region.
    /// </summary>
    LightmapInfo? Inspect(ReadOnlyMemory<byte> decompressedBody, RawChunkRegion? lightmapRegion, CGameCtnChallenge map);
}

public interface IThumbnailReader
{
    /// <summary>Parses the 0x03043007 header chunk payload. Null when absent/empty.</summary>
    ThumbnailInfo? Read(ReadOnlyMemory<byte> chunkPayload);
}

public interface IScriptMetadataDrilldown
{
    ScriptMetadataInfo? Inspect(ReadOnlyMemory<byte> decompressedBody, RawChunkRegion? region, CGameCtnChallenge map);
}

public interface IMediaTrackerDrilldown
{
    MediaTrackerInfo? Inspect(CGameCtnChallenge map, long chunkBytes);
}

/// <summary>Seam for JPEG re-encode/downscale (ImageSharp-backed in the CLI project or Core).</summary>
public interface IJpegRecoder
{
    /// <summary>Returns re-encoded JPEG bytes, or null when not beneficial/possible.</summary>
    byte[]? Recode(byte[] jpeg, int? maxDimension, int quality);
}
