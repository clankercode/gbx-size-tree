using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

namespace GbxSizeTree.Cli.Output;

public static class DiffImageWriter
{
    public static void ValidateDestination(
        string? outputPath,
        IReadOnlyList<string> inputPaths,
        bool force,
        bool stdoutRedirected)
    {
        ArgumentNullException.ThrowIfNull(inputPaths);
        if (outputPath is null)
        {
            if (!stdoutRedirected)
            {
                throw new DiffImageOutputException(
                    "Image output requires -o/--output when stdout is a terminal.",
                    ExitCodes.Usage);
            }

            return;
        }

        var normalizedOutput = Path.GetFullPath(outputPath);
        var canonicalOutput = ResolveCanonicalPath(normalizedOutput);
        if (inputPaths.Any(path => string.Equals(
                ResolveCanonicalPath(path),
                canonicalOutput,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
        {
            throw new DiffImageOutputException(
                "Image output path must differ from both input paths.",
                ExitCodes.IoError);
        }

        var directory = Path.GetDirectoryName(normalizedOutput)!;
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                $"Output directory does not exist: {directory}");
        }

        if (File.Exists(normalizedOutput) && !force)
        {
            throw new DiffImageOutputException(
                $"Output file already exists: {normalizedOutput}. Use --force to overwrite it.",
                ExitCodes.IoError);
        }

        if (Directory.Exists(normalizedOutput))
        {
            throw new IOException($"Output path is a directory: {normalizedOutput}");
        }
    }

    public static void Write(
        Image image,
        CliOutputFormat format,
        string? outputPath,
        IReadOnlyList<string> inputPaths,
        bool force,
        Stream stdout,
        bool stdoutRedirected)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(stdout);
        ValidateDestination(outputPath, inputPaths, force, stdoutRedirected);

        using var encoded = new MemoryStream();
        image.Save(encoded, Encoder(format));
        encoded.Position = 0;

        if (outputPath is null)
        {
            encoded.CopyTo(stdout);
            stdout.Flush();
            return;
        }

        WriteFile(encoded, Path.GetFullPath(outputPath), force, File.Move);
    }

    private static string ResolveCanonicalPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)!;
        var canonical = root;
        foreach (var segment in fullPath[root.Length..].Split(
                     Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(canonical, segment);
            FileSystemInfo entry = Directory.Exists(candidate)
                ? new DirectoryInfo(candidate)
                : new FileInfo(candidate);
            canonical = entry.LinkTarget is not null
                ? entry.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? candidate
                : candidate;
        }

        return Path.GetFullPath(canonical);
    }

    private static IImageEncoder Encoder(CliOutputFormat format) => format switch
    {
        CliOutputFormat.Png => new PngEncoder
        {
            TransparentColorMode = PngTransparentColorMode.Preserve,
        },
        CliOutputFormat.Webp => new WebpEncoder
        {
            FileFormat = WebpFileFormatType.Lossless,
            NearLossless = false,
            TransparentColorMode = WebpTransparentColorMode.Preserve,
        },
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "An image output format is required."),
    };

    internal static void WriteFile(
        Stream encoded,
        string outputPath,
        bool force,
        Action<string, string, bool> move)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        ArgumentNullException.ThrowIfNull(move);
        var directory = Path.GetDirectoryName(outputPath)!;
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.SequentialScan))
            {
                encoded.CopyTo(output);
                output.Flush(flushToDisk: true);
            }

            move(temporaryPath, outputPath, force);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch
            {
                // Preserve the primary write failure when cleanup is also refused.
            }
        }
    }
}

public sealed class DiffImageOutputException(string message, int exitCode) : IOException(message)
{
    public int ExitCode { get; } = exitCode;
}
