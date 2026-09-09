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
        Assert.Contains("fingerprints identify changed bytes,", help);
        Assert.Contains("not their meaning", help);
        Assert.Contains("this is not a", help);
        Assert.Contains("claim that every item property has deep semantic support", help);
        Assert.Contains("Warnings call out opaque or otherwise unavailable metadata.", help);
    }

    [Fact]
    public void DiffHelp_ExplainsBoundedNonAdditiveMeasurementsWithoutFixedBudget()
    {
        var help = HelpText.Diff("test");

        Assert.Contains("recompress each original body after removing one changed embed", help);
        Assert.Contains("context-dependent", help);
        Assert.Contains("do not add up to map size", help);
        Assert.Contains("bounded trial budget", help);
        Assert.Contains("may be unavailable", help);
        Assert.DoesNotContain("at most 8", help);
        Assert.DoesNotContain("at most 256", help);
    }

    [Fact]
    public void DiffHelp_DocumentsColorFormatsAndLocalRedirection()
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
        Assert.Contains("nothing is published", help);
        Assert.DoesNotContain("--image", help);
    }
}
