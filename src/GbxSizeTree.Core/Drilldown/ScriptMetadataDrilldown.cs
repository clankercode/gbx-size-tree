using GBX.NET.Engines.Game;
using GbxSizeTree.Abstractions;
using GbxSizeTree.Model;

namespace GbxSizeTree.Drilldown;

/// <summary>
/// Reports the parsed script-trait metadata stored in map chunk 0x03043044; see
/// <c>docs/FORMAT-NOTES.md</c>.
/// </summary>
public sealed class ScriptMetadataDrilldown : IScriptMetadataDrilldown
{
    private const int TraitNameLimit = 20;

    public ScriptMetadataInfo? Inspect(
        ReadOnlyMemory<byte> decompressedBody,
        RawChunkRegion? region,
        CGameCtnChallenge map)
    {
        if (region is null)
        {
            return null;
        }

        var traits = map.ScriptMetadata?.Traits;
        if (traits is null)
        {
            return new ScriptMetadataInfo(region.Length, null, []);
        }

        var traitNames = traits.Keys.Take(TraitNameLimit).ToArray();
        return new ScriptMetadataInfo(region.Length, traits.Count, traitNames);
    }
}
