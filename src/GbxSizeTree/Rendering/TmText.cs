using System.Text.RegularExpressions;

namespace GbxSizeTree.Cli.Rendering;

/// <summary>
/// Strips Trackmania $-format codes for terminal display. The raw strings (with codes)
/// stay untouched in <see cref="Model.MapFacts"/> and the JSON report.
/// </summary>
public static partial class TmText
{
    // $$ is a literal $; $l/$h/$p may carry a [target] that is removed with the code while
    // the link text stays; colors are 1-3 hex digits (greedy, so truncated codes still
    // strip); any other single character after $ is a style/reset code.
    [GeneratedRegex(@"\$(\$|[lhp]\[[^\]]*\]|[lhp]|[0-9a-f]{1,3}|.)", RegexOptions.IgnoreCase)]
    private static partial Regex FormatCode();

    public static string Deformat(string text) =>
        string.IsNullOrEmpty(text)
            ? text
            : FormatCode().Replace(text, static m => m.Groups[1].Value == "$" ? "$" : "");
}
