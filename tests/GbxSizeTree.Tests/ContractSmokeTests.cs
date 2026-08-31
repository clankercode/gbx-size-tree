using System.Text.Json;
using GbxSizeTree.Model;
using GbxSizeTree.Model.Json;
using GbxSizeTree.Tests.Fixtures;

namespace GbxSizeTree.Tests;

public class ContractSmokeTests
{
    [Fact]
    public void FakeAnalysis_RoundTripsThroughSourceGeneratedJson()
    {
        var analysis = FakeAnalysis.Build();
        var json = JsonSerializer.Serialize(analysis, AnalysisJsonContext.Default.MapAnalysis);
        var back = JsonSerializer.Deserialize(json, AnalysisJsonContext.Default.MapAnalysis);

        Assert.NotNull(back);
        Assert.Equal(analysis.FileBytes, back.FileBytes);
        Assert.Equal(analysis.Tree.Children.Count, back.Tree.Children.Count);
        Assert.Contains("body.residual", json);
    }

    [Fact]
    public void SampleMap_GroundTruthConstantsAreConsistent()
    {
        SampleMap.SkipUnlessAvailable();
        Assert.Equal(SampleMap.FileBytes, new FileInfo(SampleMap.Path).Length);
    }
}
