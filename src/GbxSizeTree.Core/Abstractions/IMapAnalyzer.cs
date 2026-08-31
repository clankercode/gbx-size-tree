using GbxSizeTree.Model;

namespace GbxSizeTree.Abstractions;

/// <summary>Input to analysis: a file on disk or in-memory bytes (interactive re-measurement).</summary>
public abstract record MapSource
{
    public abstract string Label { get; }

    public sealed record FromFile(string Path) : MapSource
    {
        public override string Label => System.IO.Path.GetFileName(Path);
    }

    public sealed record FromBytes(byte[] Bytes, string DisplayLabel) : MapSource
    {
        public override string Label => DisplayLabel;
    }
}

public sealed record AnalyzeOptions(
    bool HeaderOnly = false,
    bool Drilldowns = true,
    bool TrialCompressionEstimates = false,
    int TopN = 20);

/// <summary>The deep module every frontend consumes.</summary>
public interface IMapAnalyzer
{
    MapAnalysis Analyze(MapSource source, AnalyzeOptions options);
}
