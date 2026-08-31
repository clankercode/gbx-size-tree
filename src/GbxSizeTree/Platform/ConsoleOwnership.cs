using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace GbxSizeTree.Cli.Platform;

/// <summary>Reads the number of processes attached to the current Windows console.</summary>
public static class ConsoleOwnership
{
    public static int? GetConsoleProcessCount()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            return GetConsoleProcessCountWindows();
        }
        catch
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static int? GetConsoleProcessCountWindows()
    {
        var processIds = new uint[2];
        var count = GetConsoleProcessList(processIds, (uint)processIds.Length);
        return count == 0 ? null : checked((int)count);
    }

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [SupportedOSPlatform("windows")]
    private static extern uint GetConsoleProcessList([Out] uint[] processList, uint processCount);
}
