using System.Diagnostics;
using GbxSizeTree.Abstractions;

namespace GbxSizeTree.Cli.Output;

/// <summary>
/// Reports single-threaded CLI progress while map data documented in docs/FORMAT-NOTES.md is read;
/// JSON mode directs every status line to stderr.
/// </summary>
public sealed class ConsoleStatusSink(bool toStderr) : IStatusSink
{
    public void Info(string message) => WriteLine("info", message);

    public void Warn(string message) => WriteLine("warn", message);

    public IDisposable Activity(string label)
    {
        Info($"{label}...");
        return new ActivityScope(this, Stopwatch.StartNew());
    }

    private void WriteLine(string level, string message)
    {
        var output = toStderr ? Console.Error : Console.Out;
        output.WriteLine($"[{level}] {message}");
    }

    private sealed class ActivityScope(ConsoleStatusSink sink, Stopwatch stopwatch) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            stopwatch.Stop();
            sink.Info($"done ({stopwatch.ElapsedMilliseconds} ms)");
        }
    }
}
