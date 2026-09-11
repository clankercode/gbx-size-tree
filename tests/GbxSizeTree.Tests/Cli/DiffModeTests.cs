using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using GbxSizeTree.Cli.Modes;

namespace GbxSizeTree.Tests.Cli;

public sealed class DiffModeTests
{
    [Fact]
    public void CompareMaps_PreservesDuplicatePlacements()
    {
        var block = new CGameCtnBlock { Name = "duplicate", Coord = new(1, 2, 3) };
        var item = new CGameCtnAnchoredObject { AbsolutePositionInMap = new(1, 2, 3) };
        var report = DiffMode.CompareMaps(new() { Blocks = [block, block], AnchoredObjects = [item] },
            new() { Blocks = [block], AnchoredObjects = [item, item] });
        Assert.NotNull(Assert.Single(report.Blocks).Left);
        Assert.NotNull(Assert.Single(report.Items).Right);
    }

    [Fact]
    public void CompareMaps_DetectsColorScaleAndFreeTransformChanges()
    {
        var left = new CGameCtnChallenge
        {
            Blocks = [new() { Name = "free", IsFree = true, AbsolutePositionInMap = new(1, 2, 3) }],
            AnchoredObjects = [new() { Scale = 1, Color = (DifficultyColor)0 }],
        };
        var right = new CGameCtnChallenge
        {
            Blocks = [new() { Name = "free", IsFree = true, AbsolutePositionInMap = new(1.5f, 2, 3) }],
            AnchoredObjects = [new() { Scale = 2, Color = (DifficultyColor)1 }],
        };
        var report = DiffMode.CompareMaps(left, right);
        Assert.Equal(2, report.Blocks.Count);
        Assert.Equal(2, report.Items.Count);
        Assert.Equal(1.5, report.Blocks.Single(c => c.Right is not null).Right!.PhysicalPosition!.Value.X);
    }

    [Fact]
    public void CompareMaps_BakedBlocksRequireAllAndUseMidpoints()
    {
        var right = new CGameCtnChallenge { BakedBlocks = [new() { Name = "baked", Coord = new(1, 2, 3) }] };
        Assert.Empty(DiffMode.CompareMaps(new(), right).BakedBlocks);
        var report = DiffMode.CompareMaps(new(), right, all: true);
        Assert.Equal(new SpatialPosition(48, 20, 112), Assert.Single(report.BakedBlocks).Right!.PhysicalPosition);
        Assert.Single(report.RightBakedSnapshots);
    }

    [Fact]
    public void CompareMaps_EmbeddedContentChangesUseTypedDataEvenWithSameSizes()
    {
        var left = MapWithEmbed("folder/asset.Item.Gbx", "aaaa");
        var right = MapWithEmbed("folder/asset.Item.Gbx", "bbbb");
        var change = Assert.Single(DiffMode.CompareMaps(left, right).Embedded);
        Assert.NotNull(change.Left);
        Assert.NotNull(change.Right);
        Assert.Equal(change.Left.Uncompressed, change.Right.Uncompressed);
        Assert.Equal(change.Left.Compressed, change.Right.Compressed);
        Assert.NotEqual(change.Left.Sha256, change.Right.Sha256);
        Assert.Equal("folder/asset.Item.Gbx", change.Right.Path);
    }

    [Fact]
    public void CompareMaps_CompressedSizeOnlyDeltaIsNotModified()
    {
        var content = new byte[4096];
        var left = MapWithEmbeddedBytes(CompressionLevel.NoCompression, ("asset.bin", content));
        var right = MapWithEmbeddedBytes(CompressionLevel.SmallestSize, ("asset.bin", content));

        var report = DiffMode.CompareMaps(left, right);

        var leftSnapshot = Assert.Single(report.LeftEmbeddedSnapshots);
        var rightSnapshot = Assert.Single(report.RightEmbeddedSnapshots);
        Assert.NotEqual(leftSnapshot.Compressed, rightSnapshot.Compressed);
        Assert.Equal(leftSnapshot.Sha256, rightSnapshot.Sha256);
        Assert.Empty(report.Embedded);
        Assert.Empty(report.EmbeddedPropertyChanges);
    }

