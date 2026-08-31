using System.Diagnostics;
using GBX.NET;
using GBX.NET.Engines.Game;
using GbxSizeTree.Abstractions;
using GbxSizeTree.Actions;
using GbxSizeTree.Model;

namespace GbxSizeTree.Session;

/// <summary>
/// Owns one optimization session over an in-memory map. Deliberately STATELESS between
/// materializations: the session holds only the original bytes plus the requested action
/// set, and <see cref="Materialize"/> always re-parses the original and applies the set
/// ordered by <see cref="IMapAction.Order"/>. That makes undo trivial (drop a request),
/// replay deterministic, and batch vs interactive byte-identical for the same set.
/// </summary>
public sealed class MapSession
{
    private readonly ActionRegistry registry;
    private readonly IMapAnalyzer analyzer;
    private readonly IStatusSink status;
    private readonly List<(IMapAction Action, AppliedAction Request)> requests = [];
    private MaterializedMap? cache;
    private CGameCtnChallenge? detectMap;

    private MapSession(string sourcePath, byte[] originalBytes, MapAnalysis baseline,
        ActionRegistry registry, IMapAnalyzer analyzer, IStatusSink status)
    {
        SourcePath = sourcePath;
        OriginalBytes = originalBytes;
        Baseline = baseline;
        this.registry = registry;
        this.analyzer = analyzer;
        this.status = status;
    }

    /// <summary>
    /// <paramref name="onBytesRead"/> fires with the raw file bytes BEFORE the (multi-second)
    /// baseline analysis — the hook frontends use to start the background resave trial as
    /// early as possible.
    /// </summary>
    public static MapSession Open(string path, ActionRegistry registry, IMapAnalyzer analyzer, IStatusSink status,
        Action<byte[]>? onBytesRead = null)
    {
        var bytes = File.ReadAllBytes(path);
        onBytesRead?.Invoke(bytes);
        var baseline = analyzer.Analyze(
            new MapSource.FromBytes(bytes, Path.GetFileName(path)), new AnalyzeOptions());
        if (baseline.Facts is null || baseline.Body is null)
        {
            throw new InvalidOperationException("session requires a fully analyzable map (header-only input?)");
        }
        return new MapSession(path, bytes, baseline, registry, analyzer, status);
    }

    public string SourcePath { get; }
    public byte[] OriginalBytes { get; }
    public MapAnalysis Baseline { get; }

    public IReadOnlyList<AppliedAction> Applied => requests.Select(r => r.Request).ToList();

    /// <summary>A parsed map for Detect-time measured estimates. Never mutated by the session.</summary>
    public CGameCtnChallenge DetectMap
    {
        get
        {
            if (detectMap is null)
            {
                using var ms = new MemoryStream(OriginalBytes, writable: false);
                detectMap = Gbx.ParseNode<CGameCtnChallenge>(ms);
            }
            return detectMap;
        }
    }

    /// <summary>Adds (or replaces) an action request. Returns false when the id is unknown.</summary>
    public bool Apply(string actionId, IReadOnlyDictionary<string, string>? settings = null)
    {
        var action = registry.Find(actionId);
        if (action is null || action.Tier == ActionTier.EditorOnly)
        {
            return false;
        }
        requests.RemoveAll(r => string.Equals(
            r.Action.Id, action.Id, StringComparison.OrdinalIgnoreCase));
        requests.Add((action, new AppliedAction(
            action.Id, settings ?? new Dictionary<string, string>())));
        cache = null;
        return true;
    }

    /// <summary>Removes the most recently requested action; returns its id, or null when empty.</summary>
    public string? Undo()
    {
        if (requests.Count == 0)
        {
            return null;
        }
        var last = requests[^1];
        requests.RemoveAt(requests.Count - 1);
        cache = null;
        return last.Action.Id;
    }

    public void Reset()
    {
        requests.Clear();
        cache = null;
    }

    /// <summary>
    /// Fresh parse → apply the requested set by pipeline Order → save → re-analyze → validate.
    /// Cached until the request set changes. The per-action outcomes are surfaced via status.
    /// </summary>
    public MaterializedMap Materialize()
    {
        if (cache is not null)
        {
            return cache;
        }

        var sw = Stopwatch.StartNew();
        if (requests.Count == 0)
        {
            sw.Stop();
            cache = new MaterializedMap(
                OriginalBytes, OriginalBytes.LongLength, Baseline, ValidationReport.Success, sw.Elapsed);
            return cache;
        }

        using var input = new MemoryStream(OriginalBytes, writable: false);
        var gbx = Gbx.Parse<CGameCtnChallenge>(input);
        var map = gbx.Node;

        var ordered = requests.OrderBy(r => r.Action.Order).ToList();
        foreach (var (action, request) in ordered)
        {
            var ctx = new ActionApplyContext(gbx, map, Baseline, request.Settings, status);
            var outcome = action.Apply(ctx);
            status.Info($"{action.Id}: {outcome.Summary}");
        }

        using var output = new MemoryStream();
        gbx.Save(output);
        var bytes = output.ToArray();

        var analysis = analyzer.Analyze(
            new MapSource.FromBytes(bytes, Path.GetFileName(SourcePath)), new AnalyzeOptions());

        var mergedSettings = new Dictionary<string, string>();
        foreach (var (_, request) in ordered)
        {
            foreach (var (k, v) in request.Settings)
            {
                mergedSettings[k] = v;
            }
        }
        var validation = OutputValidator.Validate(
            Baseline.Facts!, bytes, ordered.Select(r => r.Action).ToList(), mergedSettings, out _);

        cache = new MaterializedMap(bytes, bytes.LongLength, analysis, validation, sw.Elapsed);
        return cache;
    }

    /// <summary>Validates, resolves the output path, writes, and returns the written path.</summary>
    public string SaveAs(string? requestedOutput, bool force)
    {
        var materialized = Materialize();
        if (!materialized.Validation.Ok)
        {
            throw new GbxSizeTreeOutputException(
                "refusing to save: output failed validation — " +
                string.Join("; ", materialized.Validation.Issues.Select(i => i.Message)),
                OutputFailureKind.ValidationFailed);
        }
        var path = OutputPathResolver.Resolve(SourcePath, requestedOutput);
        OutputPathResolver.EnsureWritable(SourcePath, path, force);
        File.WriteAllBytes(path, materialized.Bytes);
        return path;
    }
}
