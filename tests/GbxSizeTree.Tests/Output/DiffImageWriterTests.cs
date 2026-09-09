using GbxSizeTree.Cli;
using GbxSizeTree.Cli.Output;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace GbxSizeTree.Tests.Output;

public sealed class DiffImageWriterTests
{
    [Theory]
    [InlineData(CliOutputFormat.Png)]
    [InlineData(CliOutputFormat.Webp)]
    public void Write_StdoutRoundTripsExactPixels(CliOutputFormat format)
    {
        using var source = TestImage();
        using var stdout = new MemoryStream();

        DiffImageWriter.Write(source, format, null, ["left", "right"], false, stdout, true);

        Assert.Equal(format == CliOutputFormat.Png ? "PNG" : "WEBP", Signature(stdout.ToArray()));
        stdout.Position = 0;
        using var decoded = Image.Load<Rgba32>(stdout);
        Assert.Equal(source.Width, decoded.Width);
        Assert.Equal(source.Height, decoded.Height);
        Assert.Equal(source[0, 0], decoded[0, 0]);
        Assert.Equal(source[1, 0], decoded[1, 0]);
        Assert.Equal(source[0, 1], decoded[0, 1]);
        Assert.Equal(source[1, 1], decoded[1, 1]);
    }

    [Fact]
    public void Write_EncoderFailureLeavesStdoutEmpty()
    {
        using var source = TestImage();
        using var stdout = new MemoryStream();

        Assert.Throws<ArgumentOutOfRangeException>(() => DiffImageWriter.Write(
            source,
            CliOutputFormat.Console,
            null,
            ["left", "right"],
            false,
            stdout,
            true));

        Assert.Equal(0, stdout.Length);
    }

    [Fact]
    public void Write_TerminalStdoutRequiresOutputAndWritesNothing()
    {
        using var source = TestImage();
        using var stdout = new MemoryStream();

        var exception = Assert.Throws<DiffImageOutputException>(() =>
            DiffImageWriter.Write(source, CliOutputFormat.Png, null, ["left", "right"], false, stdout, false));

        Assert.Equal(ExitCodes.Usage, exception.ExitCode);
        Assert.Contains("requires -o/--output", exception.Message);
        Assert.Equal(0, stdout.Length);
    }

    [Fact]
    public void Write_RefusesExistingOutputWithoutForceAndPreservesIt()
    {
        using var directory = new TemporaryDirectory();
        var output = Path.Combine(directory.Path, "report.png");
        var original = "keep"u8.ToArray();
        File.WriteAllBytes(output, original);
        using var source = TestImage();

        var exception = Assert.Throws<DiffImageOutputException>(() =>
            DiffImageWriter.Write(source, CliOutputFormat.Png, output, ["left", "right"], false, Stream.Null, true));

        Assert.Equal(ExitCodes.IoError, exception.ExitCode);
        Assert.Equal(original, File.ReadAllBytes(output));
        AssertNoTemporaryFiles(directory.Path);
    }

    [Fact]
    public void Write_ForceAtomicallyReplacesExistingOutput()
    {
        using var directory = new TemporaryDirectory();
        var output = Path.Combine(directory.Path, "report.webp");
        File.WriteAllText(output, "old");
        using var source = TestImage();

        DiffImageWriter.Write(source, CliOutputFormat.Webp, output, ["left", "right"], true, Stream.Null, true);

        using var decoded = Image.Load<Rgba32>(output);
        Assert.Equal(new Rgba32(1, 2, 3, 0), decoded[0, 0]);
        Assert.Equal(new Rgba32(250, 240, 230, 220), decoded[1, 1]);
        AssertNoTemporaryFiles(directory.Path);
    }

    [Fact]
    public void Write_MoveFailurePreservesExistingTargetAndCleansTemporaryFile()
    {
        using var directory = new TemporaryDirectory();
        var output = Path.Combine(directory.Path, "report.png");
        var original = "winner"u8.ToArray();
        File.WriteAllBytes(output, original);
        using var encoded = new MemoryStream("new image"u8.ToArray());

        Assert.Throws<IOException>(() => DiffImageWriter.WriteFile(
            encoded,
            output,
            true,
            (_, _, _) => throw new IOException("synthetic move failure")));

        Assert.Equal(original, File.ReadAllBytes(output));
        AssertNoTemporaryFiles(directory.Path);
    }

    [Fact]
    public void Write_FileOpenFailureLeavesNoTargetOrTemporaryFile()
    {
        using var directory = new TemporaryDirectory();
        var output = Path.Combine(directory.Path, "report.png");
        using var encoded = new ThrowingReadStream();

        Assert.Throws<IOException>(() =>
            DiffImageWriter.WriteFile(encoded, output, false, File.Move));

        Assert.False(File.Exists(output));
        AssertNoTemporaryFiles(directory.Path);
    }

