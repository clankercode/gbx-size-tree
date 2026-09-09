using System.Diagnostics;
using GBX.NET;
using GBX.NET.Engines.Game;
using GbxSizeTree.Cli;
using GbxSizeTree.Cli.Modes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace GbxSizeTree.Tests.Cli;

public sealed class DiffImageModeTests
{
    [Theory]
    [InlineData("--png")]
    [InlineData("--webp")]
    public async Task Run_RedirectedStdoutProducesOnlyDecodableImage(string format)
    {
        using var maps = new MapPair();

        var result = await RunAsync([
            "diff", maps.Left, maps.Right, format, "--no-pause",
        ]);

        Assert.Equal(ExitCodes.Ok, result.ExitCode);
        Assert.Empty(result.Stderr);
        using var image = Image.Load<Rgba32>(result.Stdout);
        Assert.Equal(1400, image.Width);
        Assert.InRange(image.Height, 900, 2200);
        Assert.Equal(format == "--png" ? "PNG" : "WEBP", Signature(result.Stdout));
    }

    [Theory]
    [InlineData("--png", "report.png")]
    [InlineData("--webp", "report.webp")]
    public async Task Run_ExplicitOutputWritesImageAndLeavesStdoutEmpty(string format, string fileName)
    {
        using var maps = new MapPair();
        using var directory = new TemporaryDirectory();
        var output = Path.Combine(directory.Path, fileName);

        var result = await RunAsync([
            "diff", maps.Left, maps.Right, format, "-o", output,
        ]);

        Assert.Equal(ExitCodes.Ok, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Empty(result.Stderr);
        Assert.Equal(format == "--png" ? "PNG" : "WEBP", Signature(File.ReadAllBytes(output)));
        using var image = Image.Load<Rgba32>(output);
        Assert.Equal(1400, image.Width);
    }

    [Fact]
    public async Task Run_ExistingOutputFailsOnStderrWithoutChangingFile()
    {
        using var maps = new MapPair();
        using var directory = new TemporaryDirectory();
        var output = Path.Combine(directory.Path, "report.png");
        var original = "original"u8.ToArray();
        File.WriteAllBytes(output, original);

        var result = await RunAsync([
            "diff", maps.Left, maps.Right, "--png", "-o", output,
        ]);

        Assert.Equal(ExitCodes.IoError, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains("already exists", System.Text.Encoding.UTF8.GetString(result.Stderr));
        Assert.Equal(original, File.ReadAllBytes(output));
    }

    [Fact]
    public async Task Run_TerminalStdoutRefusesBeforeReadingInputs()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() && File.Exists("/usr/bin/script"),
            "PTY smoke requires util-linux script on Linux.");
        using var directory = new TemporaryDirectory();
        var transcript = Path.Combine(directory.Path, "pty.txt");
        var missingLeft = Path.Combine(directory.Path, "missing-left.Map.Gbx");
        var missingRight = Path.Combine(directory.Path, "missing-right.Map.Gbx");
        var command = string.Join(' ', new[]
        {
            Quote("dotnet"),
            Quote(typeof(DiffMode).Assembly.Location),
            "diff",
            Quote(missingLeft),
            Quote(missingRight),
            "--png",
        });
        var start = new ProcessStartInfo("/usr/bin/script")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[] { "-q", "-e", "-c", command, transcript })
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start)!;
        var stdoutTask = ReadBytesAsync(process.StandardOutput.BaseStream);
        var stderrTask = ReadBytesAsync(process.StandardError.BaseStream);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        var output = System.Text.Encoding.UTF8.GetString((await stdoutTask).Concat(await stderrTask).ToArray());

        Assert.Equal(ExitCodes.Usage, process.ExitCode);
        Assert.Contains("requires -o/--output", output);
        Assert.DoesNotContain("Could not find file", output);
    }

    private static async Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(typeof(DiffMode).Assembly.Location);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["GBX_SIZE_TREE_GUI"] = "1";
        start.Environment["DISPLAY"] = ":synthetic";

        using var process = Process.Start(start)!;
        var stdoutTask = ReadBytesAsync(process.StandardOutput.BaseStream);
        var stderrTask = ReadBytesAsync(process.StandardError.BaseStream);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        return new(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static async Task<byte[]> ReadBytesAsync(Stream stream)
    {
        using var output = new MemoryStream();
        await stream.CopyToAsync(output, TestContext.Current.CancellationToken);
        return output.ToArray();
    }

    private static string Signature(byte[] bytes) =>
        bytes.AsSpan(1, 3).SequenceEqual("PNG"u8)
            ? "PNG"
            : bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8)
                && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8)
                ? "WEBP"
                : "unknown";

    private static string Quote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private sealed record ProcessResult(int ExitCode, byte[] Stdout, byte[] Stderr);

    private sealed class MapPair : IDisposable
    {
        private readonly TemporaryDirectory directory = new();
        public string Left => Path.Combine(directory.Path, "left.Map.Gbx");
        public string Right => Path.Combine(directory.Path, "right.Map.Gbx");

        public MapPair()
        {
            Gbx.LZO = new GBX.NET.LZO.Lzo();
            Save(new CGameCtnChallenge { MapName = "Before" }, Left);
            Save(new CGameCtnChallenge { MapName = "After" }, Right);
        }

        public void Dispose() => directory.Dispose();

        private static void Save(CGameCtnChallenge map, string path)
        {
            map.Chunks.Create<CGameCtnChallenge.Chunk03043054>().Version = 1;
            new Gbx<CGameCtnChallenge>(map).Save(path);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"gbx-size-tree-image-mode-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
