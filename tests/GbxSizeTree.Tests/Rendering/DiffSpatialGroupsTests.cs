using System.Diagnostics;
using GbxSizeTree.Cli.Modes;
using GbxSizeTree.Cli.Rendering;

namespace GbxSizeTree.Tests.Rendering;

public sealed class DiffSpatialGroupsTests(ITestOutputHelper output)
{
    private sealed record Point(string Key, SpatialPosition? Position);

    [Theory]
    [InlineData(32000)]
    [InlineData(64000)]
    public void DenseGrid_DistanceChecksStayLinear(int count)
    {
        var changes = Enumerable.Range(0, count).Select(i => Added(i,
            new(i % 40, i / 40 % 40, i / 1600))).ToArray();
        var timer = Stopwatch.StartNew();
        var result = DiffSpatialGroups.OrderWithDiagnostics(changes, p => p.Position, p => p.Key, out var checks).ToArray();
        timer.Stop();
        output.WriteLine($"Dense grid: {count} unique points, {timer.Elapsed.TotalMilliseconds:F3} ms, {checks} distance checks");
        Assert.Equal(count, result.Length);
        Assert.Single(result.Select(r => r.Group).Distinct());
        Assert.True(checks <= count * 8L, $"Expected linear distance checks, got {checks} for {count} points.");
        Assert.Equal(changes.OrderBy(c => c.Right!.Position).ThenBy(c => c.Right!.Key, StringComparer.Ordinal),
            result.Select(r => r.Change));
        Assert.Equal(result, Order(changes.Reverse().ToArray()));
    }

    [Theory]
    [InlineData(32000)]
    [InlineData(64000)]
    public void DenseLine_DistanceChecksStayLinear(int count)
    {
        var changes = Enumerable.Range(0, count).Select(i => Added(i, new(i / (double)count * 63, 0, 0))).ToArray();
        var result = DiffSpatialGroups.OrderWithDiagnostics(changes, p => p.Position, p => p.Key, out var checks).ToArray();
        Assert.Equal(count, result.Length);
        Assert.Single(result.Select(r => r.Group).Distinct());
        Assert.True(checks <= count * 8L, $"Expected linear distance checks, got {checks} for {count} points.");
        Assert.Equal(result, Order(changes.Reverse().ToArray()));
    }