    [Fact]
    public void ValidateDestination_RejectsEitherNormalizedInputAndMissingDirectory()
    {
        using var directory = new TemporaryDirectory();
        var left = Path.Combine(directory.Path, "left.Map.Gbx");
        var right = Path.Combine(directory.Path, "right.Map.Gbx");

        var sameInput = Assert.Throws<DiffImageOutputException>(() =>
            DiffImageWriter.ValidateDestination(Path.Combine(directory.Path, ".", "right.Map.Gbx"), [left, right], true, true));
        Assert.Equal(ExitCodes.IoError, sameInput.ExitCode);

        var missingDirectory = Path.Combine(directory.Path, "missing", "report.png");
        Assert.Throws<DirectoryNotFoundException>(() =>
            DiffImageWriter.ValidateDestination(missingDirectory, [left, right], false, true));
        Assert.False(Directory.Exists(Path.GetDirectoryName(missingDirectory)));

        var directoryTarget = Assert.Throws<IOException>(() =>
            DiffImageWriter.ValidateDestination(directory.Path, [left, right], true, true));
        Assert.Contains("is a directory", directoryTarget.Message);
    }

    [Fact]
    public void ValidateDestination_RejectsInputSymlinkTarget()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Symbolic link creation may require elevated privileges on Windows.");
        using var directory = new TemporaryDirectory();
        var inputTarget = Path.Combine(directory.Path, "source.Map.Gbx");
        var inputLink = Path.Combine(directory.Path, "source-link.Map.Gbx");
        File.WriteAllText(inputTarget, "source");
        File.CreateSymbolicLink(inputLink, inputTarget);

        var exception = Assert.Throws<DiffImageOutputException>(() =>
            DiffImageWriter.ValidateDestination(inputTarget, [inputLink, "other.Map.Gbx"], true, true));

        Assert.Equal(ExitCodes.IoError, exception.ExitCode);
        Assert.Contains("differ from both input paths", exception.Message);
        Assert.Equal("source", File.ReadAllText(inputTarget));
    }

    [Fact]
    public void ValidateDestination_RejectsOutputSymlinkToInput()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Symbolic link creation may require elevated privileges on Windows.");
        using var directory = new TemporaryDirectory();
        var input = Path.Combine(directory.Path, "source.Map.Gbx");
        var outputLink = Path.Combine(directory.Path, "report.png");
        File.WriteAllText(input, "source");
        File.CreateSymbolicLink(outputLink, input);

        var exception = Assert.Throws<DiffImageOutputException>(() =>
            DiffImageWriter.ValidateDestination(outputLink, [input, "other.Map.Gbx"], true, true));

        Assert.Equal(ExitCodes.IoError, exception.ExitCode);
        Assert.Contains("differ from both input paths", exception.Message);
        Assert.Equal("source", File.ReadAllText(input));
    }

    [Fact]
    public void ValidateDestination_RejectsOutputThroughSymlinkedParent()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Symbolic link creation may require elevated privileges on Windows.");
        using var directory = new TemporaryDirectory();
        var realDirectory = Path.Combine(directory.Path, "real");
        var linkedDirectory = Path.Combine(directory.Path, "linked");
        Directory.CreateDirectory(realDirectory);
        Directory.CreateSymbolicLink(linkedDirectory, realDirectory);
        var input = Path.Combine(realDirectory, "source.Map.Gbx");
        File.WriteAllText(input, "source");
        var output = Path.Combine(linkedDirectory, "source.Map.Gbx");

        var exception = Assert.Throws<DiffImageOutputException>(() =>
            DiffImageWriter.ValidateDestination(output, [input, "other.Map.Gbx"], true, true));

        Assert.Equal(ExitCodes.IoError, exception.ExitCode);
        Assert.Contains("differ from both input paths", exception.Message);
        Assert.Equal("source", File.ReadAllText(input));
    }

    private static Image<Rgba32> TestImage()
    {
        var image = new Image<Rgba32>(2, 2);
        image[0, 0] = new(1, 2, 3, 0);
        image[1, 0] = new(5, 6, 7, 8);
        image[0, 1] = new(9, 10, 11, 12);
        image[1, 1] = new(250, 240, 230, 220);
        return image;
    }

    private static string Signature(byte[] bytes) =>
        bytes.AsSpan(1, 3).SequenceEqual("PNG"u8)
            ? "PNG"
            : bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8)
                && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8)
                ? "WEBP"
                : "unknown";

    private static void AssertNoTemporaryFiles(string directory) =>
        Assert.Empty(Directory.EnumerateFiles(directory, ".*.tmp"));

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"gbx-size-tree-image-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class ThrowingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("synthetic read failure");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
