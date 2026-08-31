using GBX.NET;
using GbxSizeTree.Abstractions;
using GbxSizeTree.Actions;
using GbxSizeTree.Model;
using GbxSizeTree.Session;
using GbxSizeTree.Tests.Fixtures;

namespace GbxSizeTree.Tests.Session;

public sealed class MapSessionTests
{
    [Fact]
    public void ApplyThenUndo_MaterializesTheExactOriginalBytesWithoutReparsing()
    {
        var path = Path.GetTempFileName();
        var original = new byte[] { 1, 2, 3, 4, 5 };
        File.WriteAllBytes(path, original);

        try
        {
            var session = MapSession.Open(
                path,
                new ActionRegistry([new FakeAction("lossless", ActionTier.Lossless)]),
                new FakeAnalyzer(),
                NullStatusSink.Instance);

            Assert.True(session.Apply("LOSSLESS"));
            Assert.Equal("lossless", Assert.Single(session.Applied).ActionId);
            Assert.Equal("lossless", session.Undo());

            var materialized = session.Materialize();
            Assert.Equal(original, materialized.Bytes);
            Assert.Equal(original.LongLength, materialized.FileBytes);
            Assert.Same(session.Baseline, materialized.Analysis);
            Assert.True(materialized.Validation.Ok);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Apply_RejectsUnknownAndEditorOnlyActions()
    {
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, [1]);

        try
        {
            var session = MapSession.Open(
                path,
                new ActionRegistry([new FakeAction("editor", ActionTier.EditorOnly)]),
                new FakeAnalyzer(),
                NullStatusSink.Instance);

            Assert.False(session.Apply("missing"));
            Assert.False(session.Apply("editor"));
            Assert.Empty(session.Applied);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class FakeAnalyzer : IMapAnalyzer
    {
        public MapAnalysis Analyze(MapSource source, AnalyzeOptions options) => FakeAnalysis.Build();
    }

    private sealed class FakeAction(string id, ActionTier tier) : IMapAction
    {
        public string Id => id;
        public string Title => id;
        public ActionTier Tier => tier;
        public string Consequence => string.Empty;
        public string HowToManually => string.Empty;
        public bool DefaultOn => tier == ActionTier.Lossless;
        public int Order => 1;

        public ActionApplicability Detect(ActionDetectContext ctx) =>
            new(true, 0, EstimateKind.Unknown, "test");

        public ActionResult Apply(ActionApplyContext ctx) =>
            throw new InvalidOperationException("Undo should prevent this action from being applied.");
    }
}
