namespace GbxSizeTree.Session;

/// <summary>
/// Resolves the output map path while preserving the original <c>.Map.Gbx</c> suffix casing
/// described in docs/FORMAT-NOTES.md.
/// </summary>
public static class OutputPathResolver
{
    public static string Resolve(string inputPath, string? requestedOutput)
    {
        if (requestedOutput is not null)
        {
            return Path.GetFullPath(requestedOutput);
        }

        var normalizedInput = Path.GetFullPath(inputPath);
        var directory = Path.GetDirectoryName(normalizedInput)!;
        var fileName = Path.GetFileName(normalizedInput);
        const string mapSuffix = ".Map.Gbx";

        string outputFileName;
        if (fileName.EndsWith(mapSuffix, StringComparison.OrdinalIgnoreCase))
        {
            var suffix = fileName[^mapSuffix.Length..];
            outputFileName = $"{fileName[..^mapSuffix.Length]}_recompressed{suffix}";
        }
        else
        {
            var extension = Path.GetExtension(fileName);
            outputFileName = extension.Length == 0
                ? $"{fileName}_recompressed"
                : $"{fileName[..^extension.Length]}_recompressed{extension}";
        }

        return Path.Combine(directory, outputFileName);
    }

    public static void EnsureWritable(string inputPath, string outputPath, bool force)
    {
        var normalizedInput = Path.GetFullPath(inputPath);
        var normalizedOutput = Path.GetFullPath(outputPath);
        var directoryComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var sameDirectory = string.Equals(
            Path.GetDirectoryName(normalizedInput),
            Path.GetDirectoryName(normalizedOutput),
            directoryComparison);
        var sameFileName = string.Equals(
            Path.GetFileName(normalizedInput),
            Path.GetFileName(normalizedOutput),
            StringComparison.OrdinalIgnoreCase);

        if (sameDirectory && sameFileName)
        {
            throw new GbxSizeTreeOutputException("Output path must differ from the input path.");
        }

        if (File.Exists(normalizedOutput) && !force)
        {
            throw new GbxSizeTreeOutputException(
                $"Output file already exists: {normalizedOutput}. Use --force to overwrite it.");
        }
    }
}

/// <summary>An output-path I/O failure; the CLI contract assigns these failures exit code 4.</summary>
public sealed class GbxSizeTreeOutputException(string message) : Exception(message)
{
    public int ExitCode { get; } = 4;
}
