namespace GbxSizeTree.Cli.Platform;

/// <summary>Observable launch facts used to classify console ownership without platform calls in tests.</summary>
public sealed record LaunchProbe(
    bool IsWindows,
    int? ConsoleProcessCount,
    bool StdinRedirected,
    bool StdoutRedirected,
    bool HasGuiDisplay,
    string? ForceGuiEnv,
    string? TerminalType);

/// <summary>Whether the process was launched from a terminal or owns a GUI-created console.</summary>
public enum LaunchKind
{
    Terminal,
    GuiOwnConsole,
}

/// <summary>Classifies the current launch mode from explicit, testable platform facts.</summary>
public static class LaunchModeDetector
{
    /// <summary>Whether prompts can safely read from and render directly to this terminal.</summary>
    public static bool HasInteractiveTerminal(LaunchProbe probe) =>
        !probe.StdinRedirected
        && !probe.StdoutRedirected
        && !string.Equals(probe.TerminalType, "dumb", StringComparison.OrdinalIgnoreCase);

    /// <summary>Chooses the TUI only when it was requested (explicitly or by default) and is safe.</summary>
    public static bool ShouldStartInteractive(
        LaunchProbe probe,
        bool explicitlyRequested,
        bool disabled,
        bool json,
        bool reportOnly,
        bool wantsOptimization) =>
        HasInteractiveTerminal(probe)
        && !disabled
        && !json
        && (explicitlyRequested || (!reportOnly && !wantsOptimization));

    public static LaunchKind Detect(LaunchProbe probe)
    {
        if (probe.IsWindows)
        {
            return probe.ConsoleProcessCount == 1
                ? LaunchKind.GuiOwnConsole
                : LaunchKind.Terminal;
        }

        if (IsTruthy(probe.ForceGuiEnv))
        {
            return LaunchKind.GuiOwnConsole;
        }

        return probe.HasGuiDisplay && probe.StdinRedirected && probe.StdoutRedirected
            ? LaunchKind.GuiOwnConsole
            : LaunchKind.Terminal;
    }

    public static LaunchProbe ProbeCurrent()
    {
        var display = Environment.GetEnvironmentVariable("DISPLAY");
        var waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");

        return new LaunchProbe(
            OperatingSystem.IsWindows(),
            ConsoleOwnership.GetConsoleProcessCount(),
            Console.IsInputRedirected,
            Console.IsOutputRedirected,
            !string.IsNullOrWhiteSpace(display) || !string.IsNullOrWhiteSpace(waylandDisplay),
            Environment.GetEnvironmentVariable("GBX_SIZE_TREE_GUI"),
            Environment.GetEnvironmentVariable("TERM"));
    }

    internal static bool RawImageOutputRequested(IReadOnlyList<string> arguments)
    {
        for (var i = 0; i < arguments.Count; i++)
        {
            switch (arguments[i])
            {
                case "--":
                    return false;
                case "--top":
                case "-o":
                case "--output":
                case "--lighten-shadows":
                case "--thumbnail":
                case "--action":
                case "--no-action":
                    i++;
                    break;
                case "--png":
                case "--webp":
                    return true;
            }
        }

        return false;
    }

    private static bool IsTruthy(string? value) => value?.Trim().ToUpperInvariant() is
        "1" or "TRUE" or "YES" or "ON";
}
