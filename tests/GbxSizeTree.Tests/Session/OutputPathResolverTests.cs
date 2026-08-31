using GbxSizeTree.Session;

namespace GbxSizeTree.Tests.Session;

public sealed class OutputPathResolverTests
{
    [Theory]
    [InlineData("A.Map.Gbx", "A_recompressed.Map.Gbx")]
    [InlineData("b.map.gbx", "b_recompressed.map.gbx")]
    [InlineData("weird.gbx", "weird_recompressed.gbx")]
    [InlineData("extensionless", "extensionless_recompressed")]
    public void Resolve_DefaultOutput_InsertsRecompressedSuffix(string input, string expected)
    {
        var actual = OutputPathResolver.Resolve(input, requestedOutput: null);

        Assert.Equal(Path.GetFullPath(expected), actual);
    }

    [Fact]
    public void Resolve_RequestedOutput_ReturnsNormalizedPath()
    {
        var actual = OutputPathResolver.Resolve("input.Map.Gbx", "chosen.Map.Gbx");

        Assert.Equal(Path.GetFullPath("chosen.Map.Gbx"), actual);
    }

    [Fact]
    public void EnsureWritable_SamePathIgnoringFileNameCase_Throws()
    {
        var input = Path.Combine(Path.GetTempPath(), "Same.Map.Gbx");
        var output = Path.Combine(Path.GetTempPath(), "same.map.gbx");

        var exception = Assert.Throws<GbxSizeTreeOutputException>(
            () => OutputPathResolver.EnsureWritable(input, output, force: true));

        Assert.Equal(4, exception.ExitCode);
    }

    [Fact]
    public void EnsureWritable_ExistingOutputWithoutForce_ThrowsWithForceHint()
    {
        var output = Path.GetTempFileName();
        try
        {
            var exception = Assert.Throws<GbxSizeTreeOutputException>(
                () => OutputPathResolver.EnsureWritable(output + ".input", output, force: false));

            Assert.Contains("--force", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public void EnsureWritable_ExistingOutputWithForce_Passes()
    {
        var output = Path.GetTempFileName();
        try
        {
            OutputPathResolver.EnsureWritable(output + ".input", output, force: true);
        }
        finally
        {
            File.Delete(output);
        }
    }
}
