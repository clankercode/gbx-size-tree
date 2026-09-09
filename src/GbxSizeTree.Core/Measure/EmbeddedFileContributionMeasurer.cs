using System.Runtime.CompilerServices;
using GBX.NET.LZO;
using GbxSizeTree.Container;

[assembly: InternalsVisibleTo("GbxSizeTree.Tests")]

namespace GbxSizeTree.Measure;

/// <summary>Limits sequential removal trials and input sizes before decompression or ZIP inspection.</summary>
public sealed record EmbeddedFileContributionOptions
{
    public int MaxTrials { get; init; } = 256;
    public int MaxBodyBytes { get; init; } = 256 * 1024 * 1024;
    public int MaxZipEntries { get; init; } = 4096;
    public long MaxZipRawBytes { get; init; } = 256 * 1024 * 1024;
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
    private readonly Func<byte[], long> compress;

    public EmbeddedFileContributionMeasurer() : this(static body => new Lzo().Compress(body).LongLength)
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

        var results = new List<EmbeddedFileContribution>(paths.Length);
        long? baseline = null;
        string? baselineFailure = null;
        var trials = 0;
        var completedTrials = 0;
        var totalTrials = Math.Min(options.MaxTrials, paths.Count(path =>
            plan.Entries.Any(entry => string.Equals(entry.Path, path, StringComparison.Ordinal))));
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
        var length = compress(body);
        return length >= 0 ? length : throw new InvalidDataException("Compressor returned a negative byte count.");
    }

    private static bool IsUnavailable(Exception ex) =>
        ex is not OperationCanceledException and not OutOfMemoryException;
}
