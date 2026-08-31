namespace GbxSizeTree.Abstractions;

/// <summary>
/// Progress/log seam so Core never references a console library. The CLI shell provides
/// a Spectre-backed sink (stderr-only in --json mode); tests use <see cref="NullStatusSink"/>.
/// </summary>
public interface IStatusSink
{
    void Info(string message);
    void Warn(string message);
    /// <summary>A long-running step; disposal ends it. May render a spinner.</summary>
    IDisposable Activity(string label);
}

public sealed class NullStatusSink : IStatusSink
{
    public static readonly NullStatusSink Instance = new();
    public void Info(string message) { }
    public void Warn(string message) { }
    public IDisposable Activity(string label) => NullScope.Instance;

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
