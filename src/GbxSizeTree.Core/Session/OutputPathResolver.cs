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
            outputFileName = $"{MarkStem(fileName[..^mapSuffix.Length])}{suffix}";
        }
        else
        {
            var extension = Path.GetExtension(fileName);
            outputFileName = extension.Length == 0
                ? MarkStem(fileName)
                : $"{MarkStem(fileName[..^extension.Length])}{extension}";
        }

        return Path.Combine(directory, outputFileName);
    }

    /// <summary>
    /// Appends the `_recompressed` marker without compounding it: re-running the tool on its
    /// own output must not yield `X_recompressed_recompressed`, and the result never equals
    /// the input stem (that would collide with the never-overwrite-input rule).
    /// </summary>
    private static string MarkStem(string stem)
    {
        var baseStem = System.Text.RegularExpressions.Regex.Replace(
            stem, @"(_recompressed\d*)+$", string.Empty, System.Text.RegularExpressions.RegexOptions.None);
        var candidate = baseStem + "_recompressed";
        return candidate == stem ? baseStem + "_recompressed2" : candidate;
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
            throw new GbxSizeTreeOutputException(
                "Output path must differ from the input path.",
                OutputFailureKind.SameAsInput);
        }

        if (File.Exists(normalizedOutput) && !force)
        {
            throw new GbxSizeTreeOutputException(
                $"Output file already exists: {normalizedOutput}. Use --force to overwrite it.",
                OutputFailureKind.AlreadyExists);
        }
    }
}

public enum OutputFailureKind
{
    General,
    SameAsInput,
    /// <summary>Recoverable with --force (or an interactive overwrite confirmation).</summary>
    AlreadyExists,
    ValidationFailed,
}

/// <summary>An output-path I/O failure; the CLI contract assigns these failures exit code 4.</summary>
public sealed class GbxSizeTreeOutputException(
    string message,
    OutputFailureKind kind = OutputFailureKind.General) : Exception(message)
{
    public OutputFailureKind Kind { get; } = kind;
    public int ExitCode { get; } = 4;
}
