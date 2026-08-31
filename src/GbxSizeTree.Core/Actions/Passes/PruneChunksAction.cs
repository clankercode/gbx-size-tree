using GbxSizeTree.Model;

namespace GbxSizeTree.Actions.Passes;

/// <summary>
/// Removes body chunks the game provably discards on load, per the Ghidra-verified table in
/// <c>docs/FORMAT-NOTES.md</c> ("Small body chunks"): the reader parses each of these and
/// immediately destroys/clears the result, so their absence cannot change game state.
/// </summary>
public sealed class PruneChunksAction : IMapAction
{
    // 0x0304305E: legacy macroblock stub (reader parses records into temp nods, then frees them).
    // 0x03043061: write-side snapshot (reader explicitly zeroes every field it just read).
    // 0x03043064: media-clip-group stub (reader deserializes into a temp vector it destroys).
    private static readonly uint[] DeadChunkIds = [0x0304305E, 0x03043061, 0x03043064];

    public string Id => "prune-chunks";
    public string Title => "Prune dead chunks";
    public ActionTier Tier => ActionTier.Lossless;
    public string Consequence => "";
    public string HowToManually => "";
    public bool DefaultOn => true;
    public int Order => 5;

    public ActionApplicability Detect(ActionDetectContext ctx)
    {
        if (ctx.Analysis.Body is not { } body)
        {
            return ActionApplicability.No("map analysis has no parsed body");
        }

        var dead = body.Chunks.Where(chunk => DeadChunkIds.Contains(chunk.ChunkId)).ToArray();
        if (dead.Length == 0)
        {
            return ActionApplicability.No("no dead chunks present");
        }

        return new ActionApplicability(
            Applies: true,
            EstimatedSavingsBytes: dead.Sum(chunk => chunk.Bytes),
            Kind: EstimateKind.Computed,
            Reason: "the game discards these chunks on load (Ghidra-verified); exact uncompressed bytes",
            Detail: string.Join(", ", dead.Select(chunk => $"0x{chunk.ChunkId:X8} ({chunk.Bytes:N0} B)")));
    }

    public ActionResult Apply(ActionApplyContext ctx)
    {
        var dead = ctx.Map.Chunks.Where(chunk => DeadChunkIds.Contains(chunk.Id)).ToList();
        if (dead.Count == 0)
        {
            return ActionResult.NoChange("no dead chunks present");
        }

        foreach (var chunk in dead)
        {
            ctx.Map.Chunks.Remove(chunk);
        }

        return new ActionResult(
            Changed: true,
            Summary: $"removed {dead.Count} dead chunk(s)",
            Notes: dead.Select(chunk => $"removed 0x{chunk.Id:X8}").ToArray());
    }
}
