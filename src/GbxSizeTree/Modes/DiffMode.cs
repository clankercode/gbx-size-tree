using System.Runtime;
using System.Security.Cryptography;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.Game;
using GbxSizeTree.Container;
using GbxSizeTree.Measure;

namespace GbxSizeTree.Cli.Modes;

public enum DiffProgressStage
{
    ReadingOld,
    ReadingNew,
    ParsingOld,
    ParsingNew,
    Comparing,
    EmbeddedDeepComparison,
    MeasuringOldEmbeds,
    MeasuringNewEmbeds,
}

public sealed record DiffProgress(
    DiffProgressStage Stage,
    string? WorkItem = null,
    int? Completed = null,
    int? Total = null);

public static class DiffMode
{
    public static int Run(IReadOnlyList<string> paths, CliOutputFormat format, bool all,
        bool? colorOption, bool styled = true)
    {
        if (paths.Count != 2) throw new ArgumentException("diff requires exactly two map paths.");
        DiffReport report;
        using (var progress = TerminalProgress.Create())
            report = CompareFiles(paths[0], paths[1], all,
                progress: progress is null ? null : progress.Report);
        if (format == CliOutputFormat.Json)
        {
            // Stream instead of materializing the full document: reports reach hundreds of MB.
            using var stdout = Console.OpenStandardOutput();
            JsonSerializer.Serialize(stdout, BuildJsonReport(report), DiffJsonContext.Default.DiffJsonReport);
            Console.Out.WriteLine();
        }
        else if (format == CliOutputFormat.Html)
            Console.WriteLine(DiffRenderer.RenderHtml(report, paths[0], paths[1], colorOption, styled));
        else if (format == CliOutputFormat.Markdown)
            Console.WriteLine(DiffRenderer.RenderMarkdown(report, paths[0], paths[1]));
        else
            DiffRenderer.Render(DiffRenderer.BuildConsole(colorOption), report, paths[0], paths[1]);
        return ExitCodes.Ok;
    }

    // Keep the JSON envelope and string-valued changes compatible; snapshots carry typed detail.
    public static string RenderJson(DiffReport report) =>
        JsonSerializer.Serialize(BuildJsonReport(report), DiffJsonContext.Default.DiffJsonReport);

    private static DiffJsonReport BuildJsonReport(DiffReport report) => new(
        report.LeftBytes, report.RightBytes,
        Legacy(report.Blocks, x => x.Key),
        Legacy(report.BakedBlocks, x => x.Key),
        Legacy(report.Items, x => x.Key),
        report.Embedded.Select(c => new Change(c.Left?.ToValue(), c.Right?.ToValue(), (c.Right ?? c.Left)?.Path)),
        report.Embedded,
        report.Chunks,
        report.LeftBakedSnapshots, report.RightBakedSnapshots,
        report.LeftBlockSnapshots, report.RightBlockSnapshots,
        report.LeftItemSnapshots, report.RightItemSnapshots,
        report.LeftEmbeddedSnapshots, report.RightEmbeddedSnapshots,
        report.MapUid, report.MapName, report.AuthorLogin, report.AuthorNickname, report.Password,
        report.MetadataChanges, report.EmbeddedContributions, report.EmbeddedPropertyChanges,
        report.PropertyChangeSummaries, report.EmbeddedUsages,
        report.LeftContributionBaselineBytes, report.RightContributionBaselineBytes, report.Warnings
    );

    private static IEnumerable<Change> Legacy<T>(IEnumerable<ValueChange<T>> changes, Func<T, string> format) where T : class =>
        changes.Select(c => new Change(c.Left is null ? null : format(c.Left), c.Right is null ? null : format(c.Right)));

