using System.Globalization;
using GBX.NET;
using GBX.NET.Engines.Game;
using GbxSizeTree.Measure;

namespace GbxSizeTree.Cli.Modes;

public sealed record ValueChange<T>(T? Left, T? Right) where T : class;

public readonly record struct SpatialPosition(double X, double Y, double Z) : IComparable<SpatialPosition>
{
    public static SpatialPosition From(Vec3 value) => new(value.X, value.Y, value.Z);

    // TM2020 grid cells are 32 × 8 × 32 metres. Convert before multiplying to avoid integer overflow.
    public static SpatialPosition Midpoint(Int3 coord) => new(((double)coord.X + .5) * 32, ((double)coord.Y + .5) * 8, ((double)coord.Z + .5) * 32);

    // Compare interleaved centimetre coordinates (Morton order) without truncating a packed key.
    public int CompareTo(SpatialPosition other)
    {
        Span<ulong> a = stackalloc ulong[] { Cell(X), Cell(Y), Cell(Z) };
        Span<ulong> b = stackalloc ulong[] { Cell(other.X), Cell(other.Y), Cell(other.Z) };
        var axis = 0;
        var leading = 64;
        for (var i = 0; i < 3; i++)
        {
            var count = System.Numerics.BitOperations.LeadingZeroCount(a[i] ^ b[i]);
            if (count < leading) { leading = count; axis = i; }
        }
        if (leading < 64) return a[axis].CompareTo(b[axis]);
        var x = X.CompareTo(other.X);
        if (x != 0) return x;
        var z = Z.CompareTo(other.Z);
        return z != 0 ? z : Y.CompareTo(other.Y);
    }

    private static ulong Cell(double value)
    {
        var scaled = Math.Floor(value * 100);
        var cell = double.IsNaN(scaled) ? 0L : scaled <= long.MinValue ? long.MinValue
            : scaled >= long.MaxValue ? long.MaxValue : (long)scaled;
        return unchecked((ulong)cell) ^ (1UL << 63);
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"({X:G}, {Y:G}, {Z:G})");
}

public sealed record BlockSnapshot(
    string Name, int X, int Y, int Z, SpatialPosition? PhysicalPosition, SpatialPosition? Rotation,
    string Direction, byte Variant, byte SubVariant, bool IsFree, bool IsGhost, bool IsGround,
    string Color, string LightmapQuality, int Flags)
{
    public string Coord => $"({X}, {Y}, {Z})";
    public string Key => FormattableString.Invariant($"{Name}|coord={Coord}|pos={PhysicalPosition?.ToString() ?? "--"}|rotation={Rotation?.ToString() ?? Direction}|variant={Variant}|subvariant={SubVariant}|color={Color}|lightmap={LightmapQuality}|flags={Flags}");

    public static BlockSnapshot From(CGameCtnBlock block) => new(
        block.Name ?? "<unknown>", block.Coord.X, block.Coord.Y, block.Coord.Z,
        block.IsFree ? block.AbsolutePositionInMap is { } pos ? SpatialPosition.From(pos) : null : SpatialPosition.Midpoint(block.Coord),
        block.IsFree && block.YawPitchRoll is { } rotation ? SpatialPosition.From(rotation) : null,
        block.Direction.ToString(), block.Variant, block.SubVariant, block.IsFree, block.IsGhost, block.IsGround,
        block.Color.ToString(), block.LightmapQuality.ToString(), block.Flags);
}

public sealed record ItemSnapshot(
    string Path, SpatialPosition PhysicalPosition, SpatialPosition Rotation, string Color, float Scale,
    SpatialPosition Pivot, string AnimationPhase, string LightmapQuality, short Flags)
{
    public string Position => PhysicalPosition.ToString();
    public string Key => FormattableString.Invariant($"{Path}|position={Position}|rotation={Rotation}|color={Color}|pivot={Pivot}|animation={AnimationPhase}|lightmap={LightmapQuality}|flags={Flags}");

    public static ItemSnapshot From(CGameCtnAnchoredObject item) => new(
        item.ItemModel?.Id ?? "<unknown>", SpatialPosition.From(item.AbsolutePositionInMap),
        SpatialPosition.From(item.YawPitchRoll), item.Color.ToString(), item.Scale,
        SpatialPosition.From(item.PivotPosition), item.AnimPhaseOffset.ToString(), item.LightmapQuality.ToString(), item.Flags);
}

public sealed record EmbeddedSnapshot(string Path, string Sha256, long Compressed, long Uncompressed)
{
    public double Ratio => Uncompressed == 0 ? 0 : (double)Compressed / Uncompressed;
    public string ToValue() => FormattableString.Invariant($"{Path}|compressed={Compressed}|uncompressed={Uncompressed}|ratio={Ratio:0.####}");
}

public sealed record Change(string? Left, string? Right, string? Key = null);

public sealed record DiffReport(
    long LeftBytes, long RightBytes, IReadOnlyList<ValueChange<BlockSnapshot>> Blocks,
    IReadOnlyList<ValueChange<BlockSnapshot>> BakedBlocks, IReadOnlyList<ValueChange<ItemSnapshot>> Items,
    IReadOnlyList<ValueChange<EmbeddedSnapshot>> Embedded, IReadOnlyList<Change> Chunks,
    Change? MapUid, Change? MapName, Change? AuthorLogin, Change? AuthorNickname, Change? Password)
{
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<MapMetadataChange> MetadataChanges { get; init; } = [];
    public IReadOnlyList<ValueChange<EmbeddedFileContribution>> EmbeddedContributions { get; init; } = [];
    public long? LeftContributionBaselineBytes { get; init; }
    public long? RightContributionBaselineBytes { get; init; }
    public IReadOnlyList<BlockSnapshot> LeftBakedSnapshots { get; init; } = [];
    public IReadOnlyList<BlockSnapshot> RightBakedSnapshots { get; init; } = [];
    public IReadOnlyList<BlockSnapshot> LeftBlockSnapshots { get; init; } = [];
    public IReadOnlyList<BlockSnapshot> RightBlockSnapshots { get; init; } = [];
    public IReadOnlyList<ItemSnapshot> LeftItemSnapshots { get; init; } = [];
    public IReadOnlyList<ItemSnapshot> RightItemSnapshots { get; init; } = [];
    public IReadOnlyList<EmbeddedSnapshot> LeftEmbeddedSnapshots { get; init; } = [];
    public IReadOnlyList<EmbeddedSnapshot> RightEmbeddedSnapshots { get; init; } = [];
}
