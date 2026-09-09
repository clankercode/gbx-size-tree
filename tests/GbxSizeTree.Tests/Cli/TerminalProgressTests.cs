using System.Text;
using GbxSizeTree.Cli;
using GbxSizeTree.Cli.Modes;

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
