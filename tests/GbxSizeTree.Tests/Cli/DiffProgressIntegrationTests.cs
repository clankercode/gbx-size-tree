using System.Diagnostics;
using System.Text;
using GbxSizeTree.Cli.Modes;
using GbxSizeTree.Tests.Fixtures;

namespace GbxSizeTree.Tests.Cli;

public sealed class DiffProgressIntegrationTests
{
    [Fact]
    public void CompareFiles_ReportsRealStagesInOrder()
    {
        SampleMap.SkipUnlessAvailable();
        var seen = new List<DiffProgress>();

        DiffMode.CompareFiles(SampleMap.Path, SampleMap.Path, progress: seen.Add);

        var stages = seen.Select(value => value.Stage).ToArray();
        Assert.Equal(
        [
            DiffProgressStage.ReadingOld,
            DiffProgressStage.ReadingNew,
            DiffProgressStage.ParsingOld,
            DiffProgressStage.ParsingNew,
            DiffProgressStage.Comparing,
            DiffProgressStage.EmbeddedDeepComparison,
        ], stages);
        Assert.All(seen, value =>
        {
            Assert.Null(value.Completed);
            Assert.Null(value.Total);
        });
    }

    [Fact]
    public void CompareFiles_FailureStillReportsCurrentStage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.Map.Gbx");
        var seen = new List<DiffProgress>();

        Assert.Throws<FileNotFoundException>(() => DiffMode.CompareFiles(path, path, progress: seen.Add));

        Assert.Equal(DiffProgressStage.ReadingOld, Assert.Single(seen).Stage);
    }

    [Fact]
    public async Task RealMaps_PseudoTerminalShowsEarlyStageUpdatesAndClearsBeforeFinalOutput()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Skip("pseudo-terminal verification requires Linux");
        var root = Environment.GetEnvironmentVariable("GBX_SIZE_TREE_SB2");
        var oldMap = root is null ? null : FindMap(root, "v205");
        var newMap = root is null ? null : FindMap(root, "v206");
        Assert.SkipUnless(oldMap is not null && newMap is not null,
            "set GBX_SIZE_TREE_SB2 with Sweet 2 Burger v205/v206 maps");

        var start = new ProcessStartInfo("script")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-qefc");
        start.ArgumentList.Add(ShellCommand(typeof(DiffMode).Assembly.Location, oldMap!, newMap!));
        start.ArgumentList.Add("/dev/null");
        start.Environment["TERM"] = "xterm";

        using var process = Process.Start(start)!;
        var read = new char[256];
        var transcript = new StringBuilder();
        using var early = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!process.HasExited && !transcript.ToString().Contains("Processing:", StringComparison.Ordinal))
        {
            var count = await process.StandardOutput.ReadAsync(read.AsMemory(), early.Token);
            if (count == 0) break;
            transcript.Append(read, 0, count);
        }
        Assert.Contains("Processing:", transcript.ToString());

        using var completion = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        completion.CancelAfter(TimeSpan.FromMinutes(2));
        transcript.Append(await process.StandardOutput.ReadToEndAsync(completion.Token));
        var stderr = await process.StandardError.ReadToEndAsync(completion.Token);
        await process.WaitForExitAsync(completion.Token);
        Assert.True(process.ExitCode == 0, stderr + transcript);

        var output = transcript.ToString();
        Assert.Contains("read OLD", output);
        Assert.Contains("parse OLD", output);
        Assert.Contains("parse NEW", output);
        Assert.Contains("compare", output);
        Assert.Contains("deep embeds", output);
        Assert.Contains("measure", output);
        Assert.Contains("ETA ", output);
        var heading = output.IndexOf("Map diff", StringComparison.Ordinal);
        Assert.True(heading > 0, output);
        var clearStart = output.LastIndexOf('\r', heading - 2);
        Assert.True(clearStart >= 0 && output[clearStart..heading].Trim('\r', ' ').Length == 0, output);
        Assert.DoesNotContain("Processing:", output[heading..]);
    }

    private static string? FindMap(string root, string version) => Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
        .FirstOrDefault(path => Path.GetFileName(path).Contains(version, StringComparison.OrdinalIgnoreCase));

    private static string ShellCommand(string assembly, string oldMap, string newMap) =>
        $"dotnet {Quote(assembly)} diff {Quote(oldMap)} {Quote(newMap)} --no-color";

    private static string Quote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
}
