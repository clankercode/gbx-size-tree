using System.Runtime.CompilerServices;
using GbxSizeTree.Cli.Modes;

[assembly: InternalsVisibleTo("GbxSizeTree.Tests")]

namespace GbxSizeTree.Cli.Rendering;

internal static class DiffSpatialGroups
{
    // Two horizontal grid cells; connected neighbours form a region, including across Morton seams.
    private const double Radius = 64;
    // A 32-unit cube has diameter < 64, so every point in a cell is connected.
    private const double CellSize = 32;

    public static IEnumerable<(ValueChange<T> Change, int Group)> Order<T>(IReadOnlyList<ValueChange<T>> changes,
        Func<T, SpatialPosition?> position, Func<T, string> key) where T : class => OrderWithDiagnostics(changes, position, key, out _);

    // Counts point-distance and AABB-distance tests, including rejected search nodes.
    internal static IEnumerable<(ValueChange<T> Change, int Group)> OrderWithDiagnostics<T>(IReadOnlyList<ValueChange<T>> changes,
        Func<T, SpatialPosition?> position, Func<T, string> key, out long distanceChecks) where T : class
    {
        long checks = 0;
        var ordered = changes.OrderBy(c => position((c.Right ?? c.Left)!))
            .ThenBy(c => key((c.Right ?? c.Left)!), StringComparer.Ordinal)
            .ThenBy(c => c.Left is null ? 1 : 0).ToArray();
        var positions = ordered.Select(c => position((c.Right ?? c.Left)!)).ToArray();
        var parents = Enumerable.Range(0, ordered.Length).ToArray();
        var ties = new Dictionary<SpatialPosition, int>();
        var buckets = new Dictionary<SpatialPosition, List<int>>();
        int? missing = null;
        for (var i = 0; i < ordered.Length; i++)
        {
            if (positions[i] is not { } p)
            {
                if (missing is { } previous) Join(i, previous);
                else missing = i;
                continue;
            }
            if (ties.TryGetValue(p, out var same))
            {
                Join(i, same);
                continue;
            }
            ties.Add(p, i);
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z)) continue;
            var bucket = new SpatialPosition(Math.Floor(p.X / CellSize), Math.Floor(p.Y / CellSize), Math.Floor(p.Z / CellSize));
            if (!buckets.TryGetValue(bucket, out var entries)) buckets.Add(bucket, entries = []);
            else Join(i, entries[0]);
            entries.Add(i);
        }
        // Keep all unique positions: a non-representative point may be the only cross-cell bridge.
        var trees = buckets.ToDictionary(b => b.Key, b => new SearchNode(b.Value.ToArray(), 0, b.Value.Count, positions));
        foreach (var (bucket, entries) in trees)
            for (var x = -2; x <= 2; x++)
                for (var y = -2; y <= 2; y++)
                    for (var z = -2; z <= 2; z++)
                        if (trees.TryGetValue(new(bucket.X + x, bucket.Y + y, bucket.Z + z), out var neighbours)
                            && entries.Representative < neighbours.Representative
                            && Root(entries.Representative) != Root(neighbours.Representative)
                            && WithinRadius(entries, neighbours))
                            Join(entries.Representative, neighbours.Representative);
        distanceChecks = checks;
        return Enumerable.Range(0, ordered.Length).GroupBy(Root).OrderBy(g => g.Key)
            .SelectMany(g => g.Select(i => (ordered[i], g.Key)));

        bool WithinRadius(SearchNode a, SearchNode b)
        {
            checks++;
            var dx = Math.Max(0, Math.Max(a.Min.X - b.Max.X, b.Min.X - a.Max.X));
            var dy = Math.Max(0, Math.Max(a.Min.Y - b.Max.Y, b.Min.Y - a.Max.Y));
            var dz = Math.Max(0, Math.Max(a.Min.Z - b.Max.Z, b.Min.Z - a.Max.Z));
            if (dx * dx + dy * dy + dz * dz > Radius * Radius) return false;
            if (Close(a.Representative, b.Representative)) return true;
            if (a.Count <= SearchNode.LeafSize && b.Count <= SearchNode.LeafSize)
            {
                for (var i = a.Start; i < a.Start + a.Count; i++)
                    for (var j = b.Start; j < b.Start + b.Count; j++)
                        if (Close(a.Indices[i], b.Indices[j])) return true;
                return false;
            }
            // Split by count to bound tree depth; prune actual point bounds at every level.
            if (a.Count >= b.Count)
            {
                a.Split(positions);
                return WithinRadius(a.Left!, b) || WithinRadius(a.Right!, b);
            }
            b.Split(positions);
            return WithinRadius(a, b.Left!) || WithinRadius(a, b.Right!);
        }

        bool Close(int i, int j)
        {
            checks++;
            var p = positions[i]!.Value;
            var q = positions[j]!.Value;
            var dx = p.X - q.X;
            var dy = p.Y - q.Y;
            var dz = p.Z - q.Z;
            return dx * dx + dy * dy + dz * dz <= Radius * Radius;
        }

        int Root(int i)
        {
            while (parents[i] != i)
            {
                parents[i] = parents[parents[i]];
                i = parents[i];
            }
            return i;
        }

        void Join(int a, int b)
        {
            a = Root(a);
            b = Root(b);
            parents[Math.Max(a, b)] = Math.Min(a, b);
        }
    }

    private sealed class SearchNode
    {
        public const int LeafSize = 8;
        public int[] Indices { get; }
        public int Start { get; }
        public int Count { get; }
        public int Representative { get; }
        public SpatialPosition Min { get; }
        public SpatialPosition Max { get; }
        public SearchNode? Left { get; private set; }
        public SearchNode? Right { get; private set; }

        public SearchNode(int[] indices, int start, int count, SpatialPosition?[] positions)
        {
            Indices = indices;
            Start = start;
            Count = count;
            Representative = indices[start];
            var min = positions[Representative]!.Value;
            var max = min;
            for (var i = start + 1; i < start + count; i++)
            {
                var p = positions[indices[i]]!.Value;
                min = new(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
                max = new(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
            }
            Min = min;
            Max = max;
        }

        public void Split(SpatialPosition?[] positions)
        {
            if (Left is not null) return;
            var extent = new SpatialPosition(Max.X - Min.X, Max.Y - Min.Y, Max.Z - Min.Z);
            var axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
            Array.Sort(Indices, Start, Count, Comparer<int>.Create((a, b) =>
            {
                var p = positions[a]!.Value;
                var q = positions[b]!.Value;
                var order = axis == 0 ? p.X.CompareTo(q.X) : axis == 1 ? p.Y.CompareTo(q.Y) : p.Z.CompareTo(q.Z);
                return order != 0 ? order : a.CompareTo(b);
            }));
            var half = Count / 2;
            Left = new(Indices, Start, half, positions);
            Right = new(Indices, Start + half, Count - half, positions);
        }
    }
}
