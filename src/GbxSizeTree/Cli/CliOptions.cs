namespace GbxSizeTree.Cli;

public enum CliOutputFormat
{
    Console,
    Json,
    Html,
    Markdown,
    Png,
    Webp,
}

/// <summary>
/// Represents the command-line choices used to analyse or optimize one TM2020 <c>.Map.Gbx</c> file.
/// </summary>
public sealed record CliOptions
{
    public string? InputPath { get; init; }
    public IReadOnlyList<string> InputPaths { get; init; } = Array.Empty<string>();
    public bool Diff { get; init; }
    public bool CompareAll { get; init; }
    public bool HeaderOnly { get; init; }
    public bool EstimateCompressed { get; init; }
    public bool AllChunks { get; init; }
    public bool UnknownChunks { get; init; }
    public int TopN { get; init; } = 20;
    public bool Json { get; init; }
    public CliOutputFormat Format { get; init; } = CliOutputFormat.Console;
    public bool Styled { get; init; } = true;
    public bool? Color { get; init; }
    public bool Interactive { get; init; }
    public bool NonInteractive { get; init; }
    public bool Optimize { get; init; }
    public string? OutputPath { get; init; }
    public bool Force { get; init; }
    public bool StripLightmap { get; init; }
    public byte? ShadowBrightnessFloor { get; init; }
    public string ThumbnailMode { get; init; } = "keep";
    public bool EmbedStored { get; init; }
    public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> NoActions { get; init; } = Array.Empty<string>();
    public bool DryRun { get; init; }
    public bool Attribute { get; init; }
    public bool Experimental { get; init; }
    public bool Verbose { get; init; }
    public bool Quiet { get; init; }
    public bool ShowHelp { get; init; }
    public bool ShowVersion { get; init; }
    public bool? Pause { get; init; }
}
