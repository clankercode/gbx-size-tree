using System.Runtime;
using System.Runtime.CompilerServices;
using GbxSizeTree.Container;
using SharpLzo;

[assembly: InternalsVisibleTo("GbxSizeTree.Tests")]

namespace GbxSizeTree.Measure;

/// <summary>Limits removal trials, trial parallelism, and input sizes before decompression or ZIP inspection.</summary>
public sealed record EmbeddedFileContributionOptions
{
    public int MaxTrials { get; init; } = 256;
    public int MaxBodyBytes { get; init; } = 256 * 1024 * 1024;
    public int MaxZipEntries { get; init; } = 4096;
    public long MaxZipRawBytes { get; init; } = 256 * 1024 * 1024;

    /// <summary>
    /// LZO trials run this many at a time for bodies up to <see cref="ParallelTrialsMaxBodyBytes"/>;
    /// 1 restores strictly sequential measurement. Larger bodies stay sequential to bound peak memory.
    /// </summary>
    public int MaxParallelTrials { get; init; } = 2;
    public int ParallelTrialsMaxBodyBytes { get; init; } = 64 * 1024 * 1024;
}

/// <summary>
/// Signed removal savings in one outer LZO stream, not an additive allocation. ZIP sizes are
/// independent metadata; null marginal bytes means unavailable, never zero contribution.
/// </summary>
public sealed record EmbeddedFileContribution(
    string Path,
    long? ZipCompressedBytes,
    long? ZipRawBytes,
    long? MarginalCompressedBodyBytes,
    string? UnavailableReason);

/// <summary>
/// The baseline recompresses the original body, not a parsed/resaved map. Unchanged container
/// overhead cancels in each trial; these values need not sum to any map or ZIP size.
/// </summary>
public sealed record EmbeddedFileContributionMeasurement(
    long? BaselineCompressedBodyBytes,
    IReadOnlyList<EmbeddedFileContribution> Entries,
    string? UnavailableReason);

/// <summary>Reports bounded removal-trial work synchronously without depending on a UI.</summary>
public sealed record EmbeddedFileContributionProgress(
    string Path,
    int CompletedTrials,
    int TotalTrials);

/// <summary>Measures context-dependent embedded-file removal savings without modifying the source map.</summary>
public sealed class EmbeddedFileContributionMeasurer
{
    // Null selects the pooled SharpLzo path; the delegate exists for tests with fake compressors.
    private readonly Func<byte[], long>? compress;

    public EmbeddedFileContributionMeasurer()
    {
    }

    internal EmbeddedFileContributionMeasurer(Func<byte[], long> compress) => this.compress = compress;

    /// <summary>
    /// Measures only the distinct, ordinal paths requested (normally changed entries). Every trial
    /// starts from the same original body. Negative results are valid. Cancellation is checked
    /// between bounded synchronous trials; an in-flight LZO call cannot be interrupted.
    /// </summary>
    public EmbeddedFileContributionMeasurement Measure(
        byte[] originalFile,
        IReadOnlyCollection<string> requestedPaths,
        EmbeddedFileContributionOptions? options = null,
        CancellationToken cancellationToken = default,
        Action<EmbeddedFileContributionProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(originalFile);
        ArgumentNullException.ThrowIfNull(requestedPaths);
        progress = BestEffort(progress);
        options ??= new();
        ArgumentOutOfRangeException.ThrowIfNegative(options.MaxTrials);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxBodyBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxZipEntries);
        ArgumentOutOfRangeException.ThrowIfNegative(options.MaxZipRawBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxParallelTrials);
        ArgumentOutOfRangeException.ThrowIfNegative(options.ParallelTrialsMaxBodyBytes);
        var paths = requestedPaths.Distinct(StringComparer.Ordinal).ToArray();
        if (paths.Length == 0)
        {
            return new(null, [], null);
        }

        cancellationToken.ThrowIfCancellationRequested();
        EmbeddedZipRemovalPlan plan;
        try
        {
            plan = EmbeddedZipRemovalPlan.Create(originalFile, options);
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            return new(null, paths.Select(path => new EmbeddedFileContribution(path, null, null, null, ex.Message)).ToArray(), ex.Message);
        }

