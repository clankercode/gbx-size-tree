using GbxSizeTree.Cli;

namespace GbxSizeTree.Tests.Cli;

public class ArgParserTests
{
    [Theory]
    [InlineData("--header-only", nameof(CliOptions.HeaderOnly), "True")]
    [InlineData("--estimate-compressed", nameof(CliOptions.EstimateCompressed), "True")]
    [InlineData("--top 50", nameof(CliOptions.TopN), "50")]
    [InlineData("--json", nameof(CliOptions.Json), "True")]
    [InlineData("--html", nameof(CliOptions.Format), "Html")]
    [InlineData("--markdown", nameof(CliOptions.Format), "Markdown")]
    [InlineData("--md", nameof(CliOptions.Format), "Markdown")]
    [InlineData("diff a.Map.Gbx b.Map.Gbx", nameof(CliOptions.Diff), "True")]
    [InlineData("--all", nameof(CliOptions.CompareAll), "True")]
    [InlineData("--color", nameof(CliOptions.Color), "True")]
    [InlineData("--no-color", nameof(CliOptions.Color), "False")]
    [InlineData("--interactive", nameof(CliOptions.Interactive), "True")]
    [InlineData("-i", nameof(CliOptions.Interactive), "True")]
    [InlineData("-n", nameof(CliOptions.NonInteractive), "True")]
    [InlineData("--non-interactive", nameof(CliOptions.NonInteractive), "True")]
    [InlineData("--optimize", nameof(CliOptions.Optimize), "True")]
    [InlineData("-O", nameof(CliOptions.Optimize), "True")]
    [InlineData("--output result.Map.Gbx", nameof(CliOptions.OutputPath), "result.Map.Gbx")]
    [InlineData("-o result.Map.Gbx", nameof(CliOptions.OutputPath), "result.Map.Gbx")]
    [InlineData("--force", nameof(CliOptions.Force), "True")]
    [InlineData("--strip-lightmap", nameof(CliOptions.StripLightmap), "True")]
    [InlineData("--lighten-shadows 100", nameof(CliOptions.ShadowBrightnessFloor), "100")]
    [InlineData("--thumbnail keep", nameof(CliOptions.ThumbnailMode), "keep")]
    [InlineData("--embed-stored", nameof(CliOptions.EmbedStored), "True")]
    [InlineData("--action embed-zip", nameof(CliOptions.Actions), "embed-zip")]
    [InlineData("--no-action resave", nameof(CliOptions.NoActions), "resave")]
    [InlineData("--dry-run", nameof(CliOptions.DryRun), "True")]
    [InlineData("--attribute", nameof(CliOptions.Attribute), "True")]
    [InlineData("--all-chunks", nameof(CliOptions.AllChunks), "True")]
    [InlineData("--unknown-chunks", nameof(CliOptions.UnknownChunks), "True")]
    [InlineData("--experimental", nameof(CliOptions.Experimental), "True")]
    [InlineData("--verbose", nameof(CliOptions.Verbose), "True")]
    [InlineData("-v", nameof(CliOptions.Verbose), "True")]
    [InlineData("--quiet", nameof(CliOptions.Quiet), "True")]
    [InlineData("-q", nameof(CliOptions.Quiet), "True")]
    [InlineData("--help", nameof(CliOptions.ShowHelp), "True")]
    [InlineData("-h", nameof(CliOptions.ShowHelp), "True")]
    [InlineData("--version", nameof(CliOptions.ShowVersion), "True")]
    [InlineData("--pause", nameof(CliOptions.Pause), "True")]
    [InlineData("--no-pause", nameof(CliOptions.Pause), "False")]
    public void Parse_EachFlagSetsExpectedOption(string commandLine, string property, string expected)
    {
        var (options, error) = ArgParser.Parse(commandLine.Split(' '));

        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Equal(expected, ReadValue(options, property));
    }

    [Theory]
    [InlineData("strip")]
    [InlineData("lossless")]
    [InlineData("recompress:80")]
    [InlineData("downscale:256")]
    public void Parse_ValidThumbnailModesAreAccepted(string mode)
    {
        var (options, error) = ArgParser.Parse(["--thumbnail", mode]);

        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Equal(mode, options.ThumbnailMode);
    }

    [Theory]
    [InlineData("recompress:0")]
    [InlineData("downscale:9999")]
    public void Parse_InvalidThumbnailModesAreRejected(string mode)
    {
        var (options, error) = ArgParser.Parse(["--thumbnail", mode]);

        Assert.Null(options);
        Assert.Contains("--thumbnail", error);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("256")]
    [InlineData("bright")]
    public void Parse_InvalidShadowBrightnessFloorsAreRejected(string value)
    {
        var (options, error) = ArgParser.Parse(["--lighten-shadows", value]);

        Assert.Null(options);
        Assert.Contains("--lighten-shadows", error);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("255", 255)]
    public void Parse_ShadowBrightnessFloorBoundsAreAccepted(string value, byte expected)
    {
        var (options, error) = ArgParser.Parse(["--lighten-shadows", value]);

        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Equal(expected, options.ShadowBrightnessFloor);
    }

