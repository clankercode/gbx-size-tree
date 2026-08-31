using GbxSizeTree.Cli.Platform;

namespace GbxSizeTree.Tests.Platform;

public class LaunchModeDetectorTests
{
    public static TheoryData<LaunchProbe, LaunchKind> LaunchModes => new()
    {
        { Probe(isWindows: true, consoleProcessCount: 1), LaunchKind.GuiOwnConsole },
        { Probe(isWindows: true, consoleProcessCount: 3), LaunchKind.Terminal },
        { Probe(isWindows: true, consoleProcessCount: null), LaunchKind.Terminal },
        { Probe(forceGuiEnv: "1"), LaunchKind.GuiOwnConsole },
        { Probe(stdinRedirected: true, stdoutRedirected: true), LaunchKind.Terminal },
        {
            Probe(stdinRedirected: true, stdoutRedirected: true, hasGuiDisplay: true),
            LaunchKind.GuiOwnConsole
        },
        {
            Probe(stdinRedirected: false, stdoutRedirected: true, hasGuiDisplay: true),
            LaunchKind.Terminal
        },
    };

    [Theory]
    [MemberData(nameof(LaunchModes))]
    public void Detect_ClassifiesLaunchProbe(LaunchProbe probe, LaunchKind expected) =>
        Assert.Equal(expected, LaunchModeDetector.Detect(probe));

    [Fact]
    public void ShouldStartInteractive_NormalTerminalDefaultsToTui()
    {
        Assert.True(LaunchModeDetector.ShouldStartInteractive(
            Probe(), explicitlyRequested: false, disabled: false, json: false,
            reportOnly: false, wantsOptimization: false));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ShouldStartInteractive_RedirectedTerminalNeverStartsTui(
        bool stdinRedirected,
        bool stdoutRedirected)
    {
        var probe = Probe(stdinRedirected: stdinRedirected, stdoutRedirected: stdoutRedirected);

        Assert.False(LaunchModeDetector.ShouldStartInteractive(
            probe, explicitlyRequested: true, disabled: false, json: false,
            reportOnly: false, wantsOptimization: false));
    }

    [Fact]
    public void ShouldStartInteractive_DumbTerminalNeverStartsTui()
    {
        Assert.False(LaunchModeDetector.ShouldStartInteractive(
            Probe(terminalType: "dumb"),
            explicitlyRequested: true,
            disabled: false,
            json: false,
            reportOnly: false,
            wantsOptimization: false));
    }

    [Theory]
    [InlineData(false, true, false, false, false)]
    [InlineData(false, false, true, false, false)]
    [InlineData(false, false, false, true, false)]
    [InlineData(false, false, false, false, true)]
    [InlineData(true, false, false, true, true)]
    public void ShouldStartInteractive_RespectsModeFlags(
        bool explicitlyRequested,
        bool disabled,
        bool json,
        bool reportOnly,
        bool wantsOptimization)
    {
        Assert.Equal(
            explicitlyRequested && !disabled && !json,
            LaunchModeDetector.ShouldStartInteractive(
                Probe(), explicitlyRequested, disabled, json, reportOnly, wantsOptimization));
    }

    [Fact]
    public void ConsoleOwnership_OnNonWindows_ReturnsNull()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Null(ConsoleOwnership.GetConsoleProcessCount());
    }

    [Fact]
    public void ZenityPicker_WhenExecutableDoesNotExist_ReturnsNull()
    {
        var picker = new ZenityFilePicker($"gbx-size-tree-missing-{Guid.NewGuid():N}");

        Assert.Null(picker.PickMapFile());
    }

    private static LaunchProbe Probe(
        bool isWindows = false,
        int? consoleProcessCount = null,
        bool stdinRedirected = false,
        bool stdoutRedirected = false,
        bool hasGuiDisplay = false,
        string? forceGuiEnv = null,
        string? terminalType = null) =>
        new(
            isWindows,
            consoleProcessCount,
            stdinRedirected,
            stdoutRedirected,
            hasGuiDisplay,
            forceGuiEnv,
            terminalType);
}
