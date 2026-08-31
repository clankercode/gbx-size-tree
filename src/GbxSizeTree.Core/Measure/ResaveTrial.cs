using GBX.NET;
using GBX.NET.Engines.Game;

namespace GbxSizeTree.Measure;

/// <summary>
/// Background trial resave: parses the original bytes on a worker thread and saves them with
/// the LZO1x_999 path, so a *measured* resave estimate is ready by the time recommendations
/// render. Start it as early as possible (right after the file is read, before analysis) —
/// it then runs concurrently with the analyzer and is usually finished before anyone asks.
/// The trial uses its own isolated Gbx instance and never shares nodes with the caller.
/// </summary>
public sealed class ResaveTrial
{
    private readonly Task<long?> resavedLength;
    private readonly long originalLength;

    private ResaveTrial(Task<long?> resavedLength, long originalLength)
    {
        this.resavedLength = resavedLength;
        this.originalLength = originalLength;
    }

    public static ResaveTrial Start(byte[] originalBytes)
    {
        var task = Task.Run<long?>(() =>
        {
            try
            {
                using var input = new MemoryStream(originalBytes, writable: false);
                var gbx = Gbx.Parse<CGameCtnChallenge>(input);
                gbx.BodyCompression = GbxCompression.Compressed;
                using var output = new MemoryStream();
                gbx.Save(output);
                return output.Length;
            }
            catch
            {
                // A trial estimate must never take the report down with it.
                return null;
            }
        });
        return new ResaveTrial(task, originalBytes.LongLength);
    }

    /// <summary>
    /// Measured whole-file savings of a pure LZO1x_999 resave (can be negative), or null when
    /// the trial failed or is still running after <paramref name="wait"/>.
    /// </summary>
    public long? TryGetMeasuredSavings(TimeSpan wait)
    {
        try
        {
            if (!resavedLength.Wait(wait))
            {
                return null;
            }
        }
        catch
        {
            return null;
        }

        return resavedLength.Result is long resaved ? originalLength - resaved : null;
    }
}
