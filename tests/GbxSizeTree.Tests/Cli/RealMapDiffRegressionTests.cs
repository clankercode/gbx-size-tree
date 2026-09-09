using System.Security.Cryptography;
using GbxSizeTree.Cli.Modes;
using GbxSizeTree.Measure;

namespace GbxSizeTree.Tests.Cli;

public sealed class RealMapDiffRegressionTests
{
    private const string OldName = "Sweet 2 burger v205.Map.gbx";
    private const string OldSha256 = "b2802168218f1e6bfc26b3834d7934084eacaccfd5e108857bf3a35dabe88870";
    private const string NewName = "Sweet 2 burger v206.Map.Gbx";
    private const string NewSha256 = "e784e1b1078ded693da232ae044b7effe2b60077e076ce241b08a3ebfcffec2e";

    [Fact]
    public void Sweet2BurgerV206_DefaultAllReverseAndSelfRemainConsistent()
    {
        var (oldPath, newPath) = Paths();
        var forward = DiffMode.CompareFiles(oldPath, newPath);
        var all = DiffMode.CompareFiles(oldPath, newPath, all: true);
        var reverse = DiffMode.CompareFiles(newPath, oldPath);
        var reverseAll = DiffMode.CompareFiles(newPath, oldPath, all: true);
        var self = DiffMode.CompareFiles(newPath, newPath);
        var selfAll = DiffMode.CompareFiles(newPath, newPath, all: true);

        Assert.Equal(3_334_387, forward.LeftBytes);
        Assert.Equal(7_099_215, forward.RightBytes);
        Assert.Empty(forward.BakedBlocks);
        Assert.Empty(forward.Chunks);
        Assert.NotEmpty(all.BakedBlocks);
        Assert.NotEmpty(all.Chunks);
        Assert.Contains(all.Chunks, change => change.Key == "content:decompressed-body");
        Assert.Contains(all.Chunks, change => change.Key == "content:stored-body");

        Assert.Equal(17, forward.EmbeddedPropertyChanges.Count);
        Assert.Contains(forward.EmbeddedPropertyChanges,
            entry => entry.Properties.LeftIssues.Concat(entry.Properties.RightIssues)
                .Any(issue => issue.Code is "partial-coverage" or "unsupported"));
        Assert.Contains(forward.MetadataChanges, change => change.Path.StartsWith("script.traits/", StringComparison.Ordinal));
        Assert.Empty(forward.Warnings);

        var contributionSides = forward.EmbeddedContributions
            .SelectMany(change => new[] { change.Left, change.Right })
            .OfType<EmbeddedFileContribution>()
            .ToArray();
        Assert.Equal(52, contributionSides.Length);
        Assert.All(contributionSides, contribution =>
        {
            Assert.NotNull(contribution.MarginalCompressedBodyBytes);
            Assert.Null(contribution.UnavailableReason);
        });
        Assert.DoesNotContain(contributionSides,
            contribution => contribution.UnavailableReason == "Removal trial budget exhausted.");

        AssertReverse(forward, reverse);
        AssertReverse(all, reverseAll);

        AssertNoChanges(self);
        AssertNoChanges(selfAll);
    }

