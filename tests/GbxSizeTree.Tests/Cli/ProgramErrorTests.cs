using System.Diagnostics;
using System.Text.Json;
using GbxSizeTree.Cli;
using GbxSizeTree.Cli.Modes;

namespace GbxSizeTree.Tests.Cli;

public sealed class ProgramErrorTests
{
    [Fact]
    public void FromException_PreservesCurrentIoAndInternalCategories()
    {
        Assert.Equal(ExitCodes.IoError, ExitCodes.FromException(new FileNotFoundException()));
        Assert.Equal(ExitCodes.IoError, ExitCodes.FromException(new IOException()));
        Assert.Equal(ExitCodes.Internal, ExitCodes.FromException(new InvalidOperationException()));
    }

    [Fact]
    public async Task JsonDiff_MissingQuotedPaths_UsesIoCodeForJsonAndProcess()
    {
        var missingLeft = Path.Combine(Path.GetTempPath(), $"missing-\"left-{Guid.NewGuid():N}.Map.Gbx");
        var missingRight = Path.Combine(Path.GetTempPath(), $"missing-\"right-{Guid.NewGuid():N}.Map.Gbx");
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[]
        {
            typeof(DiffMode).Assembly.Location,
            "diff",
            missingLeft,
            missingRight,
            "--json",
        })
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        Assert.Equal(ExitCodes.IoError, process.ExitCode);
        using var document = JsonDocument.Parse(stdout);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal(process.ExitCode, error.GetProperty("code").GetInt32());
        Assert.Contains(missingLeft, error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("\\u0022", stdout, StringComparison.Ordinal);
        Assert.Equal($"error: Could not find file '{missingLeft}'.{Environment.NewLine}", stderr);
    }
}
