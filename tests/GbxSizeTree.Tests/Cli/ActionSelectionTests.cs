using GbxSizeTree.Actions;
using GbxSizeTree.Cli;
using GbxSizeTree.Cli.Modes;

namespace GbxSizeTree.Tests.Cli;

public sealed class ActionSelectionTests
{
    [Fact]
    public void RequestedIds_MergesFlagsDefaultsAndExclusionsInRequestOrder()
    {
        var registry = new ActionRegistry([
            new FakeAction("default-a", defaultOn: true, order: 10),
            new FakeAction("default-b", defaultOn: true, order: 20),
            new FakeAction("custom", defaultOn: false, order: 30),
        ]);
        var options = new CliOptions
        {
            Optimize = true,
            StripLightmap = true,
            ThumbnailMode = "strip",
            Actions = ["custom", "DEFAULT-A"],
            NoActions = ["default-b"],
        };

        var ids = ActionSelection.RequestedIds(options, registry);

        Assert.Equal(["default-a", "strip-lightmap", "thumbnail", "custom"], ids);
    }

    [Fact]
    public void BuildSettings_TranslatesSharedActionSettings()
    {
        var settings = ActionSelection.BuildSettings(new CliOptions
        {
            EmbedStored = true,
            ThumbnailMode = "downscale:512",
            Experimental = true,
        });

        Assert.Equal("true", settings["stored"]);
        Assert.Equal("downscale:512", settings["mode"]);
        Assert.Equal("true", settings["experimental"]);
    }

    [Theory]
    [InlineData(nameof(CliOptions.Optimize))]
    [InlineData(nameof(CliOptions.StripLightmap))]
    [InlineData(nameof(CliOptions.DryRun))]
    [InlineData(nameof(CliOptions.Attribute))]
    public void WantsOptimization_TrueForEachOptimizationFlag(string property)
    {
        Assert.False(ActionSelection.WantsOptimization(new CliOptions()));
        Assert.True(ActionSelection.WantsOptimization(SetFlag(new CliOptions(), property)));
    }

    private static CliOptions SetFlag(CliOptions options, string property) => property switch
    {
        nameof(CliOptions.Optimize) => options with { Optimize = true },
        nameof(CliOptions.StripLightmap) => options with { StripLightmap = true },
        nameof(CliOptions.DryRun) => options with { DryRun = true },
        nameof(CliOptions.Attribute) => options with { Attribute = true },
        _ => throw new ArgumentOutOfRangeException(nameof(property)),
    };

    private sealed class FakeAction(string id, bool defaultOn, int order) : IMapAction
    {
        public string Id => id;
        public string Title => id;
        public ActionTier Tier => ActionTier.Lossless;
        public string Consequence => string.Empty;
        public string HowToManually => string.Empty;
        public bool DefaultOn => defaultOn;
        public int Order => order;
        public ActionApplicability Detect(ActionDetectContext ctx) =>
            new(true, 0, EstimateKind.Unknown, "test");
        public ActionResult Apply(ActionApplyContext ctx) => ActionResult.NoChange("test");
    }
}
