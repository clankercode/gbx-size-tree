namespace GbxSizeTree.Tests.Fixtures;

/// <summary>
/// Locates the (uncommitted) sample map. Tests needing it call <see cref="SkipUnlessAvailable"/>
/// so the suite passes on machines without it.
/// </summary>
public static class SampleMap
{
    public const string DefaultPath = "/home/xertrov/Downloads/sample.Map.Gbx";

    public static string Path =>
        Environment.GetEnvironmentVariable("GBX_SIZE_TREE_SAMPLE") is { Length: > 0 } p ? p : DefaultPath;

    public static bool Available => File.Exists(Path);

    public static void SkipUnlessAvailable() =>
        Assert.SkipUnless(Available, $"sample map not found (set GBX_SIZE_TREE_SAMPLE); looked at: {Path}");

    // Ground truth for the default sample (docs/FORMAT-NOTES.md).
    public const long FileBytes = 7_832_571;
    public const long BodyUncompressed = 18_529_821;
    public const long BodyCompressed = 7_767_065;
    public const int UserDataSize = 65_473;
}
