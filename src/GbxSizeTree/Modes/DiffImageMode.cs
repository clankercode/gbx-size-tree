using GbxSizeTree.Cli.Output;
using GbxSizeTree.Cli.Rendering;

namespace GbxSizeTree.Cli.Modes;

public static class DiffImageMode
{
    public static int Run(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.InputPaths.Count != 2)
        {
            throw new ArgumentException("diff requires exactly two map paths.");
        }

        DiffImageWriter.ValidateDestination(
            options.OutputPath,
            options.InputPaths,
            options.Force,
            Console.IsOutputRedirected);
        return RunValidated(options);
    }

    internal static int RunValidated(CliOptions options)
    {
        DiffReport report;
        using (var progress = TerminalProgress.Create())
            report = DiffMode.CompareFiles(
                options.InputPaths[0],
                options.InputPaths[1],
                options.CompareAll,
                progress: progress is null ? null : progress.Report);
        using var image = DiffInfographic.Render(
            report,
            options.InputPaths[0],
            options.InputPaths[1]);
        DiffImageWriter.Write(
            image,
            options.Format,
            options.OutputPath,
            options.InputPaths,
            options.Force,
            Console.OpenStandardOutput(),
            Console.IsOutputRedirected);
        return ExitCodes.Ok;
    }
}
