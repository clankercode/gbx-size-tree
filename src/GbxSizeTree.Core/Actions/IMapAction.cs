using GBX.NET;
using GBX.NET.Engines.Game;
using GbxSizeTree.Abstractions;
using GbxSizeTree.Model;

namespace GbxSizeTree.Actions;

public enum ActionTier
{
    /// <summary>T1: byte-different, semantically identical. Default-on.</summary>
    Lossless = 1,
    /// <summary>T2: gameplay identical, cosmetic/editor-side loss. Explicit opt-in.</summary>
    BenignLossy = 2,
    /// <summary>T3: changes actual data. Hidden unless --experimental.</summary>
    Experimental = 3,
    /// <summary>Detect-only: the tool cannot apply it; surfaced as an editor recommendation.</summary>
    EditorOnly = 4,
}

public enum EstimateKind
{
    /// <summary>The optimized result was actually produced and measured.</summary>
    Measured,
    /// <summary>Computed exactly from known sizes (e.g. chunk removal).</summary>
    Computed,
    Heuristic,
    /// <summary>Only known after a full save (e.g. LZO999 resave).</summary>
    MeasuredOnSave,
    Unknown,
    NotImplemented,
}

public sealed record ActionApplicability(
    bool Applies,
    long EstimatedSavingsBytes,
    EstimateKind Kind,
    string Reason,
    string? Detail = null)
{
    public static ActionApplicability No(string reason) => new(false, 0, EstimateKind.Unknown, reason);
}

public sealed record ActionResult(bool Changed, string Summary, IReadOnlyList<string> Notes)
{
    public static ActionResult NoChange(string summary) => new(false, summary, []);
}

/// <summary>
/// <paramref name="Map"/> is present when the caller holds a parsed map (batch/interactive) —
/// actions may use it for MEASURED trial estimates; when null (plain report mode) they must
/// degrade to computed/heuristic estimates from <paramref name="Analysis"/> alone.
/// <paramref name="ResaveTrial"/> is the background trial resave the frontends start right
/// after reading the file, when available.
/// </summary>
public sealed record ActionDetectContext(
    MapAnalysis Analysis,
    IReadOnlyDictionary<string, string> Settings,
    IStatusSink Status,
    GBX.NET.Engines.Game.CGameCtnChallenge? Map = null,
    Measure.ResaveTrial? ResaveTrial = null);

public sealed record ActionApplyContext(
    Gbx Gbx,
    CGameCtnChallenge Map,
    MapAnalysis Analysis,
    IReadOnlyDictionary<string, string> Settings,
    IStatusSink Status);

/// <summary>
/// One optimization in the shared action library, consumed by all three frontends
/// (report recommendations, batch flags, interactive menu). Apply mutates the in-memory
/// node graph only — it never writes files. Actions MUST be deterministic functions of
/// (map state, settings): session undo is reload-original + replay by ascending Order.
/// </summary>
public interface IMapAction
{
    /// <summary>Stable kebab-case id: used as CLI value, JSON key, and menu key.</summary>
    string Id { get; }
    string Title { get; }
    ActionTier Tier { get; }
    /// <summary>One line shown next to the action; empty for lossless.</summary>
    string Consequence { get; }
    /// <summary>Manual editor steps; the whole story for EditorOnly actions.</summary>
    string HowToManually { get; }
    bool DefaultOn { get; }
    /// <summary>Deterministic pipeline order (see docs/CONTRACTS.md for assignments).</summary>
    int Order { get; }

    /// <summary>
    /// False for actions that degrade the map enough that the tool should never *suggest*
    /// them (they surface as warnings instead of ranked recommendations, and never count
    /// toward the under-limit verdict). The action itself stays available via flags/menu.
    /// </summary>
    bool RecommendByDefault => true;

    ActionApplicability Detect(ActionDetectContext ctx);

    /// <summary>EditorOnly actions throw <see cref="NotSupportedException"/>.</summary>
    ActionResult Apply(ActionApplyContext ctx);

    /// <summary>Action-specific invariants checked between the pre- and post-materialization facts.</summary>
    IEnumerable<ValidationIssue> ValidateResult(MapFacts before, MapFacts after) => [];
}