    [Theory]
    [InlineData(8000, false, false)]
    [InlineData(8000, true, false)]
    [InlineData(8000, false, true)]
    [InlineData(8000, true, true)]
    [InlineData(32000, false, false)]
    [InlineData(32000, true, false)]
    [InlineData(32000, false, true)]
    [InlineData(32000, true, true)]
    [InlineData(64000, false, false)]
    [InlineData(64000, true, false)]
    [InlineData(64000, false, true)]
    [InlineData(64000, true, true)]
    public void DenseDisconnectedCells_PruneActualBoundsAndKeepBridges(int count, bool overlappingBounds, bool bridge)
    {
        var changes = Enumerable.Range(0, count).Select(i =>
        {
            var delta = i / (double)count * .01;
            var p = i >= count / 2 ? new SpatialPosition(63.9 + delta, 31.9, 31.9)
                : overlappingBounds ? i % 2 == 0 ? new(0, 31 + delta, 0) : new(0, 0, 31 + delta)
                : new(delta, 0, 0);
            return Added(i, p);
        }).ToArray();
        if (bridge) changes = [.. changes, Added(count, new(31, 31, 31))];
        var timer = Stopwatch.StartNew();
        var result = DiffSpatialGroups.OrderWithDiagnostics(changes, p => p.Position, p => p.Key, out var checks).ToArray();
        output.WriteLine($"Disconnected cells: {count}, overlapping bounds={overlappingBounds}, bridge={bridge}, "
            + $"{timer.Elapsed.TotalMilliseconds:F3} ms, {checks} distance checks");
        Assert.Equal(changes.Length, result.Length);
        Assert.Equal(bridge ? 1 : 2, result.Select(r => r.Group).Distinct().Count());
        Assert.True(checks <= count * 8L, $"Expected bounded search work, got {checks} for {count} points.");
        Assert.Equal(result, Order(changes.Reverse().ToArray()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-128)]
    public void AdjacentCells_KeepNonRepresentativeBridgeEvidence(int offset)
    {
        // The cell representatives are too far apart; only the later points bridge the cells.
        SpatialPosition[] positions = [new(offset, 0, 0), new(offset + 31, 31, 31),
            new(offset + 64, 32, 32), new(offset + 95, 63, 63), new(offset + 128, 64, 64),
            new(offset + 159, 95, 95), new(offset + 192, 96, 96), new(offset + 500, 0, 0)];
        var changes = positions.Select((p, i) => Added(i, p)).ToArray();
        AssertMatchesOracle(changes);
        Assert.Equal(2, Order(changes).Select(r => r.Group).Distinct().Count());
    }

    [Fact]
    public void AdjacentCellChain_LaterMembersConnectBothSides()
    {
        SpatialPosition[] positions = [new(0, 0, 0), new(31, 31, 31), new(32, 32, 63),
            new(63, 63, 63), new(64, 64, 95), new(95, 95, 95), new(96, 96, 127)];
        var changes = positions.Select((p, i) => Added(i, p)).ToArray();
        AssertMatchesOracle(changes);
        Assert.Single(Order(changes).Select(r => r.Group).Distinct());
        AssertMatchesOracle([Added(0, new(-64, 0, 0)), Added(1, new(0, 0, 0)),
            Added(2, new(31, 0, 0)), Added(3, new(95, 0, 0))]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-64)]
    public void LargerCells_MustNotMergeDisconnectedCorners(int offset)
    {
        var changes = new[] { Added(0, new(offset, offset, offset)),
            Added(1, new(offset + 63, offset + 63, offset + 63)) };
        AssertMatchesOracle(changes);
        Assert.Equal(2, Order(changes).Select(r => r.Group).Distinct().Count());
    }

    [Fact]
    public void RadiusBoundary_UsesAllThreeAxesAndTwoCellReach()
    {
        foreach (var end in new[] { new SpatialPosition(64, 0, 0), new(64.0001, 0, 0),
            new(0, 64, 0), new(0, 0, 64), new(40, 40, 40), new(-64, 0, 0) })
            AssertMatchesOracle([Added(0, new(0, 0, 0)), Added(1, end)]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(.000001)]
    public void HierarchicalSearch_KeepsExactRadiusBoundary(double beyond)
    {
        var changes = Enumerable.Range(0, 80).Select(i => Added(i, i < 40
            ? new(0, i / 100.0, 0) : new(64 + beyond, 31 + i / 1000.0, 31))).ToArray();
        changes = [.. changes, Added(80, new(0, 31.08, 31)), Added(81, new(64 + beyond, 31.08, 31))];
        AssertMatchesOracle(changes);
        Assert.Equal(beyond == 0 ? 1 : 2, Order(changes).Select(r => r.Group).Distinct().Count());
    }

    [Fact]
    public void DenseSeededCells_MatchAllPairsAfterHierarchicalSplits()
    {
        var random = new Random(56057);
        for (var trial = 0; trial < 20; trial++)
        {
            var changes = Enumerable.Range(0, 100).Select(i => Added(i, new(
                (i < 50 ? 0 : 63) + random.NextDouble(),
                random.NextDouble() * 32, random.NextDouble() * 32))).ToArray();
            AssertMatchesOracle(changes);
        }
    }

    [Fact]
    public void MixedPositions_PreserveMortonOrderCoincidentTiesAndMissingValues()
    {
        var tied = new Point("tie", new(-1, 0, 0));
        ValueChange<Point>[] changes = [new(null, tied), new(tied, null),
            Added(0, new(-1, 0, 0)), Added(1, new(1, 0, 0)), Added(2, new(-.5, 0, 1000)),
            Added(3, new(64, 0, 0)), Added(4, null), Added(5, null),
            Added(6, new(double.NaN, 0, 0)), Added(7, new(double.NaN, 0, 0)),
            Added(8, new(double.PositiveInfinity, 0, 0)), Added(9, new(double.PositiveInfinity, 0, 0)),
            new(new("old", new(5000, 0, 0)), new("new", new(2, 0, 0)))];
        AssertMatchesOracle(changes);
    }

    [Fact]
    public void SeededClouds_MatchAllPairsComponentsAndStableOrder()
    {
        var random = new Random(56056);
        for (var trial = 0; trial < 50; trial++)
        {
            var changes = Enumerable.Range(0, 100).Select(i => Added(i, new(
                random.Next(-6, 7) * 32 + random.NextDouble() * 32,
                random.Next(-6, 7) * 32 + random.NextDouble() * 32,
                random.Next(-6, 7) * 32 + random.NextDouble() * 32))).ToArray();
            AssertMatchesOracle(changes);
        }
        Assert.Empty(Order([]));
    }

    private static ValueChange<Point> Added(int i, SpatialPosition? position) => new(null, new($"point{i:D6}", position));

    private static (ValueChange<Point> Change, int Group)[] Order(ValueChange<Point>[] changes) =>
        DiffSpatialGroups.Order(changes, p => p.Position, p => p.Key).ToArray();

    private static void AssertMatchesOracle(ValueChange<Point>[] changes)
    {
        var ordered = changes.OrderBy(c => (c.Right ?? c.Left)!.Position)
            .ThenBy(c => (c.Right ?? c.Left)!.Key, StringComparer.Ordinal)
            .ThenBy(c => c.Left is null ? 1 : 0).ToArray();
        var groups = Enumerable.Range(0, ordered.Length).ToArray();
        for (var i = 0; i < ordered.Length; i++)
        for (var j = 0; j < i; j++)
        {
            var p = (ordered[i].Right ?? ordered[i].Left)!.Position;
            var q = (ordered[j].Right ?? ordered[j].Left)!.Position;
            var connected = p == q;
            if (p is { } a && q is { } b)
            {
                var dx = a.X - b.X;
                var dy = a.Y - b.Y;
                var dz = a.Z - b.Z;
                connected |= dx * dx + dy * dy + dz * dz <= 64 * 64;
            }
            if (!connected) continue;
            var from = Math.Max(groups[i], groups[j]);
            var to = Math.Min(groups[i], groups[j]);
            for (var k = 0; k < groups.Length; k++)
                if (groups[k] == from) groups[k] = to;
        }
        var expected = Enumerable.Range(0, ordered.Length).OrderBy(i => groups[i])
            .Select(i => (ordered[i], groups[i])).ToArray();
        Assert.Equal(expected, Order(changes));
        Assert.Equal(expected, Order(changes.Reverse().ToArray()));
    }
}
