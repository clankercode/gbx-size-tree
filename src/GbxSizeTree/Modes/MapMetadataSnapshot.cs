using GBX.NET.Engines.Game;

namespace GbxSizeTree.Cli.Modes;

// Null means API absence, not necessarily a missing serialized chunk. Captured scalars populate one member.
public sealed record MapMetadataValue(string? Text = null, long? Integer = null, bool? Boolean = null);

public sealed record MapMetadataChange(string Path, MapMetadataValue? Left, MapMetadataValue? Right);

public sealed class MapMetadataSnapshot
{
    public IReadOnlyDictionary<string, MapMetadataValue?> Values { get; }

    private MapMetadataSnapshot(SortedDictionary<string, MapMetadataValue?> values) =>
        Values = new System.Collections.ObjectModel.ReadOnlyDictionary<string, MapMetadataValue?>(values);

    public static MapMetadataSnapshot Capture(CGameCtnChallenge map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var values = new SortedDictionary<string, MapMetadataValue?>(StringComparer.Ordinal)
        {
            ["author.extraInfo"] = Text(map.AuthorExtraInfo),
            ["author.login"] = Text(map.AuthorLogin),
            ["author.nickname"] = Text(map.AuthorNickname),
            ["author.version"] = Number(map.AuthorVersion),
            ["author.zone"] = Text(map.AuthorZone),
            ["build.titleId"] = Text(map.TitleId),
            ["build.version"] = Text(map.BuildVersion),
            ["display.comments"] = Text(map.Comments),
            ["display.decoBaseHeightOffset"] = Number(map.DecoBaseHeightOffset),
            ["display.objectiveAuthor"] = Text(map.ObjectiveTextAuthor),
            ["display.objectiveBronze"] = Text(map.ObjectiveTextBronze),
            ["display.objectiveGold"] = Text(map.ObjectiveTextGold),
            ["display.objectiveSilver"] = Text(map.ObjectiveTextSilver),
            ["display.palette"] = Number((int)map.Palette),
            ["display.tip"] = Text(map.ChallengeParameters?.Tip),
            ["editor.flags"] = Number((int)map.Editor),
            ["lighting.dayDurationMs"] = Number(map.DayDuration?.TotalMilliseconds),
            ["lighting.dayTimeTicks"] = Number(map.DayTime?.Ticks),
            ["lighting.dynamicDaylight"] = Flag(map.DynamicDaylight),
            ["lighting.hasLightmaps"] = Flag(map.HasLightmaps),
            ["lighting.version"] = Number(map.LightmapVersion),
            ["map.checkpoints"] = Number(map.NbCheckpoints),
            ["map.cost"] = Number(map.Cost),
            ["map.hasClones"] = Flag(map.HasClones),
            ["map.isLapRace"] = Flag(map.IsLapRace),
            ["map.kind"] = Number((int)map.Kind),
            ["map.kindInHeader"] = Number((int)map.KindInHeader),
            ["map.laps"] = Number(map.NbLaps),
            ["map.name"] = Text(map.MapName),
            ["map.needUnlock"] = Flag(map.NeedUnlock),
            ["map.playMode"] = Number((int)map.Mode),
            ["map.style"] = Text(map.MapStyle),
            ["map.type"] = Text(map.MapType),
            ["map.uid"] = Text(map.MapUid),
            ["medals.authorMs"] = Number(map.AuthorTime?.TotalMilliseconds),
            ["medals.authorScore"] = Number(map.AuthorScore),
            ["medals.bronzeMs"] = Number(map.BronzeTime?.TotalMilliseconds),
            ["medals.goldMs"] = Number(map.GoldTime?.TotalMilliseconds),
            ["medals.silverMs"] = Number(map.SilverTime?.TotalMilliseconds),
            // RemovePassword leaves a zero hash; do not expose either credential's contents.
            ["security.hashedPasswordPresent"] = Flag(map.HashedPassword is { } hash && hash != GBX.NET.Checksum128.Zero),
            ["security.passwordPresent"] = Flag(map.Password is not null),
            ["validation.forScriptModes"] = Flag(map.ChallengeParameters?.IsValidatedForScriptModes),
            ["validation.timeLimitMs"] = Number(map.ChallengeParameters?.TimeLimit.TotalMilliseconds),
        };
        return new(values);
    }

    public static IReadOnlyList<MapMetadataChange> Compare(MapMetadataSnapshot left, MapMetadataSnapshot right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return left.Values.Keys.Union(right.Values.Keys, StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Where(path => left.Values.GetValueOrDefault(path) != right.Values.GetValueOrDefault(path))
            .Select(path => new MapMetadataChange(path, left.Values.GetValueOrDefault(path), right.Values.GetValueOrDefault(path)))
            .ToArray();
    }

    private static MapMetadataValue? Text(string? value) => value is null ? null : new(Text: value);
    private static MapMetadataValue? Flag(bool? value) => value is null ? null : new(Boolean: value);
    private static MapMetadataValue? Number(long? value) => value is null ? null : new(Integer: value);
}
