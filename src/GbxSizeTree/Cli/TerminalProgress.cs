using System.Diagnostics;
using System.Globalization;
using System.Text;
using GbxSizeTree.Cli.Modes;
using Spectre.Console;

namespace GbxSizeTree.Cli;

internal sealed class TerminalProgress : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(200);
    private readonly object gate = new();
    private readonly TextWriter writer;
    private readonly Func<TimeSpan> clock;
    private readonly Func<int> width;
    private readonly TimeSpan startedAt;
    private readonly IDisposable? timer;
    private readonly TimeSpan throttle;
    private DiffProgress? current;
    private TimeSpan stageStartedAt;
    private TimeSpan lastRenderedAt = TimeSpan.MinValue;
    private int renderedLength;
    private bool disposed;
    private bool outputFailed;

    private TerminalProgress(TextWriter writer, Func<TimeSpan> clock, Func<int> width,
        TimeSpan throttle, Func<Action, IDisposable>? timerFactory)
    {
        this.writer = writer;
        this.clock = clock;
        this.width = width;
        this.throttle = throttle;
        startedAt = clock();
        stageStartedAt = startedAt;
        timer = timerFactory?.Invoke(Refresh);
    }

    internal TerminalProgress(TextWriter writer, Func<TimeSpan> clock, Func<int> width,
        TimeSpan? throttle = null, Func<Action, IDisposable>? timerFactory = null)
        : this(writer, clock, width, throttle ?? RefreshInterval, timerFactory)
    {
    }

    public static TerminalProgress? Create()
    {
        if (!IsSupported(Console.IsErrorRedirected, Environment.GetEnvironmentVariable("TERM")))
            return null;

        var started = Stopwatch.GetTimestamp();
        return new TerminalProgress(Console.Error,
            () => Stopwatch.GetElapsedTime(started),
            GetConsoleWidth,
            RefreshInterval,
            callback => new Timer(_ => callback(), null, RefreshInterval, RefreshInterval));
    }

    internal static bool IsSupported(bool errorRedirected, string? term) =>
        !errorRedirected && !string.Equals(term, "dumb", StringComparison.OrdinalIgnoreCase);

    public void Report(DiffProgress value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (gate)
        {
            if (disposed || outputFailed) return;
            var now = clock();
            var stageChanged = current?.Stage != value.Stage;
            if (stageChanged) stageStartedAt = now;
            current = value;
            var completed = value.Total is > 0 && value.Completed == value.Total;
            if (stageChanged || completed || now - lastRenderedAt >= throttle)
                Render(now, stageChanged);
        }
    }

    internal void Refresh()
    {
        lock (gate)
        {
            if (!disposed && !outputFailed && current is not null)
                Render(clock(), stageChanged: false);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            timer?.Dispose();
            if (outputFailed || renderedLength == 0) return;
            TryWrite($"\r{new string(' ', renderedLength)}\r");
            renderedLength = 0;
        }
    }

    private void Render(TimeSpan now, bool stageChanged)
    {
        var progress = current!;
        var availableWidth = Math.Max(1, width() - 1);
        var stage = "Processing: " + CompactStageName(progress.Stage);
        var statistics = new StringBuilder();
        if (progress.Completed is int completed && progress.Total is int total)
            statistics.Append(" — ").Append(completed).Append('/').Append(total);
        statistics.Append(" — elapsed ").Append(FormatDuration(now - startedAt));
        var eta = EstimateEta(progress, now, stageChanged);
        statistics.Append(" — ETA ").Append(eta is null ? "unknown" : FormatDuration(eta.Value));

        var suffix = statistics.ToString();
        var stageWidth = stage.GetCellWidth();
        var suffixWidth = suffix.GetCellWidth();
        var workItem = string.IsNullOrWhiteSpace(progress.WorkItem) ? null : Sanitize(progress.WorkItem);
        string line;
        if (suffixWidth >= availableWidth)
        {
            line = Clip(suffix, availableWidth);
        }
        else if (stageWidth + suffixWidth >= availableWidth)
        {
            line = Clip(stage, availableWidth - suffixWidth) + suffix;
        }
        else if (workItem is null)
        {
            line = stage + suffix;
        }
        else
        {
            var workItemWidth = availableWidth - stageWidth - suffixWidth - " — ".GetCellWidth();
            line = stage + " — " + Clip(workItem, workItemWidth) + suffix;
        }

        var lineWidth = line.GetCellWidth();
        var padding = Math.Max(0, renderedLength - lineWidth);
        if (!TryWrite($"\r{line}{new string(' ', padding)}")) return;
        renderedLength = lineWidth;
        lastRenderedAt = now;
    }

    private TimeSpan? EstimateEta(DiffProgress progress, TimeSpan now, bool stageChanged)
    {
        if (stageChanged || progress.Completed is not > 0 ||
            progress.Total is not int total || progress.Completed > total)
            return null;
        var remaining = total - progress.Completed.Value;
        if (remaining <= 0) return TimeSpan.Zero;
        var perItemTicks = (now - stageStartedAt).Ticks / progress.Completed.Value;
        if (perItemTicks <= 0 || perItemTicks > TimeSpan.MaxValue.Ticks / remaining)
            return null;
        return TimeSpan.FromTicks(perItemTicks * remaining);
    }

    private bool TryWrite(string value)
    {
        try
        {
            writer.Write(value);
            writer.Flush();
            return true;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or NotSupportedException)
        {
            outputFailed = true;
            return false;
        }
    }

    private static string CompactStageName(DiffProgressStage stage) => stage switch
    {
        DiffProgressStage.ReadingOld => "read OLD",
        DiffProgressStage.ReadingNew => "read NEW",
        DiffProgressStage.ParsingOld => "parse OLD",
        DiffProgressStage.ParsingNew => "parse NEW",
        DiffProgressStage.Comparing => "compare",
        DiffProgressStage.EmbeddedDeepComparison => "deep embeds",
        DiffProgressStage.MeasuringOldEmbeds => "measure OLD embeds",
        DiffProgressStage.MeasuringNewEmbeds => "measure NEW embeds",
        _ => throw new ArgumentOutOfRangeException(nameof(stage)),
    };

    private static string Sanitize(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
            result.Append(Rune.IsControl(rune) ? ' ' : rune.ToString());
        return result.ToString();
    }

    private static string Clip(string value, int width)
    {
        if (width <= 0) return "";
        if (value.GetCellWidth() <= width) return value;
        if (width == 1) return "…";
        var result = new StringBuilder(value.Length);
        var remaining = width - "…".GetCellWidth();
        foreach (var rune in value.EnumerateRunes())
        {
            var runeWidth = Math.Max(0, rune.ToString().GetCellWidth());
            if (runeWidth > remaining) break;
            result.Append(rune.ToString());
            remaining -= runeWidth;
        }
        return result.Append('…').ToString();
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
        if (duration.TotalSeconds < 10)
            return duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s";
        if (duration.TotalMinutes < 1)
            return Math.Round(duration.TotalSeconds).ToString(CultureInfo.InvariantCulture) + "s";
        if (duration.TotalHours < 1)
            return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
        return $"{(int)duration.TotalHours}h {duration.Minutes}m";
    }

    private static int GetConsoleWidth()
    {
        try
        {
            return Console.WindowWidth is > 1 and <= 1000 ? Console.WindowWidth : 80;
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException)
        {
            return 80;
        }
    }
}
