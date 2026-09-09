using GbxSizeTree.Cli;

namespace GbxSizeTree.Tests.Cli;

public sealed class HelpTextTests
{
    [Fact]
    public void DiffHelp_DoesNotRequireMapPaths()
    {
        var (options, error) = ArgParser.Parse(["diff", "--help"]);

        Assert.Null(error);
        Assert.NotNull(options);
        Assert.True(options.Diff);
        Assert.True(options.ShowHelp);
        Assert.Empty(options.InputPaths);
    }

    [Fact]
    public void DiffHelp_DescribesDefaultAndAllCoverageWithoutOverclaiming()
    {
        var help = HelpText.Diff("test");

        Assert.Contains("OLD_MAP", help);
        Assert.Contains("NEW_MAP", help);
        Assert.Contains("embedded ZIP entries", help);
        Assert.Contains("placed items", help);
        Assert.Contains("ordinary blocks", help);
        Assert.Contains("map metadata", help);
        Assert.Contains("Baked blocks and serialized content fingerprints are included only with --all.", help);
        Assert.Contains("identify changed bytes, not their meaning", help);
        Assert.Contains("this is not a", help);
        Assert.Contains("claim that every item property has deep semantic support", help);
        Assert.Contains("Warnings call out opaque or otherwise", help);
        Assert.Contains("unavailable metadata", help);
    }

    [Fact]
    public void DiffHelp_ExplainsBoundedNonAdditiveMeasurementsAndCurrentBudget()
    {
        var help = HelpText.Diff("test");

        Assert.Contains("recompress each original body after removing", help);
        Assert.Contains("one changed embed", help);
        Assert.Contains("context-dependent", help);
        Assert.Contains("do not add up to map size", help);
        Assert.Contains("bounded trial budget (currently 256 trials per map)", help);
        Assert.Contains("may be unavailable", help);
        Assert.DoesNotContain("at most 8", help);
    }

    [Fact]
    public void DiffHelp_DocumentsTextFormatsColorAndSafeLocalUse()
    {
        var help = HelpText.Diff("test");

        Assert.Contains("NO_COLOR", help);
        Assert.Contains("TERM=dumb", help);
        Assert.Contains("--color and --no-color override", help);
        Assert.Contains("--html", help);
        Assert.Contains("--styled", help);
        Assert.Contains("--not-styled", help);
        Assert.Contains("--markdown, --md", help);
        Assert.Contains("--json", help);
        Assert.Contains("> diff.html", help);
        Assert.Contains("> diff.md", help);
        Assert.Contains("> diff.json", help);
        Assert.Contains("nothing is published or uploaded", help, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiffHelp_DocumentsImageFormatAndOutputContracts()
    {
        var help = HelpText.Diff("test");

        Assert.Contains("--png", help);
        Assert.Contains("--webp", help);
        Assert.Contains("lossless WebP", help);
        Assert.Contains("purpose-built", help);
        Assert.Contains("not screenshots of HTML", help);
        Assert.Contains("--all controls their coverage too", help);
        Assert.Contains("mutually exclusive formats", help);
        Assert.Contains("only when it is redirected", help);
        Assert.Contains("at a terminal, use -o/--output", help);
        Assert.Contains("same-directory temporary file", help);
        Assert.Contains("atomic rename", help);
        Assert.Contains("refuses either input", help);
        Assert.Contains("unless --force", help);
        Assert.Contains("does not create parent", help);
        Assert.Contains("extension never selects or changes the", help);
        Assert.Contains("format. Successful image file output", help);
        Assert.Contains("leaves stdout empty", help);
        Assert.Contains("errors", help);
        Assert.Contains("stderr", help);
        Assert.Contains("--pause/--no-pause", help);
        Assert.Contains("-i/--interactive", help);
        Assert.Contains("-n/--non-interactive", help);
        Assert.Contains("--color/--no-color", help);
        Assert.Contains("--styled/--not-styled", help);
        Assert.Contains("diff --help needs no map paths", help);
        Assert.Contains("--png --all -o diff.png Before.Map.Gbx After.Map.Gbx", help);
        Assert.Contains("--webp Before.Map.Gbx After.Map.Gbx > diff.webp", help);
    }

    [Fact]
    public void DiffHelp_DocumentsTransientProgressWithoutPromisingImmediateEta()
    {
        var help = HelpText.Diff("test");

        Assert.Contains("Terminal progress shows actual stages and elapsed time", help);
        Assert.Contains("ETA stays unknown until", help);
        Assert.Contains("cleared before final output", help);
        Assert.Contains("omitted when stderr is", help);
        Assert.Contains("redirected", help);
        Assert.DoesNotContain("immediate ETA", help, StringComparison.OrdinalIgnoreCase);
    }
}
