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
