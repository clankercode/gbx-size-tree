namespace GbxSizeTree.Cli.Platform;

/// <summary>Observable launch facts used to classify console ownership without platform calls in tests.</summary>
public sealed record LaunchProbe(
    bool IsWindows,
    int? ConsoleProcessCount,
    bool StdinRedirected,
    bool StdoutRedirected,
    bool HasGuiDisplay,
    string? ForceGuiEnv);

/// <summary>Whether the process was launched from a terminal or owns a GUI-created console.</summary>
public enum LaunchKind
{
    Terminal,
    GuiOwnConsole,
}

/// <summary>Classifies the current launch mode from explicit, testable platform facts.</summary>
public static class LaunchModeDetector
{
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
            Environment.GetEnvironmentVariable("GBX_SIZE_TREE_GUI"));
    }

    private static bool IsTruthy(string? value) => value?.Trim().ToUpperInvariant() is
        "1" or "TRUE" or "YES" or "ON";
}
