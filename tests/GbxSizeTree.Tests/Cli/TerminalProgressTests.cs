using System.Text;
using GbxSizeTree.Cli;
using GbxSizeTree.Cli.Modes;
using Spectre.Console;

namespace GbxSizeTree.Tests.Cli;

public sealed class TerminalProgressTests
{
    [Theory]
    [InlineData(true, null, false)]
    [InlineData(false, "dumb", false)]
    [InlineData(false, "DUMB", false)]
    [InlineData(false, null, true)]
    [InlineData(false, "xterm-256color", true)]
    public void IsSupported_RequiresInteractiveNonDumbStderr(bool redirected, string? term, bool expected) =>
        Assert.Equal(expected, TerminalProgress.IsSupported(redirected, term));

    [Fact]
    public void Report_UsesHonestEtaAfterSamplesAndClipsSanitizedOutput()
    {
        var now = TimeSpan.Zero;
        var writer = new StringWriter();
        using var progress = new TerminalProgress(writer, () => now, () => 160, TimeSpan.Zero);

        progress.Report(new(DiffProgressStage.ParsingOld, "bad\r\nname"));
        now = TimeSpan.FromSeconds(2);
        progress.Report(new(DiffProgressStage.MeasuringOldEmbeds, "asset\u001b[31m.bin", 0, 4));
        now = TimeSpan.FromSeconds(4);
        progress.Report(new(DiffProgressStage.MeasuringOldEmbeds, "asset\u001b[31m.bin", 1, 4));

        var output = writer.ToString();
        Assert.Contains("ETA unknown", output);
        Assert.Contains("ETA 6.0s", output);
        Assert.DoesNotContain('\n', output);
        Assert.DoesNotContain('\u001b', output);
        Assert.All(output.Split('\r', StringSplitOptions.RemoveEmptyEntries), line => Assert.True(line.Length <= 159));
    }

    [Fact]
    public void EightyColumnsReserveCountsElapsedAndEtaAfterLongUnicodeWorkItem()
    {
        var now = TimeSpan.Zero;
        var writer = new StringWriter();
        using var progress = new TerminalProgress(writer, () => now, () => 80, TimeSpan.Zero);

        progress.Report(new(DiffProgressStage.MeasuringOldEmbeds, "路徑/😀/" + new string('長', 40) + ".Item.Gbx", 0, 4));
        now = TimeSpan.FromSeconds(4);
        progress.Report(new(DiffProgressStage.MeasuringOldEmbeds, "路徑/😀/" + new string('長', 40) + ".Item.Gbx", 1, 4));

        var lines = Lines(writer);
        var line = lines[^1];
        Assert.Contains("Processing: measure OLD embeds", line);
        Assert.Contains("1/4", line);
        Assert.Contains("elapsed 4.0s", line);
        Assert.Contains("ETA 12s", line);
        Assert.Contains('…', line);
        Assert.All(lines, value => Assert.InRange(value.GetCellWidth(), 1, 79));
        Assert.All(lines, value => Assert.True(IsWellFormedUtf16(value)));
    }

    [Fact]
    public void UnicodeDisplayWidthClipsAndClearsWithoutWrappingOrSplittingSurrogates()
    {
        var now = TimeSpan.Zero;
        var writer = new StringWriter();
        var progress = new TerminalProgress(writer, () => now, () => 80, TimeSpan.Zero);

        progress.Report(new(DiffProgressStage.MeasuringOldEmbeds, "C:/maps/" + string.Concat(Enumerable.Repeat("漢😀", 40)), 1, 2));
        now = TimeSpan.FromSeconds(10);
        progress.Report(new(DiffProgressStage.ReadingNew, "短😀.Map.Gbx"));
        progress.Dispose();

        var lines = Lines(writer);
        Assert.All(lines, value => Assert.InRange(value.GetCellWidth(), 1, 79));
        Assert.All(lines, value => Assert.True(IsWellFormedUtf16(value)));
        Assert.Equal(3, lines.Length);
        Assert.NotEqual("", lines[0].Trim());
        Assert.NotEqual("", lines[1].Trim());
        Assert.Equal(lines[0].GetCellWidth(), lines[1].GetCellWidth());
        Assert.Equal(lines[1].TrimEnd().GetCellWidth(), lines[2].GetCellWidth());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(10)]
    [InlineData(20)]
    public void NarrowWidthsStayWithinOneTerminalLine(int width)
    {
        var writer = new StringWriter();
        using var progress = new TerminalProgress(writer, () => TimeSpan.Zero, () => width, TimeSpan.Zero);

        progress.Report(new(DiffProgressStage.MeasuringNewEmbeds, "超長😀path.Item.Gbx", 1234, 5678));

        Assert.All(Lines(writer), line => Assert.InRange(line.GetCellWidth(), 1, width - 1));
        Assert.DoesNotContain('\n', writer.ToString());
    }

