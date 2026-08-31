using System.Diagnostics;

namespace GbxSizeTree.Cli.Platform;

/// <summary>Uses the Zenity desktop dialog to select a Trackmania map file.</summary>
public sealed class ZenityFilePicker(string executableName = "zenity") : IFilePicker
{
    public string? PickMapFile()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executableName,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("--file-selection");
            startInfo.ArgumentList.Add("--title=Select a Trackmania map");
            // Map extension and conventional map location follow docs/FORMAT-NOTES.md.
            startInfo.ArgumentList.Add("--file-filter=*.Map.Gbx *.Map.gbx");

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
            {
                var initialPath = Path.Combine(home, "Documents", "Trackmania", "Maps")
                    + Path.DirectorySeparatorChar;
                startInfo.ArgumentList.Add($"--filename={initialPath}");
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                return null;
            }

            var path = output.Trim();
            return path.Length == 0 ? null : path;
        }
        catch
        {
            return null;
        }
    }
}
