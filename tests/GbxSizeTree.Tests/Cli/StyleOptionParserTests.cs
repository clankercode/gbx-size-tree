using GbxSizeTree.Cli;

namespace GbxSizeTree.Tests.Cli;

public class StyleOptionParserTests
{
    [Theory]
    [InlineData("--styled", true)]
    [InlineData("--not-styled", false)]
    public void Parse_HtmlDiffStyleFlagSetsExpectedOption(string styleFlag, bool expected)
    {
        var (options, error) = ArgParser.Parse([
            "diff", "before.Map.Gbx", "after.Map.Gbx", "--html", styleFlag,
        ]);

        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Equal(expected, options.Styled);
    }

    [Fact]
    public void Parse_HtmlDiffIsStyledByDefault()
    {
        var (options, error) = ArgParser.Parse([
            "diff", "before.Map.Gbx", "after.Map.Gbx", "--html",
        ]);

        Assert.Null(error);
        Assert.NotNull(options);
        Assert.True(options.Styled);
    }

    [Theory]
    [InlineData("--styled", "--not-styled")]
    [InlineData("--not-styled", "--styled")]
    public void Parse_ConflictingStyleFlagsReturnDeterministicError(string first, string second)
    {
        var (options, error) = ArgParser.Parse([
            "diff", "before.Map.Gbx", "after.Map.Gbx", "--html", first, second,
        ]);

        Assert.Null(options);
        Assert.Equal("--styled and --not-styled cannot be combined.", error);
    }

    [Theory]
    [InlineData("--html map.Map.Gbx --styled")]
    [InlineData("diff before.Map.Gbx after.Map.Gbx --styled")]
    [InlineData("--not-styled map.Map.Gbx")]
    public void Parse_StyleFlagsOutsideHtmlDiffAreRejected(string commandLine)
    {
        var (options, error) = ArgParser.Parse(commandLine.Split(' '));

        Assert.Null(options);
        Assert.Equal("--styled/--not-styled can only be used with diff --html.", error);
    }

    [Theory]
    [InlineData("diff --help --styled")]
    [InlineData("diff --styled --help")]
    public void Parse_DiffHelpRejectsStyleWithoutHtml(string commandLine)
    {
        var (options, error) = ArgParser.Parse(commandLine.Split(' '));

        Assert.Null(options);
        Assert.Equal("--styled/--not-styled can only be used with diff --html.", error);
    }

    [Theory]
    [InlineData("diff --help --styled --not-styled")]
    [InlineData("diff --not-styled --styled --help")]
    public void Parse_DiffHelpStyleConflictUsesDeterministicError(string commandLine)
    {
        var (options, error) = ArgParser.Parse(commandLine.Split(' '));

        Assert.Null(options);
        Assert.Equal("--styled and --not-styled cannot be combined.", error);
    }

    [Fact]
    public void Parse_HtmlDiffStyleHelpDoesNotRequireMapPaths()
    {
        var (options, error) = ArgParser.Parse(["diff", "--html", "--not-styled", "--help"]);

        Assert.Null(error);
        Assert.NotNull(options);
        Assert.True(options.Diff);
        Assert.Equal(CliOutputFormat.Html, options.Format);
        Assert.False(options.Styled);
        Assert.True(options.ShowHelp);
    }
}
