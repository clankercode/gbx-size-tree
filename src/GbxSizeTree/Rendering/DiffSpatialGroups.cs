using GbxSizeTree.Cli.Modes;

namespace GbxSizeTree.Cli.Rendering;

internal static class DiffSpatialGroups
{
    // Two horizontal grid cells; connected neighbours form a region, including across Morton seams.
    private const double Radius = 64;

    public static IEnumerable<(ValueChange<T> Change, int Group)> Order<T>(IReadOnlyList<ValueChange<T>> changes,
        Func<T, SpatialPosition?> position, Func<T, string> key) where T : class
    {
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
            var bucket = new SpatialPosition(Math.Floor(p.X / Radius), Math.Floor(p.Y / Radius), Math.Floor(p.Z / Radius));
            for (var x = -1; x <= 1; x++)
                for (var y = -1; y <= 1; y++)
                    for (var z = -1; z <= 1; z++)
                        if (buckets.TryGetValue(new(bucket.X + x, bucket.Y + y, bucket.Z + z), out var neighbours))
                            foreach (var j in neighbours)
                            {
                                var q = positions[j]!.Value;
                                var dx = p.X - q.X;
                                var dy = p.Y - q.Y;
                                var dz = p.Z - q.Z;
                                if (dx * dx + dy * dy + dz * dz <= Radius * Radius) Join(i, j);
                            }
            if (!buckets.TryGetValue(bucket, out var entries)) buckets.Add(bucket, entries = []);
            entries.Add(i);
        }
        return Enumerable.Range(0, ordered.Length).GroupBy(Root).OrderBy(g => g.Key)
            .SelectMany(g => g.Select(i => (ordered[i], g.Key)));

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
}