    /// <summary>Compares map metadata and placements, with bounded outer-body trials for changed embeds.</summary>
    public static DiffReport CompareFiles(string left, string right, bool all = false,
        EmbeddedFileContributionOptions? contributionOptions = null,
        Action<DiffProgress>? progress = null)
    {
        progress = BestEffort(progress);
        IReadOnlyDictionary<string, string> leftContent = new Dictionary<string, string>();
        IReadOnlyDictionary<string, string> rightContent = new Dictionary<string, string>();
        long leftLength = 0, rightLength = 0;
        if (all)
        {
            leftContent = CaptureContent(left, DiffProgressStage.ParsingOld, progress);
            ReleaseTransientMemory();
            rightContent = CaptureContent(right, DiffProgressStage.ParsingNew, progress);
            ReleaseTransientMemory();
            leftLength = new FileInfo(left).Length;
            rightLength = new FileInfo(right).Length;
        }
        DiffReport report;
        // Frame-scoped phases keep file bytes, parsed maps, and snapshots out of later phases (B065).
        try
        {
            report = ParseAndCompare(left, right, leftContent, rightContent, all, progress);
        }
        catch (Exception ex) when (all && ex is not OperationCanceledException and not OutOfMemoryException)
        {
            progress?.Invoke(new(DiffProgressStage.Comparing));
            return ContentOnlyReport(leftLength, rightLength, leftContent, rightContent);
        }
        ReleaseTransientMemory();
        return WithContributions(report, left, right, contributionOptions, progress);
    }

    private static DiffReport ParseAndCompare(string left, string right,
        IReadOnlyDictionary<string, string> leftContent, IReadOnlyDictionary<string, string> rightContent,
        bool all, Action<DiffProgress>? progress)
    {
        var leftSnapshot = ParseSide(left, DiffProgressStage.ReadingOld, DiffProgressStage.ParsingOld, leftContent, progress);
        ReleaseTransientMemory();
        var rightSnapshot = ParseSide(right, DiffProgressStage.ReadingNew, DiffProgressStage.ParsingNew, rightContent, progress);
        ReleaseTransientMemory();
        progress?.Invoke(new(DiffProgressStage.Comparing));
        return Compare(leftSnapshot, rightSnapshot, all, progress);
    }

    private static IReadOnlyDictionary<string, string> CaptureContent(string path, DiffProgressStage stage,
        Action<DiffProgress>? progress)
    {
        progress?.Invoke(new(stage, Path.GetFileName(path)));
        return MapContentComparison.Capture(File.ReadAllBytes(path));
    }

    // File bytes die with this frame; the returned snapshot keeps only extracted data.
    private static Snapshot ParseSide(string path, DiffProgressStage readStage, DiffProgressStage parseStage,
        IReadOnlyDictionary<string, string> content, Action<DiffProgress>? progress)
    {
        progress?.Invoke(new(readStage, Path.GetFileName(path)));
        var bytes = File.ReadAllBytes(path);
        progress?.Invoke(new(parseStage, Path.GetFileName(path)));
        return ReadBytes(bytes, content);
    }

