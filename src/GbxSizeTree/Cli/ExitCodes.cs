namespace GbxSizeTree.Cli;

/// <summary>
/// Defines the process exit-code contract documented in <c>docs/CONTRACTS.md</c>.
/// </summary>
public static class ExitCodes
{
    public const int Ok = 0;
    public const int Usage = 1;
    public const int ParseError = 2;
    public const int ValidationFailed = 3;
    public const int IoError = 4;
    public const int Internal = 5;

    public static int FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception is IOException ? IoError : Internal;
    }
}
