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
