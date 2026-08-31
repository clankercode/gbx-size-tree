namespace GbxSizeTree.Cli.Platform;

/// <summary>Prevents a GUI-owned console from disappearing before its result can be read.</summary>
public static class PauseOnExit
{
    public static void PauseIfNeeded(bool? overridePause, LaunchKind kind)
    {
        var shouldPause = overridePause == true
            || (overridePause is null && kind == LaunchKind.GuiOwnConsole);
        if (!shouldPause)
        {
            return;
        }

        // A GUI-owned console always has real streams; redirected stdin means a pipe
        // misdetected as GUI, where "press any key" would just hang or spam the log.
        if (overridePause is null && (Console.IsInputRedirected || Console.IsOutputRedirected))
        {
            return;
        }

        try
        {
            Console.Write("\nPress any key to exit...");
        }
        catch
        {
            return;
        }

        try
        {
            Console.ReadKey(intercept: true);
        }
        catch
        {
            try
            {
                Console.ReadLine();
            }
            catch
            {
                // A redirected or unavailable input stream must not turn shutdown into a failure.
            }
        }
    }
}
