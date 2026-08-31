namespace GbxSizeTree.Model;

public sealed record ValidationIssue(string Rule, string Message);

public sealed record ValidationReport(bool Ok, IReadOnlyList<ValidationIssue> Issues)
{
    public static readonly ValidationReport Success = new(true, []);
}

/// <summary>One action applied in a session, replayable in order.</summary>
public sealed record AppliedAction(string ActionId, IReadOnlyDictionary<string, string> Settings);

/// <summary>The serialized + re-analyzed result of the session's current action list.</summary>
public sealed record MaterializedMap(
    byte[] Bytes,
    long FileBytes,
    MapAnalysis Analysis,
    ValidationReport Validation,
    TimeSpan Elapsed);

/// <summary>One step of a marginal-savings attribution replay (cumulative, pipeline order).</summary>
public sealed record AttributionRow(string Label, long FileBytes, long SavedBytes);

/// <summary>Batch-mode outcome for the --json envelope: what was applied and what it bought.</summary>
public sealed record OptimizationSummary(
    IReadOnlyList<string> AppliedActionIds,
    long BeforeBytes,
    long AfterBytes,
    long SavedBytes,
    string? OutputPath,
    double ElapsedSeconds,
    IReadOnlyList<AttributionRow>? Attribution);

public sealed record Recommendation(
    string? ActionId,
    string Title,
    Actions.ActionTier Tier,
    long EstimatedSavingsBytes,
    Actions.EstimateKind Kind,
    string Consequence,
    string HowTo);