    [Fact]
    public void CompareMaps_CaseOnlyEmbedRenamePreservesDistinctPaths()
    {
        var report = DiffMode.CompareMaps(MapWithEmbed("asset.Item.Gbx", "data"), MapWithEmbed("Asset.Item.Gbx", "data"));
        Assert.Equal(2, report.Embedded.Count);
        Assert.Equal("asset.Item.Gbx", report.Embedded.Single(c => c.Left is not null).Left!.Path);
        Assert.Equal("Asset.Item.Gbx", report.Embedded.Single(c => c.Right is not null).Right!.Path);
    }

    [Fact]
    public void CompareMaps_CaseDistinctZipEntriesAreNotOverwritten()
    {
        var left = MapWithEmbeds(("asset.Item.Gbx", "old"), ("Asset.Item.Gbx", "keep"));
        var right = MapWithEmbeds(("asset.Item.Gbx", "new"), ("Asset.Item.Gbx", "keep"));
        var report = DiffMode.CompareMaps(left, right);
        Assert.Equal(2, report.LeftEmbeddedSnapshots.Count);
        Assert.Equal("asset.Item.Gbx", Assert.Single(report.Embedded).Left!.Path);
        Assert.NotEqual(report.Embedded[0].Left!.Sha256, report.Embedded[0].Right!.Sha256);
    }

    [Fact]
    public void CompareMaps_RejectsAmbiguousDuplicateZipPaths()
    {
        var map = MapWithEmbeds(("asset.Item.Gbx", "first"), ("asset.Item.Gbx", "second"));
        Assert.Throws<InvalidDataException>(() => DiffMode.CompareMaps(new(), map));
    }