    [Fact]
    public void TinyWidthClipsLine()
    {
        var writer = new StringWriter();
        using var progress = new TerminalProgress(writer, () => TimeSpan.Zero, () => 20, TimeSpan.Zero);

        progress.Report(new(DiffProgressStage.ParsingOld, "a very long map name.gbx"));

        Assert.All(writer.ToString().Split('\r', StringSplitOptions.RemoveEmptyEntries),
            line => Assert.True(line.Length <= 19));
        Assert.Contains('…', writer.ToString());
    }

    [Fact]
    public void TimerRefreshesElapsedAndDisposeClearsExactlyOnce()
    {
        var now = TimeSpan.Zero;
        var writer = new StringWriter();
        Action? tick = null;
        var timer = new FakeDisposable();
        var progress = new TerminalProgress(writer, () => now, () => 120,
            TimeSpan.FromHours(1), callback => { tick = callback; return timer; });
        progress.Report(new(DiffProgressStage.ParsingOld, "map.gbx"));
        var afterReport = writer.GetStringBuilder().Length;

        now = TimeSpan.FromSeconds(3);
        tick!();
        Assert.True(writer.GetStringBuilder().Length > afterReport);
        Assert.Contains("elapsed 3.0s", writer.ToString());

        progress.Dispose();
        var disposedOutput = writer.ToString();
        Assert.True(timer.Disposed);
        Assert.Matches("\\r +\\r$", disposedOutput);
        progress.Dispose();
        tick();
        progress.Report(new(DiffProgressStage.ReadingNew));
        Assert.Equal(disposedOutput, writer.ToString());
    }

    [Fact]
    public void ThrottlesSameStageButRendersTransitionsAndCompletion()
    {
        var now = TimeSpan.Zero;
        var writer = new CountingWriter();
        using var progress = new TerminalProgress(writer, () => now, () => 120, TimeSpan.FromSeconds(5));

        progress.Report(new(DiffProgressStage.ParsingOld));
        now = TimeSpan.FromSeconds(1);
        progress.Report(new(DiffProgressStage.ParsingOld));
        progress.Report(new(DiffProgressStage.MeasuringOldEmbeds, "a", 0, 1));
        now = TimeSpan.FromSeconds(2);
        progress.Report(new(DiffProgressStage.MeasuringOldEmbeds, "a", 1, 1));

        Assert.Equal(3, writer.WriteCount);
    }

    [Fact]
    public void WriterFailureDisablesFurtherOutputAndCleanupErrors()
    {
        var writer = new ThrowingWriter();
        var progress = new TerminalProgress(writer, () => TimeSpan.Zero, () => 80);

        progress.Report(new(DiffProgressStage.ReadingOld));
        progress.Report(new(DiffProgressStage.ReadingNew));
        progress.Dispose();

        Assert.Equal(1, writer.Attempts);
    }

    private static string[] Lines(StringWriter writer) =>
        writer.ToString().Split('\r', StringSplitOptions.RemoveEmptyEntries);

    private static bool IsWellFormedUtf16(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]))
            {
                if (++i >= value.Length || !char.IsLowSurrogate(value[i])) return false;
            }
            else if (char.IsLowSurrogate(value[i]))
            {
                return false;
            }
        }
        return true;
    }

    private sealed class FakeDisposable : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class CountingWriter : StringWriter
    {
        public int WriteCount { get; private set; }
        public override void Write(string? value)
        {
            WriteCount++;
            base.Write(value);
        }
    }

    private sealed class ThrowingWriter : TextWriter
    {
        public int Attempts { get; private set; }
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(string? value)
        {
            Attempts++;
            throw new IOException("closed terminal");
        }
    }
}