        // The plan slices the decompressed file buffer; the container bytes are no longer needed.
        originalFile = null!;
        ReleaseTransientMemory();

        // The pooled path keeps one trial buffer, one output buffer, and one LZO work memory for the
        // whole measurement instead of allocating full body copies per trial (B065 memory target).
        // Buffers are per-Measure arrays (not ArrayPool) so the runtime can decommit them between sides.
        var pooled = compress is null;
        var results = new List<EmbeddedFileContribution>(paths.Length);
        long? baseline = null;
        string? baselineFailure = null;
        var trials = 0;
        var completedTrials = 0;
        var totalTrials = Math.Min(options.MaxTrials, paths.Count(path =>
            plan.Entries.Any(entry => string.Equals(entry.Path, path, StringComparison.Ordinal))));
        if (pooled && totalTrials > 1 && options.MaxParallelTrials > 1
            && plan.BodyLength <= options.ParallelTrialsMaxBodyBytes)
        {
            return MeasureParallel(plan, paths, options, cancellationToken, progress, totalTrials);
        }
        var trialBuffer = pooled ? new byte[plan.BodyLength] : null;
        var outputBuffer = pooled ? new byte[OutputBound(plan.BodyLength)] : null;
        var workMemory = pooled ? new byte[Lzo.WorkMemorySize] : null;
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = plan.Entries.SingleOrDefault(entry => string.Equals(entry.Path, path, StringComparison.Ordinal));
            if (entry is null)
            {
                results.Add(new(path, null, null, null, "Entry path is not present in the original ZIP."));
                continue;
            }