    [Fact]
    public void Sweet2BurgerV206_AllFormatsKeepExpectedSectionsAndPlainHtmlUnstyled()
    {
        var (oldPath, newPath) = Paths();
        var report = DiffMode.CompareFiles(oldPath, newPath, all: true);
        var json = DiffMode.RenderJson(report);
        var markdown = DiffRenderer.RenderMarkdown(report, oldPath, newPath);
        var styled = DiffRenderer.RenderHtml(report, oldPath, newPath, styled: true);
        var plain = DiffRenderer.RenderHtml(report, oldPath, newPath, styled: false);

        Assert.Contains("\"EmbeddedPropertyChanges\"", json);
        Assert.Contains("\"MetadataChanges\"", json);
        Assert.Contains("### Embedded item properties — modified", markdown);
        Assert.Contains("### Map metadata", markdown);
        foreach (var html in new[] { styled, plain })
        {
            foreach (var id in new[]
            {
                "table-embedded-added-removed", "table-embedded-modified",
                "table-embedded-properties-modified", "table-embedded-contributions",
                "table-placed-items", "table-blocks", "table-baked-blocks",
                "table-map-metadata", "table-chunks",
            })
            {
                Assert.Contains($"id=\"{id}\"", html);
            }
            Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains("<style>", styled);
        Assert.DoesNotContain("<style", plain, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" style=", plain, StringComparison.OrdinalIgnoreCase);
    }

    private static (string Old, string New) Paths()
    {
        var root = Environment.GetEnvironmentVariable("GBX_SIZE_TREE_SB2");
        var oldPath = root is null ? null : Path.Combine(root, OldName);
        var newPath = root is null ? null : Path.Combine(root, NewName);
        Assert.SkipUnless(oldPath is not null && newPath is not null && File.Exists(oldPath) && File.Exists(newPath),
            "set GBX_SIZE_TREE_SB2 for Sweet 2 burger real-map diff regression tests");
        VerifyFixture("OLD", oldPath!, OldSha256);
        VerifyFixture("NEW", newPath!, NewSha256);
        return (oldPath!, newPath!);
    }

    private static void VerifyFixture(string label, string path, string expectedSha256)
    {
        using var stream = File.OpenRead(path);
        var actualSha256 = Convert.ToHexStringLower(SHA256.HashData(stream));
        Assert.True(string.Equals(expectedSha256, actualSha256, StringComparison.Ordinal),
            $"incorrect {label} fixture: {path}{Environment.NewLine}" +
            $"expected SHA-256: {expectedSha256}{Environment.NewLine}" +
            $"actual SHA-256:   {actualSha256}");
    }

    private static void AssertReverse(DiffReport forward, DiffReport reverse)
    {
        Assert.Equal(forward.LeftBytes, reverse.RightBytes);
        Assert.Equal(forward.RightBytes, reverse.LeftBytes);
        Assert.Equal(forward.Blocks.Select(Swap), reverse.Blocks);
        Assert.Equal(forward.BakedBlocks.Select(Swap), reverse.BakedBlocks);
        Assert.Equal(forward.Items.Select(Swap), reverse.Items);
        Assert.Equal(forward.Embedded.Select(Swap), reverse.Embedded);
        Assert.Equal(forward.Chunks.Select(Swap), reverse.Chunks);
        Assert.Equal(forward.MetadataChanges.Select(change => change with { Left = change.Right, Right = change.Left }), reverse.MetadataChanges);
        Assert.Equal(forward.EmbeddedContributions.Select(Swap), reverse.EmbeddedContributions);
        AssertDeepPropertyReverse(forward.EmbeddedPropertyChanges, reverse.EmbeddedPropertyChanges);
    }

    private static Change Swap(Change change) =>
        change with { Left = change.Right, Right = change.Left };

    private static ValueChange<T> Swap<T>(ValueChange<T> change) where T : class =>
        new(change.Right, change.Left);

    private static EmbeddedPropertyEntryDiff Swap(EmbeddedPropertyEntryDiff entry) => entry with
    {
        LeftSha256 = entry.RightSha256,
        RightSha256 = entry.LeftSha256,
        Properties = entry.Properties with
        {
            Changes = entry.Properties.Changes.Select(change => change with { Left = change.Right, Right = change.Left }).ToArray(),
            LeftIssues = entry.Properties.RightIssues,
            RightIssues = entry.Properties.LeftIssues,
        },
    };

    private static void AssertDeepPropertyReverse(
        IReadOnlyList<EmbeddedPropertyEntryDiff> forward,
        IReadOnlyList<EmbeddedPropertyEntryDiff> reverse)
    {
        Assert.Equal(forward.Count, reverse.Count);
        for (var i = 0; i < forward.Count; i++)
        {
            var expected = Swap(forward[i]);
            var actual = reverse[i];
            Assert.Equal(expected.Path, actual.Path);
            Assert.Equal(expected.LeftSha256, actual.LeftSha256);
            Assert.Equal(expected.RightSha256, actual.RightSha256);
            Assert.Equal(expected.Properties.ContentChanged, actual.Properties.ContentChanged);
            Assert.Equal(expected.Properties.Changes, actual.Properties.Changes);
            Assert.Equal(expected.Properties.LeftIssues, actual.Properties.LeftIssues);
            Assert.Equal(expected.Properties.RightIssues, actual.Properties.RightIssues);
        }
    }

    private static void AssertNoChanges(DiffReport report)
    {
        Assert.Empty(report.Blocks);
        Assert.Empty(report.BakedBlocks);
        Assert.Empty(report.Items);
        Assert.Empty(report.Embedded);
        Assert.Empty(report.Chunks);
        Assert.Empty(report.MetadataChanges);
        Assert.Empty(report.EmbeddedContributions);
        Assert.Empty(report.EmbeddedPropertyChanges);
        Assert.Empty(report.Warnings);
    }
}