    [Fact]
    public void Parse_LightenAndStripLightmapAreRejectedTogether()
    {
        var (options, error) = ArgParser.Parse(["--lighten-shadows", "100", "--strip-lightmap"]);

        Assert.Null(options);
        Assert.Contains("cannot be combined", error);
    }

    [Fact]
    public void Parse_LightenAndGenericStripLightmapAreRejectedTogether()
    {
        var (options, error) = ArgParser.Parse([
            "--lighten-shadows", "100", "--action", "strip-lightmap",
        ]);

        Assert.Null(options);
        Assert.Contains("cannot be combined", error);

        var (excludedOptions, excludedError) = ArgParser.Parse([
            "--lighten-shadows", "100", "--action", "strip-lightmap",
            "--no-action", "strip-lightmap",
        ]);
        Assert.Null(excludedError);
        Assert.NotNull(excludedOptions);
    }

    [Fact]
    public void Parse_RepeatedActionListsAccumulate()
    {
        var (options, error) = ArgParser.Parse(["--action", "a,b", "--action", "c"]);

        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Equal(["a", "b", "c"], options.Actions);
    }

    [Fact]
    public void Parse_RepeatedNoActionListsAccumulateAndAreCommaSplit()
    {
        var (options, error) = ArgParser.Parse(["--no-action", "a,b", "--no-action", "c,d"]);

        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Equal(["a", "b", "c", "d"], options.NoActions);
    }

    [Fact]
    public void Parse_UnknownFlagNamesItInError()
    {
        var (options, error) = ArgParser.Parse(["--frobnicate"]);

        Assert.Null(options);
        Assert.Contains("--frobnicate", error);
    }

    [Theory]
    [InlineData("--json", "-i")]
    [InlineData("--html", "--markdown")]
    [InlineData("--html", "--md")]
    [InlineData("--json", "--html")]
    [InlineData("-i", "-n")]
    [InlineData("-i", "--non-interactive")]
    [InlineData("-q", "-v")]
    public void Parse_ConflictingFlagsAreRejected(string first, string second)
    {
        var (options, error) = ArgParser.Parse([first, second]);

        Assert.Null(options);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("many")]
    public void Parse_NonPositiveOrNonNumericTopIsRejected(string value)
    {
        var (options, error) = ArgParser.Parse(["--top", value]);

        Assert.Null(options);
        Assert.Contains("--top", error);
    }

    [Fact]
    public void Parse_OutputWithoutValueIsRejected()
    {
        var (options, error) = ArgParser.Parse(["-o"]);

        Assert.Null(options);
        Assert.Contains("-o", error);
    }

    [Fact]
    public void Parse_DiffHelpDoesNotRequireMapPaths()
    {
        var (options, error) = ArgParser.Parse(["diff", "--help"]);
        Assert.Null(error);
        Assert.NotNull(options);
        Assert.True(options.Diff);
        Assert.True(options.ShowHelp);
    }

    [Fact]
    public void Parse_NoArgumentsReturnsDefaults()
    {
        var (options, error) = ArgParser.Parse([]);

        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Null(options.InputPath);
        Assert.Equal(20, options.TopN);
        Assert.Equal("keep", options.ThumbnailMode);
        Assert.Null(options.ShadowBrightnessFloor);
        Assert.Null(options.Color);
        Assert.Null(options.Pause);
    }

    [Fact]
    public void Parse_OnlyOneInputPathIsAllowed()
    {
        var (options, error) = ArgParser.Parse(["one.Map.Gbx", "two.Map.Gbx"]);

        Assert.Null(options);
        Assert.Contains("two.Map.Gbx", error);
    }

    [Fact]
    public void Parse_DoubleDashTreatsFollowingFlagLikeAPath()
    {
        var (options, error) = ArgParser.Parse(["--", "--unusual.Map.Gbx"]);

        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Equal("--unusual.Map.Gbx", options.InputPath);
    }

    [Fact]
    public void Parse_DoubleDashRejectsMultipleFollowingPaths()
    {
        var (options, error) = ArgParser.Parse(["--", "one.Map.Gbx", "two.Map.Gbx"]);

        Assert.Null(options);
        Assert.Contains("two.Map.Gbx", error);
    }

    private static string ReadValue(CliOptions options, string property) => property switch
    {
        nameof(CliOptions.Actions) => string.Join(',', options.Actions),
        nameof(CliOptions.NoActions) => string.Join(',', options.NoActions),
        _ => typeof(CliOptions).GetProperty(property)?.GetValue(options)?.ToString()
            ?? throw new InvalidOperationException($"No value for {property}."),
    };
}