    [Fact]
    public void Json_PreservesEnvelopeStringChangesAndStructuredSnapshots()
    {
        var left = MapWithEmbed("asset.Item.Gbx", "aaaa");
        var right = MapWithEmbed("asset.Item.Gbx", "bbbb");
        right.Blocks = [new() { Name = "new", Coord = new(1, 2, 3) }];
        using var json = JsonDocument.Parse(DiffMode.RenderJson(DiffMode.CompareMaps(left, right)));
        var root = json.RootElement;
        Assert.Equal(JsonValueKind.String, root.GetProperty("Embedded")[0].GetProperty("Left").ValueKind);
        Assert.Equal("asset.Item.Gbx", root.GetProperty("Embedded")[0].GetProperty("Key").GetString());
        var typed = root.GetProperty("EmbeddedChanges")[0];
        Assert.Equal("asset.Item.Gbx", typed.GetProperty("Left").GetProperty("Path").GetString());
        Assert.Equal(4, typed.GetProperty("Right").GetProperty("Uncompressed").GetInt64());
        Assert.Equal(1, typed.GetProperty("Right").GetProperty("Ratio").GetDouble());
        Assert.NotEqual(typed.GetProperty("Left").GetProperty("Sha256").GetString(), typed.GetProperty("Right").GetProperty("Sha256").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("Blocks")[0].GetProperty("Left").ValueKind);
        Assert.Contains("new|coord=", root.GetProperty("Blocks")[0].GetProperty("Right").GetString());
        Assert.Equal(48, root.GetProperty("RightBlockSnapshots")[0].GetProperty("PhysicalPosition").GetProperty("X").GetDouble());
        Assert.NotEmpty(root.GetProperty("LeftEmbeddedSnapshots")[0].GetProperty("Sha256").GetString()!);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("Password").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("LeftBakedSnapshots").ValueKind);
    }

    [Fact]
    public void SpatialOrdering_IsSignedFractionalLargeAndCultureIndependent()
    {
        var positions = new[] { new SpatialPosition(2048, 0, 0), new(-.125, 0, 0), new(.25, 0, 0), new(.125, 0, 0), new(1e20, 0, 0) };
        Assert.Equal(new[] { -.125, .125, .25, 2048, 1e20 }, positions.Order().Select(p => p.X));
        Assert.True(new SpatialPosition(0, 4, 1).CompareTo(new(0, 0, 2)) > 0);
        Assert.True(new SpatialPosition(0, 1, 0).CompareTo(new(0, 2, 0)) < 0);
        Assert.Equal(((double)int.MaxValue + .5) * 32, SpatialPosition.Midpoint(new(int.MaxValue, 0, 0)).X);
        var culture = CultureInfo.CurrentCulture;
        try
        {
            var commaCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            commaCulture.NumberFormat.NumberDecimalSeparator = ",";
            CultureInfo.CurrentCulture = commaCulture;
            Assert.Equal("(0.125, -0.5, 1E+20)", new SpatialPosition(.125, -.5, 1e20).ToString());
            Assert.Contains("ratio=0.5", new EmbeddedSnapshot("path", "hash", 1, 2).ToValue());
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [Fact]
    public void CompareMaps_SortsMixedChangesAndJsonIndependentlyOfInputOrder()
    {
        var near = new CGameCtnBlock { Name = "near", Coord = new(1, 0, 0) };
        var far = new CGameCtnBlock { Name = "far", Coord = new(3, 0, 0) };
        var middle = new CGameCtnBlock { Name = "middle", IsFree = true, AbsolutePositionInMap = new(80, 4, 16) };
        var report = DiffMode.CompareMaps(new() { Blocks = [far, near] }, new() { Blocks = [middle] });
        Assert.Equal(new[] { "near", "middle", "far" }, report.Blocks.Select(c => (c.Right ?? c.Left)!.Name));
        Assert.Equal(DiffMode.RenderJson(report), DiffMode.RenderJson(
            DiffMode.CompareMaps(new() { Blocks = [near, far] }, new() { Blocks = [middle] })));
    }

    [Theory]
    [InlineData("color")]
    [InlineData("scale")]
    [InlineData("rotation")]
    [InlineData("pivot")]
    [InlineData("animation")]
    [InlineData("lightmap")]
    [InlineData("flags")]
    public void CompareMaps_DetectsIndividualItemProperties(string property)
    {
        var item = new CGameCtnAnchoredObject();
        switch (property)
        {
            case "color": item.Color = (DifficultyColor)1; break;
            case "scale": item.Scale = 2; break;
            case "rotation": item.YawPitchRoll = new(.25f, 0, 0); break;
            case "pivot": item.PivotPosition = new(0, 1, 0); break;
            case "animation": item.AnimPhaseOffset = (CGameCtnAnchoredObject.EPhaseOffset)1; break;
            case "lightmap": item.LightmapQuality = (LightmapQuality)1; break;
            case "flags": item.Flags = 1; break;
        }
        var report = DiffMode.CompareMaps(new() { AnchoredObjects = [new()] }, new() { AnchoredObjects = [item] });
        Assert.Equal(2, report.Items.Count);
    }

    [Fact]
    public void CompareMaps_ScaleOnlyEditsRemainDistinctRawSnapshotsWithoutScaleInKey()
    {
        var left = new CGameCtnChallenge { AnchoredObjects = [new() { Scale = 1 }, new() { Scale = 1 }] };
        var right = new CGameCtnChallenge { AnchoredObjects = [new() { Scale = 1 }, new() { Scale = 2 }] };
        var report = DiffMode.CompareMaps(left, right);
        Assert.Equal(2, report.Items.Count);
        var removed = Assert.Single(report.Items, c => c.Right is null).Left!;
        var added = Assert.Single(report.Items, c => c.Left is null).Right!;
        Assert.Equal(1, removed.Scale);
        Assert.Equal(2, added.Scale);
        Assert.NotEqual(removed, added);
        Assert.Equal(removed.Key, added.Key);
        Assert.DoesNotContain("|scale=", removed.Key);
        Assert.Equal(new float[] { 1, 1 }, report.LeftItemSnapshots.Select(x => x.Scale));
        Assert.Equal(new float[] { 1, 2 }, report.RightItemSnapshots.Select(x => x.Scale).Order());
        foreach (var output in new[] { DiffRenderer.RenderHtml(report, "a", "b"), DiffRenderer.RenderMarkdown(report, "a", "b") })
        {
            Assert.DoesNotContain("Scale", output);
            Assert.DoesNotContain("No differences", output);
            Assert.Contains("1 added", output);
            Assert.Contains("1 removed", output);
        }
    }

    [Fact]
    public void JsonAndSpatialOrdering_HandleNonFiniteAndMissingPositions()
    {
        var report = DiffMode.CompareMaps(new(), new()
        {
            Blocks = [new() { Name = "missing", IsFree = true }],
            AnchoredObjects = [new() { AbsolutePositionInMap = new(float.NaN, float.PositiveInfinity, float.NegativeInfinity) }],
        });
        using var json = JsonDocument.Parse(DiffMode.RenderJson(report));
        Assert.Equal("NaN", json.RootElement.GetProperty("RightItemSnapshots")[0].GetProperty("PhysicalPosition").GetProperty("X").GetString());
        Assert.Null(Assert.Single(report.Blocks).Right!.PhysicalPosition);
        Assert.True(new SpatialPosition(double.NaN, 0, 0).CompareTo(new(0, 0, 0)) < 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompareFiles_ThrowingObserverPreservesJsonResult(bool all)
    {
        var leftPath = Path.GetTempFileName();
        var rightPath = Path.GetTempFileName();
        try
        {
            SaveEmbeddedMap(MapWithEmbeds(("asset.bin", "old")), leftPath);
            SaveEmbeddedMap(MapWithEmbeds(("asset.bin", "new")), rightPath);
            var expected = DiffMode.RenderJson(DiffMode.CompareFiles(leftPath, rightPath, all));
            var calls = 0;

            var actual = DiffMode.CompareFiles(leftPath, rightPath, all,
                progress: _ => { calls++; throw new InvalidOperationException("observer failed"); });

            Assert.Equal(1, calls);
            Assert.Equal(expected, DiffMode.RenderJson(actual));
        }
        finally { File.Delete(leftPath); File.Delete(rightPath); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompareFiles_MeasuresChangedEntriesFromOriginalBytes(bool all)
    {
        var left = MapWithEmbeds(("changed.bin", "aaaa"), ("removed.bin", "old"), ("keep.bin", "keep"));
        var right = MapWithEmbeds(("changed.bin", "bbbb"), ("added.bin", "new"), ("keep.bin", "keep"));
        var leftPath = Path.GetTempFileName();
        var rightPath = Path.GetTempFileName();
        try
        {
            SaveEmbeddedMap(left, leftPath);
            SaveEmbeddedMap(right, rightPath);
            var original = File.ReadAllBytes(leftPath);
            var report = DiffMode.CompareFiles(leftPath, rightPath, all);
            Assert.Equal(new[] { "added.bin", "changed.bin", "removed.bin" },
                report.EmbeddedContributions.Select(c => (c.Right ?? c.Left)!.Path));
            var expected = new GbxSizeTree.Measure.EmbeddedFileContributionMeasurer().Measure(original,
                ["changed.bin", "removed.bin"], cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(expected.BaselineCompressedBodyBytes);
            Assert.Equal(expected.BaselineCompressedBodyBytes, report.LeftContributionBaselineBytes);
            Assert.Equal(expected.Entries, report.EmbeddedContributions.Where(c => c.Left is not null).Select(c => c.Left!));
            Assert.All(report.EmbeddedContributions, c => Assert.Null((c.Right ?? c.Left)!.UnavailableReason));
            Assert.Equal(original, File.ReadAllBytes(leftPath));
            Assert.Empty(DiffMode.CompareFiles(leftPath, leftPath).EmbeddedContributions);
            using var json = JsonDocument.Parse(DiffMode.RenderJson(report));
            Assert.Equal(JsonValueKind.Number, json.RootElement.GetProperty("EmbeddedContributions")[0]
                .GetProperty("Right").GetProperty("MarginalCompressedBodyBytes").ValueKind);
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("EmbeddedContributions")[0].GetProperty("Left").ValueKind);
            var budget = DiffMode.CompareFiles(leftPath, rightPath, contributionOptions: new() { MaxTrials = 0 });
            Assert.All(budget.EmbeddedContributions, c =>
            {
                Assert.Null((c.Right ?? c.Left)!.MarginalCompressedBodyBytes);
                Assert.Contains("budget", (c.Right ?? c.Left)!.UnavailableReason!);
            });
            using var plain = new MemoryStream();
            using (var compressed = new MemoryStream(original)) Gbx.Decompress(compressed, plain);
            File.WriteAllBytes(leftPath, plain.ToArray());
            var uncompressed = DiffMode.CompareFiles(leftPath, rightPath);
            Assert.All(uncompressed.EmbeddedContributions.Where(c => c.Left is not null), c =>
            {
                Assert.Null(c.Left!.MarginalCompressedBodyBytes);
                Assert.Contains("uncompressed", c.Left.UnavailableReason!);
            });
        }
        finally { File.Delete(leftPath); File.Delete(rightPath); }
    }

    [Fact]
    public void CompareMaps_LabelsMissingOriginalContainerRatherThanInventingContributions()
    {
        var report = DiffMode.CompareMaps(MapWithEmbed("asset.bin", "a"), MapWithEmbed("asset.bin", "b"));
        var change = Assert.Single(report.EmbeddedContributions);
        Assert.Null(change.Left!.MarginalCompressedBodyBytes);
        Assert.Null(change.Right!.MarginalCompressedBodyBytes);
        Assert.Contains("original container", change.Left.UnavailableReason!);
        Assert.Equal(1, change.Right.ZipRawBytes);
    }

    [Fact]
    public void CompareFiles_DefaultBudgetMeasuresTenAndExplicitLowerCapIsPreserved()
    {
        var leftPath = Path.GetTempFileName();
        var rightPath = Path.GetTempFileName();
        try
        {
            SaveEmbeddedMap(MapWithEmbeds(), leftPath);
            SaveEmbeddedMap(MapWithEmbeds(Enumerable.Range(0, 10).Reverse()
                .Select(i => ($"asset{i:D2}.bin", $"content{i}")).ToArray()), rightPath);
            var report = DiffMode.CompareFiles(leftPath, rightPath);
            Assert.Equal(10, report.EmbeddedContributions.Count);
            Assert.All(report.EmbeddedContributions, c =>
            {
                Assert.NotNull(c.Right!.MarginalCompressedBodyBytes);
                Assert.Null(c.Right.UnavailableReason);
                Assert.True(c.Right.ZipRawBytes > 0);
                Assert.True(c.Right.ZipCompressedBytes > 0);
            });
            Assert.Contains("Default: at most 256 removal trials per map", DiffRenderer.RenderHtml(report, leftPath, rightPath));

            var capped = DiffMode.CompareFiles(leftPath, rightPath,
                contributionOptions: new() { MaxTrials = 8 });
            Assert.All(capped.EmbeddedContributions.Take(8), c => Assert.NotNull(c.Right!.MarginalCompressedBodyBytes));
            Assert.All(capped.EmbeddedContributions.Skip(8), c =>
            {
                Assert.Null(c.Right!.MarginalCompressedBodyBytes);
                Assert.Contains("budget", c.Right.UnavailableReason!);
                Assert.True(c.Right.ZipRawBytes > 0);
                Assert.True(c.Right.ZipCompressedBytes > 0);
            });
        }
        finally { File.Delete(leftPath); File.Delete(rightPath); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Run_HtmlForwardsStyleOption(bool styled)
    {
        GbxSizeTree.Tests.Fixtures.SampleMap.SkipUnlessAvailable();
        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        foreach (var arg in new[] { typeof(DiffMode).Assembly.Location, "diff",
            GbxSizeTree.Tests.Fixtures.SampleMap.Path, GbxSizeTree.Tests.Fixtures.SampleMap.Path,
            "--html", styled ? "--styled" : "--not-styled" })
            start.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.True(process.ExitCode == 0, await stderr);
        var html = await stdout;
        Assert.Equal(styled, html.Contains("<style>", StringComparison.Ordinal));
        if (!styled) Assert.DoesNotContain(" style=", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Run_JsonStreamsSameDocumentAsRenderJson()
    {
        var left = Path.GetTempFileName();
        var right = Path.GetTempFileName();
        try
        {
            SaveEmbeddedMap(MapWithEmbeds(("asset.bin", "same")), left);
            SaveEmbeddedMap(MapWithEmbeds(("asset.bin", "same")), right);
            var start = new System.Diagnostics.ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            };
            foreach (var arg in new[] { typeof(DiffMode).Assembly.Location, "diff", "--json", left, right })
                start.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.True(process.ExitCode == 0, await stderr);
            Assert.Equal(DiffMode.RenderJson(DiffMode.CompareFiles(left, right)) + Environment.NewLine, await stdout);
        }
        finally { File.Delete(left); File.Delete(right); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Run_HtmlForwardsExplicitColorOverride(bool color)
    {
        GbxSizeTree.Tests.Fixtures.SampleMap.SkipUnlessAvailable();
        Gbx.LZO = new GBX.NET.LZO.Lzo();
        Gbx.ZLib = new GBX.NET.ZLib.ZLib();
        var map = Gbx.Parse<CGameCtnChallenge>(GbxSizeTree.Tests.Fixtures.SampleMap.Path);
        var item = Assert.IsType<CGameCtnAnchoredObject>(map.Node.AnchoredObjects!.First());
        item.Color = item.Color == DifficultyColor.Blue ? DifficultyColor.Red : DifficultyColor.Blue;
        var changed = Path.GetTempFileName();
        try
        {
            map.Save(changed);
            var start = new System.Diagnostics.ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            };
            foreach (var arg in new[] { typeof(DiffMode).Assembly.Location, "diff",
                GbxSizeTree.Tests.Fixtures.SampleMap.Path, changed, "--html", color ? "--color" : "--no-color" })
                start.ArgumentList.Add(arg);
            start.Environment["NO_COLOR"] = color ? "1" : "";
            using var process = System.Diagnostics.Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.True(process.ExitCode == 0, await stderr);
            var html = await stdout;
            Assert.Equal(color, html.Contains("class=\"color-chip\"", StringComparison.Ordinal));
        }
        finally { File.Delete(changed); }
    }

    [Fact]
    public void CompareMaps_DeepDiffsSameLengthModifiedItemPropertiesAtOriginalFullPath()
    {
        const string path = "Embedded/items/Folder/asset.Item.Gbx";
        var left = MapWithEmbeddedBytes((path, ItemBytes("Grass")), ("unchanged.Item.Gbx", [1, 2, 3]));
        var right = MapWithEmbeddedBytes((path, ItemBytes("Stone")), ("unchanged.Item.Gbx", [1, 2, 3]));

        var report = DiffMode.CompareMaps(left, right);

        var entry = Assert.Single(report.EmbeddedPropertyChanges);
        Assert.Equal(path, entry.Path);
        Assert.True(entry.Properties.ContentChanged);
        var embedded = Assert.Single(report.Embedded, value => value.Left?.Path == path && value.Right?.Path == path);
        Assert.Equal(embedded.Left!.Uncompressed, embedded.Right!.Uncompressed);
        var change = Assert.Single(entry.Properties.Changes,
            value => value.Path.EndsWith("Material > Name", StringComparison.Ordinal));
        Assert.Equal("Grass", change.Left!.Text);
        Assert.Equal("Stone", change.Right!.Text);
        Assert.DoesNotContain(report.EmbeddedPropertyChanges, value => value.Path == "unchanged.Item.Gbx");

        using var json = JsonDocument.Parse(DiffMode.RenderJson(report));
        var jsonEntry = json.RootElement.GetProperty("EmbeddedPropertyChanges")[0];
        Assert.Equal(path, jsonEntry.GetProperty("Path").GetString());
        var jsonChange = jsonEntry.GetProperty("Properties").GetProperty("Changes")
            .EnumerateArray().Single(value => value.GetProperty("Path").GetString()!.EndsWith("Material > Name", StringComparison.Ordinal));
        Assert.Equal("Grass", jsonChange.GetProperty("Left").GetProperty("Text").GetString());
        Assert.Equal("Stone", jsonChange.GetProperty("Right").GetProperty("Text").GetString());
    }

    [Fact]
    public void CompareMaps_DeepDiffFallbackRetainsChangedHashAndNeverImpliesEquality()
    {
        var left = MapWithEmbeddedBytes(("broken.Item.Gbx", [.. "GBX"u8, 1, 2, 3]));
        var right = MapWithEmbeddedBytes(("broken.Item.Gbx", [.. "GBX"u8, 1, 2, 4]));

        var report = DiffMode.CompareMaps(left, right);
        var entry = Assert.Single(report.EmbeddedPropertyChanges);

        Assert.NotEqual(entry.LeftSha256, entry.RightSha256);
        Assert.True(entry.Properties.ContentChanged);
        Assert.Empty(entry.Properties.Changes);
        Assert.Contains(entry.Properties.LeftIssues, issue => issue.Code == "parse");
        foreach (var output in new[]
        {
            DiffRenderer.RenderMarkdown(report, "left", "right"),
            DiffRenderer.RenderHtml(report, "left", "right", styled: false),
        })
        {
            Assert.Contains("changed (SHA-256)", output.Replace("\\", "", StringComparison.Ordinal));
            Assert.Contains("No supported property difference was available", output);
            Assert.DoesNotContain("No differences in the compared fields", output);
        }
    }

    private static byte[] ItemBytes(string materialName)
    {
        var material = new CPlugMaterialUserInst { MaterialName = materialName };
        material.Chunks.Create<CPlugMaterialUserInst.Chunk090FD000>();
        using var stream = new MemoryStream();
        new Gbx<CPlugMaterialUserInst>(material).Save(stream);
        return stream.ToArray();
    }

    private static void SaveEmbeddedMap(CGameCtnChallenge map, string path)
    {
        Gbx.LZO = new GBX.NET.LZO.Lzo();
        map.Chunks.Create<CGameCtnChallenge.Chunk03043054>().Version = 1;
        new Gbx<CGameCtnChallenge>(map).Save(path);
    }

    private static CGameCtnChallenge MapWithEmbed(string path, string content) => MapWithEmbeds((path, content));

    private static CGameCtnChallenge MapWithEmbeddedBytes(params (string Path, byte[] Content)[] entries) =>
        MapWithEmbeddedBytes(CompressionLevel.NoCompression, entries);

    private static CGameCtnChallenge MapWithEmbeddedBytes(CompressionLevel level, params (string Path, byte[] Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                using var output = zip.CreateEntry(path, level).Open();
                output.Write(content);
            }
        }
        return new() { EmbeddedZipData = stream.ToArray() };
    }

    private static CGameCtnChallenge MapWithEmbeds(params (string Path, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path, CompressionLevel.NoCompression).Open());
                writer.Write(content);
            }
        }
        return new() { EmbeddedZipData = stream.ToArray() };
    }
}