            long? marginal = null;
            string? reason = null;
            if (trials >= options.MaxTrials)
            {
                reason = "Removal trial budget exhausted.";
            }
            else if (baselineFailure is not null)
            {
                reason = baselineFailure;
            }
            else
            {
                progress?.Invoke(new(path, completedTrials, totalTrials));
                try
                {
                    trials++;
                    if (pooled)
                    {
                        // Validate the trial before paying for the shared baseline compression.
                        var trialLength = plan.Remove(path, trialBuffer!);
                        if (baseline is null)
                        {
                            try
                            {
                                baseline = CompressPooled(plan.BodySpan, outputBuffer!, workMemory!);
                            }
                            catch (Exception ex) when (IsUnavailable(ex))
                            {
                                baselineFailure = ex.Message;
                                throw;
                            }
                        }
                        cancellationToken.ThrowIfCancellationRequested();
                        marginal = baseline.Value - CompressPooled(trialBuffer!.AsSpan(0, trialLength), outputBuffer!, workMemory!);
                    }
                    else
                    {
                        // Validate the trial before paying for the shared baseline compression.
                        var trial = plan.Remove(path);
                        if (baseline is null)
                        {
                            try
                            {
                                baseline = Compress(plan.OriginalBody);
                            }
                            catch (Exception ex) when (IsUnavailable(ex))
                            {
                                baselineFailure = ex.Message;
                                throw;
                            }
                        }
                        cancellationToken.ThrowIfCancellationRequested();
                        marginal = baseline.Value - Compress(trial);
                    }
                }
                catch (Exception ex) when (IsUnavailable(ex))
                {
                    reason = ex.Message;
                }
                finally
                {
                    completedTrials++;
                    progress?.Invoke(new(path, completedTrials, totalTrials));
                }
            }
            results.Add(new(path, entry.ZipCompressedBytes, entry.ZipRawBytes, marginal, reason));
        }
        return new(baseline, results, baselineFailure);
    }

    // Same per-path results and budget semantics as the sequential loop; only LZO trials run
    // concurrently. Eligibility is decided in path order so the trial budget picks identical entries.
    private static EmbeddedFileContributionMeasurement MeasureParallel(
        EmbeddedZipRemovalPlan plan,
        string[] paths,
        EmbeddedFileContributionOptions options,
        CancellationToken cancellationToken,
        Action<EmbeddedFileContributionProgress>? progress,
        int totalTrials)
    {
        var results = new EmbeddedFileContribution[paths.Length];
        var eligible = new List<int>(totalTrials);
        for (var i = 0; i < paths.Length; i++)
        {
            var entry = plan.Entries.SingleOrDefault(e => string.Equals(e.Path, paths[i], StringComparison.Ordinal));
            if (entry is null)
            {
                results[i] = new(paths[i], null, null, null, "Entry path is not present in the original ZIP.");
            }
            else if (eligible.Count >= options.MaxTrials)
            {
                results[i] = new(paths[i], entry.ZipCompressedBytes, entry.ZipRawBytes, null, "Removal trial budget exhausted.");
            }
            else
            {
                eligible.Add(i);
            }
        }

        long baselineValue;
        try
        {
            baselineValue = CompressPooled(plan.BodySpan, new byte[OutputBound(plan.BodyLength)], new byte[Lzo.WorkMemorySize]);
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            // Sequential semantics: a baseline failure reports the failure on every present entry,
            // and only the first attempted trial emits progress.
            if (eligible.Count > 0)
            {
                var first = paths[eligible[0]];
                progress?.Invoke(new(first, 0, totalTrials));
                progress?.Invoke(new(first, 1, totalTrials));
            }
            for (var i = 0; i < paths.Length; i++)
            {
                var failed = plan.Entries.SingleOrDefault(e => string.Equals(e.Path, paths[i], StringComparison.Ordinal));
                if (failed is not null)
                {
                    results[i] = new(paths[i], failed.ZipCompressedBytes, failed.ZipRawBytes, null, ex.Message);
                }
            }
            return new(null, results, ex.Message);
        }

        var completedTrials = 0;
        var progressLock = new object();
        Parallel.ForEach(eligible,
            new ParallelOptions { MaxDegreeOfParallelism = options.MaxParallelTrials, CancellationToken = cancellationToken },
            localInit: () => (Trial: new byte[plan.BodyLength], Output: new byte[OutputBound(plan.BodyLength)], Work: new byte[Lzo.WorkMemorySize]),
            body: (i, _, buffers) =>
            {
                var path = paths[i];
                lock (progressLock) progress?.Invoke(new(path, Volatile.Read(ref completedTrials), totalTrials));
                long? marginal = null;
                string? reason = null;
                try
                {
                    var trialLength = plan.Remove(path, buffers.Trial);
                    marginal = baselineValue - CompressPooled(buffers.Trial.AsSpan(0, trialLength), buffers.Output, buffers.Work);
                }
                catch (Exception ex) when (IsUnavailable(ex))
                {
                    reason = ex.Message;
                }
                var entry = plan.Entries.Single(e => string.Equals(e.Path, path, StringComparison.Ordinal));
                results[i] = new(path, entry.ZipCompressedBytes, entry.ZipRawBytes, marginal, reason);
                lock (progressLock) progress?.Invoke(new(path, Interlocked.Increment(ref completedTrials), totalTrials));
                return buffers;
            },
            localFinally: _ => { });
        return new(baselineValue, results, null);
    }

    private static void ReleaseTransientMemory()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }

    private static int OutputBound(int bodyLength) => checked(bodyLength + bodyLength / 16 + 64 + 3);

    private static long CompressPooled(ReadOnlySpan<byte> body, byte[] output, byte[] workMemory)
    {
        // Lzo1x_999 matches GBX.NET.LZO.Lzo.Compress, which delegates to SharpLzo with mode 1.
        var result = Lzo.TryCompress(CompressionMode.Lzo1x_999, body, body.Length, output, out var length, workMemory);
        return result == LzoResult.OK ? length
            : throw new InvalidDataException($"LZO compression failed ({result}).");
    }

    private static Action<EmbeddedFileContributionProgress>? BestEffort(
        Action<EmbeddedFileContributionProgress>? progress)
    {
        if (progress is null) return null;
        var failed = false;
        return value =>
        {
            if (failed) return;
            try { progress(value); }
            catch { failed = true; }
        };
    }

    private long Compress(byte[] body)
    {
        var length = compress!(body);
        return length >= 0 ? length : throw new InvalidDataException("Compressor returned a negative byte count.");
    }

    private static bool IsUnavailable(Exception ex) =>
        ex is not OperationCanceledException and not OutOfMemoryException;
}