    // Phases hold entire parsed maps and decompressed bodies; reclaiming one phase's garbage
    // before the next allocates keeps peak RSS near a single phase instead of the sum of two.
    private static void ReleaseTransientMemory()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect();
    }

    private static Action<DiffProgress>? BestEffort(Action<DiffProgress>? progress)
    {
        if (progress is null) return null;
        var failed = false;
        return value =>
        {
            if (failed) return;
            try { progress(value); }
            catch { failed = true; }
        };
    }

    private static Snapshot ReadBytes(byte[] bytes, IReadOnlyDictionary<string, string> content)
    {
        Gbx.LZO = new GBX.NET.LZO.Lzo();
        Gbx.ZLib = new GBX.NET.ZLib.ZLib();
        using var stream = new MemoryStream(bytes, writable: false);
        var map = Gbx.Parse<CGameCtnChallenge>(stream).Node;
        return Capture(map, bytes.Length, content);
    }

    private static DiffReport ContentOnlyReport(long leftBytes, long rightBytes,
        IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
    {
        var chunks = MapContentComparison.Compare(left, right);
        return new DiffReport(leftBytes, rightBytes, [], [], [], [], chunks, null, null, null, null, null)
        {
            Warnings = ["GBX.NET map parse failed; semantic fields unavailable. Only original serialized content was compared."],
        };
    }

    // Side bytes are re-read from disk and released per side so both maps never pile up with the
    // decompression plan and trial buffers during measurement (B065 memory target).
    private static DiffReport WithContributions(DiffReport report, string? leftPath, string? rightPath,
        EmbeddedFileContributionOptions? options = null, Action<DiffProgress>? progress = null)
    {
        if (report.Embedded.Count == 0) return report;
        var left = Measure(leftPath, report.Embedded.Select(c => c.Left).OfType<EmbeddedSnapshot>().ToArray(),
            DiffProgressStage.MeasuringOldEmbeds);
        ReleaseTransientMemory();
        var right = Measure(rightPath, report.Embedded.Select(c => c.Right).OfType<EmbeddedSnapshot>().ToArray(),
            DiffProgressStage.MeasuringNewEmbeds);
        var leftEntries = left.Entries.ToDictionary(e => e.Path, StringComparer.Ordinal);
        var rightEntries = right.Entries.ToDictionary(e => e.Path, StringComparer.Ordinal);
        return report with
        {
            LeftContributionBaselineBytes = left.BaselineCompressedBodyBytes,
            RightContributionBaselineBytes = right.BaselineCompressedBodyBytes,
            EmbeddedContributions = report.Embedded.Select(c => new ValueChange<EmbeddedFileContribution>(
                c.Left is null ? null : leftEntries[c.Left.Path],
                c.Right is null ? null : rightEntries[c.Right.Path])).ToArray(),
        };

        EmbeddedFileContributionMeasurement Measure(string? path, EmbeddedSnapshot[] entries,
            DiffProgressStage stage)
        {
            if (entries.Length == 0) return new(null, [], null);
            var bytes = path is null ? null : File.ReadAllBytes(path);
            string? unavailable = null;
            try
            {
                unavailable = bytes is null ? "Measurement requires original container bytes."
                    : !GbxContainerReader.Read(bytes).BodyCompressed ? "Outer body is uncompressed; no LZO contribution is available." : null;
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or OverflowException)
            {
                unavailable = ex.Message;
            }
            if (unavailable is not null)
                return new(null, entries.Select(e => new EmbeddedFileContribution(e.Path, e.Compressed, e.Uncompressed,
                    null, unavailable)).ToArray(), unavailable);
            var measured = new EmbeddedFileContributionMeasurer().Measure(bytes!, entries.Select(e => e.Path).ToArray(), options,
                progress: value => progress?.Invoke(new(stage, value.Path, value.CompletedTrials, value.TotalTrials)));
            var snapshots = entries.ToDictionary(e => e.Path, StringComparer.Ordinal);
            return measured with
            {
                Entries = measured.Entries.Select(e => e with
                {
                    ZipCompressedBytes = e.ZipCompressedBytes ?? snapshots[e.Path].Compressed,
                    ZipRawBytes = e.ZipRawBytes ?? snapshots[e.Path].Uncompressed,
                }).ToArray(),
            };
        }
    }

    /// <summary>
    /// Compares semantic placements; all also compares the chunks serialized by GBX.NET.
    /// In-memory objects have no original container bytes, and properties without chunks are not serialized.
    /// Use MapContentComparison.Compare for original bytes, including data GBX.NET does not preserve.
    /// </summary>
    public static DiffReport CompareMaps(CGameCtnChallenge left, CGameCtnChallenge right, bool all = false) =>
        WithContributions(Compare(Capture(left, 0, SerializedContent(left, all)), Capture(right, 0, SerializedContent(right, all)), all), null, null);

    private static IReadOnlyDictionary<string, string> SerializedContent(CGameCtnChallenge map, bool all)
    {
        if (!all) return new Dictionary<string, string>();
        Gbx.LZO = new GBX.NET.LZO.Lzo();
        Gbx.ZLib = new GBX.NET.ZLib.ZLib();
        using var stream = new MemoryStream();
        new Gbx<CGameCtnChallenge>(map).Save(stream);
        return MapContentComparison.Capture(stream.ToArray());
    }

    private static Snapshot Capture(CGameCtnChallenge map, long fileBytes, IReadOnlyDictionary<string, string> chunks) => new(
        fileBytes, chunks,
        map.Blocks?.Select(BlockSnapshot.From).OrderBy(x => x.PhysicalPosition).ThenBy(x => x.Key, StringComparer.Ordinal).ToArray() ?? [],
        map.BakedBlocks?.Select(BlockSnapshot.From).OrderBy(x => x.PhysicalPosition).ThenBy(x => x.Key, StringComparer.Ordinal).ToArray() ?? [],
        map.AnchoredObjects?.Select(ItemSnapshot.From).OrderBy(x => x.PhysicalPosition).ThenBy(x => x.Key, StringComparer.Ordinal).ToArray() ?? [],
        ReadEmbeds(map), map.EmbeddedZipData, MapMetadataSnapshot.Capture(map), map.MapUid ?? "", map.MapName ?? "", map.AuthorLogin ?? "", map.AuthorNickname ?? "", map.Password is not null);

    private static DiffReport Compare(Snapshot a, Snapshot b, bool all, Action<DiffProgress>? progress = null)
    {
        var embedded = EmbeddedDiff(a.Embedded, b.Embedded);
        var report = new DiffReport(
            a.FileBytes, b.FileBytes, InstanceDiff(a.Blocks, b.Blocks, x => x.PhysicalPosition, x => x.Key),
            all ? InstanceDiff(a.BakedBlocks, b.BakedBlocks, x => x.PhysicalPosition, x => x.Key) : [],
            InstanceDiff(a.Items, b.Items, x => x.PhysicalPosition, x => x.Key), embedded,
            all ? MapContentComparison.Compare(a.Chunks, b.Chunks) : [],
            Different(a.MapUid, b.MapUid), Different(a.MapName, b.MapName),
            Different(a.AuthorLogin, b.AuthorLogin), Different(a.AuthorNickname, b.AuthorNickname),
            Different(a.Password.ToString(), b.Password.ToString()))
        {
            MetadataChanges = MapMetadataSnapshot.Compare(a.Metadata, b.Metadata),
            Warnings = a.Metadata.Values.Concat(b.Metadata.Values)
                .Where(p => p.Key.EndsWith("/status", StringComparison.Ordinal) && p.Value?.Text?.StartsWith("unavailable:", StringComparison.Ordinal) == true)
                .Select(p => $"Metadata {p.Key}: {p.Value!.Text}").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            LeftBakedSnapshots = all ? a.BakedBlocks : [], RightBakedSnapshots = all ? b.BakedBlocks : [],
            LeftBlockSnapshots = a.Blocks, RightBlockSnapshots = b.Blocks,
            LeftItemSnapshots = a.Items, RightItemSnapshots = b.Items,
            LeftEmbeddedSnapshots = a.Embedded.Values.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray(),
            RightEmbeddedSnapshots = b.Embedded.Values.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray(),
        };
        progress?.Invoke(new(DiffProgressStage.EmbeddedDeepComparison));
        return report with
        {
            EmbeddedPropertyChanges = EmbeddedPropertyReport.Capture(a.EmbeddedZipData, b.EmbeddedZipData, embedded),
            PropertyChangeSummaries = PropertySummaries(a.Blocks, b.Blocks, a.Items, b.Items),
            EmbeddedUsages = EmbeddedUsages(a.Embedded.Keys, b.Embedded.Keys, a.Items, b.Items),
        };
    }

    private static IReadOnlyList<PropertyChangeSummary> PropertySummaries(IEnumerable<BlockSnapshot> leftBlocks, IEnumerable<BlockSnapshot> rightBlocks, IEnumerable<ItemSnapshot> leftItems, IEnumerable<ItemSnapshot> rightItems)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        Compare(leftBlocks, rightBlocks, x => $"{x.Name}|{x.Coord}|{x.PhysicalPosition}|{x.Rotation}|{x.Variant}|{x.SubVariant}", (l, r) => { if (l.LightmapQuality != r.LightmapQuality) Add("Lightmap quality"); if (l.IsGround != r.IsGround) Add("Ground state"); });
        Compare(leftItems, rightItems, x => $"{x.Path}|{x.Position}|{x.Rotation}|{x.Color}|{x.Pivot}|{x.Flags}", (l, r) => { if (l.LightmapQuality != r.LightmapQuality) Add("Lightmap quality"); if (l.AnimationPhase != r.AnimationPhase) Add("Animation phase"); });
        return result.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new PropertyChangeSummary(x.Key, x.Value)).ToArray();
        void Add(string property) => result[property] = result.GetValueOrDefault(property) + 1;
        static void Compare<T>(IEnumerable<T> left, IEnumerable<T> right, Func<T, string> identity, Action<T, T> compare) where T : class
        {
            var remaining = right.GroupBy(identity).ToDictionary(x => x.Key, x => new Queue<T>(x), StringComparer.Ordinal);
            foreach (var item in left)
                if (remaining.TryGetValue(identity(item), out var matches) && matches.Count > 0)
                    compare(item, matches.Dequeue());
        }
    }

    private static IReadOnlyList<EmbeddedUsage> EmbeddedUsages(IEnumerable<string> leftPaths, IEnumerable<string> rightPaths, IEnumerable<ItemSnapshot> leftItems, IEnumerable<ItemSnapshot> rightItems)
    {
        var left = leftItems.GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);
        var right = rightItems.GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);
        return leftPaths.Union(rightPaths, StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(path =>
        {
            var leftKnown = left.ContainsKey(path); var rightKnown = right.ContainsKey(path);
            return new EmbeddedUsage(path, leftKnown ? left[path] : null, rightKnown ? right[path] : null,
                leftKnown && rightKnown ? null : "Embedded identity does not match a placed-item model exactly; aliases and transitive dependencies were not resolved.");
        }).ToArray();
    }

    private static Change? Different(string a, string b) => a == b ? null : new(a, b);

    private static Dictionary<string, EmbeddedSnapshot> ReadEmbeds(CGameCtnChallenge map)
    {
        var result = new Dictionary<string, EmbeddedSnapshot>(StringComparer.Ordinal);
        if (map.EmbeddedZipData is not { Length: > 0 }) return result;
        using var archive = map.OpenReadEmbeddedZipData();
        foreach (var entry in archive.Entries)
        {
            using var input = entry.Open();
            var snapshot = new EmbeddedSnapshot(entry.FullName, Convert.ToHexString(SHA256.HashData(input)), entry.CompressedLength, entry.Length);
            if (!result.TryAdd(entry.FullName, snapshot))
                throw new InvalidDataException($"Duplicate embedded ZIP path: {entry.FullName}");
        }
        return result;
    }

    private static IReadOnlyList<ValueChange<T>> InstanceDiff<T>(IEnumerable<T> a, IEnumerable<T> b,
        Func<T, SpatialPosition?> position, Func<T, string> key) where T : class
    {
        var left = a.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var right = b.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var changes = new List<ValueChange<T>>();
        foreach (var value in left.Keys.Union(right.Keys))
        {
            var delta = right.GetValueOrDefault(value) - left.GetValueOrDefault(value);
            for (var i = 0; i < Math.Abs(delta); i++)
                changes.Add(delta < 0 ? new(value, null) : new(null, value));
        }
        return changes.OrderBy(c => position((c.Right ?? c.Left)!))
            .ThenBy(c => key((c.Right ?? c.Left)!), StringComparer.Ordinal)
            .ThenBy(c => c.Left is null ? 1 : 0).ToArray();
    }

    // Content hash decides modification: compressed sizes move with recompression alone.
    private static IReadOnlyList<ValueChange<EmbeddedSnapshot>> EmbeddedDiff(
        IReadOnlyDictionary<string, EmbeddedSnapshot> a, IReadOnlyDictionary<string, EmbeddedSnapshot> b) =>
        a.Keys.Union(b.Keys, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal)
            .Where(k => !a.TryGetValue(k, out var av) || !b.TryGetValue(k, out var bv) || av.Sha256 != bv.Sha256)
            .Select(k => new ValueChange<EmbeddedSnapshot>(a.GetValueOrDefault(k), b.GetValueOrDefault(k))).ToArray();

    private sealed record Snapshot(long FileBytes, IReadOnlyDictionary<string, string> Chunks,
        BlockSnapshot[] Blocks, BlockSnapshot[] BakedBlocks, ItemSnapshot[] Items,
        IReadOnlyDictionary<string, EmbeddedSnapshot> Embedded, byte[]? EmbeddedZipData,
        MapMetadataSnapshot Metadata,
        string MapUid, string MapName, string AuthorLogin, string AuthorNickname, bool Password);
}
