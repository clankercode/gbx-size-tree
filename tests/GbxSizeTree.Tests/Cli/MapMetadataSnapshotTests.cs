using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GBX.NET;
using GBX.NET.Engines.Game;
using GbxSizeTree.Tests.Fixtures;
using GbxSizeTree.Cli.Modes;
using TmEssentials;

namespace GbxSizeTree.Tests.Cli;

public sealed class MapMetadataSnapshotTests
{
    public static TheoryData<string, Action<CGameCtnChallenge>, MapMetadataValue> ScalarSettings => new()
    {
        { "map.uid", m => m.MapUid = "uid", new(Text: "uid") },
        { "map.name", m => m.MapName = "$f00Name", new(Text: "$f00Name") },
        { "map.type", m => m.MapType = "TrackMania\\TM_Race", new(Text: "TrackMania\\TM_Race") },
        { "map.style", m => m.MapStyle = "Tech", new(Text: "Tech") },
        { "map.kind", m => m.Kind = (CGameCtnChallenge.MapKind)3, new(Integer: 3) },
        { "map.kindInHeader", m => m.KindInHeader = (CGameCtnChallenge.MapKind)4, new(Integer: 4) },
        { "map.playMode", m => m.Mode = (CGameCtnChallenge.PlayMode)6, new(Integer: 6) },
        { "map.cost", m => m.Cost = 1234, new(Integer: 1234) },
        { "map.isLapRace", m => m.IsLapRace = true, new(Boolean: true) },
        { "map.laps", m => m.NbLaps = 3, new(Integer: 3) },
        { "map.checkpoints", m => m.NbCheckpoints = 7, new(Integer: 7) },
        { "map.needUnlock", m => m.NeedUnlock = true, new(Boolean: true) },
        { "map.hasClones", m => m.HasClones = true, new(Boolean: true) },
        { "author.login", m => m.AuthorLogin = "login", new(Text: "login") },
        { "author.nickname", m => m.AuthorNickname = "$fffNick", new(Text: "$fffNick") },
        { "author.zone", m => m.AuthorZone = "World|Australia", new(Text: "World|Australia") },
        { "author.extraInfo", m => m.AuthorExtraInfo = "extra", new(Text: "extra") },
        { "author.version", m => m.AuthorVersion = 9, new(Integer: 9) },
        { "build.version", m => m.BuildVersion = "2026-09", new(Text: "2026-09") },
        { "build.titleId", m => m.TitleId = "Trackmania", new(Text: "Trackmania") },
        { "editor.flags", m => m.Editor = (CGameCtnChallenge.EditorMode)7, new(Integer: 7) },
        { "display.comments", m => m.Comments = "Line 1\nLine 2", new(Text: "Line 1\nLine 2") },
        { "display.objectiveAuthor", m => m.ObjectiveTextAuthor = "A", new(Text: "A") },
        { "display.objectiveGold", m => m.ObjectiveTextGold = "G", new(Text: "G") },
        { "display.objectiveSilver", m => m.ObjectiveTextSilver = "S", new(Text: "S") },
        { "display.objectiveBronze", m => m.ObjectiveTextBronze = "B", new(Text: "B") },
        { "display.palette", m => m.Palette = (CGameCtnChallenge.PaletteColor)2, new(Integer: 2) },
        { "display.decoBaseHeightOffset", m => m.DecoBaseHeightOffset = -17, new(Integer: -17) },
        { "lighting.dynamicDaylight", m => m.DynamicDaylight = true, new(Boolean: true) },
        { "lighting.dayDurationMs", m => m.DayDuration = new TimeInt32(60001), new(Integer: 60001) },
        { "lighting.hasLightmaps", m => m.HasLightmaps = true, new(Boolean: true) },
        { "lighting.version", m => m.LightmapVersion = 8, new(Integer: 8) },
        { "security.passwordPresent", m => m.Password = "secret", new(Boolean: true) },
    };

    [Theory]
    [MemberData(nameof(ScalarSettings))]
    public void Compare_DetectsEachExplicitScalarSetting(string path, Action<CGameCtnChallenge> edit, MapMetadataValue expected)
    {
        var map = new CGameCtnChallenge();
        var before = MapMetadataSnapshot.Capture(map);
        edit(map);
        var after = MapMetadataSnapshot.Capture(map);
        var change = Assert.Single(MapMetadataSnapshot.Compare(before, after));
        Assert.Equal(path, change.Path);
        Assert.Equal(expected, change.Right);
        var reversed = Assert.Single(MapMetadataSnapshot.Compare(after, before));
        Assert.Equal(new MapMetadataChange(path, change.Right, change.Left), reversed);
    }

    public static TheoryData<string, Action<CGameCtnChallengeParameters>, MapMetadataValue> ParameterSettings => new()
    {
        { "medals.authorMs", p => p.AuthorTime = new TimeInt32(1001), new(Integer: 1001) },
        { "medals.bronzeMs", p => p.BronzeTime = new TimeInt32(4004), new(Integer: 4004) },
        { "medals.silverMs", p => p.SilverTime = new TimeInt32(3003), new(Integer: 3003) },
        { "medals.goldMs", p => p.GoldTime = new TimeInt32(2002), new(Integer: 2002) },
        { "medals.authorScore", p => p.AuthorScore = 777, new(Integer: 777) },
        { "map.type", p => p.MapType = "script", new(Text: "script") },
        { "map.style", p => p.MapStyle = "style", new(Text: "style") },
        { "validation.forScriptModes", p => p.IsValidatedForScriptModes = true, new(Boolean: true) },
        { "validation.timeLimitMs", p => p.TimeLimit = new TimeInt32(12345), new(Integer: 12345) },
        { "display.tip", p => p.Tip = "tip", new(Text: "tip") },
    };

    [Theory]
    [MemberData(nameof(ParameterSettings))]
    public void Capture_UsesEffectiveChallengeParameters(string path, Action<CGameCtnChallengeParameters> edit, MapMetadataValue expected)
    {
        var map = new CGameCtnChallenge { ChallengeParameters = new() };
        var before = MapMetadataSnapshot.Capture(map);
        edit(map.ChallengeParameters);
        var change = Assert.Single(MapMetadataSnapshot.Compare(before, MapMetadataSnapshot.Capture(map)));
        Assert.Equal(path, change.Path);
        Assert.Equal(expected, change.Right);
    }

    [Fact]
    public void Capture_DoesNotFallbackToHeaderWhenParametersHaveNullMedalsOrStyle()
    {
        var map = new CGameCtnChallenge { AuthorTime = new TimeInt32(123), MapStyle = "header" };
        map.ChallengeParameters = new() { AuthorTime = null, MapStyle = null };
        var snapshot = MapMetadataSnapshot.Capture(map);
        Assert.Null(snapshot.Values["medals.authorMs"]);
        Assert.Null(snapshot.Values["map.style"]);
    }

    [Fact]
    public void Capture_DistinguishesAbsentEmptyFalseAndZeroWithoutExposingPasswords()
    {
        var absent = MapMetadataSnapshot.Capture(new());
        var present = MapMetadataSnapshot.Capture(new()
        {
            MapStyle = "", Comments = "", Password = "", ChallengeParameters = new()
            {
                MapStyle = "", IsValidatedForScriptModes = false, TimeLimit = new TimeInt32(0),
            },
        });
        Assert.Null(absent.Values["map.style"]);
        Assert.Equal(new MapMetadataValue(Text: ""), present.Values["map.style"]);
        Assert.Null(absent.Values["validation.forScriptModes"]);
        Assert.Equal(new MapMetadataValue(Boolean: false), present.Values["validation.forScriptModes"]);
        Assert.Null(absent.Values["validation.timeLimitMs"]);
        Assert.Equal(new MapMetadataValue(Integer: 0), present.Values["validation.timeLimitMs"]);
        Assert.Equal(new MapMetadataValue(Boolean: true), present.Values["security.passwordPresent"]);
        Assert.Empty(MapMetadataSnapshot.Compare(MapMetadataSnapshot.Capture(new() { Password = "secret-a" }),
            MapMetadataSnapshot.Capture(new() { Password = "secret-b" })));
    }

    [Fact]
    public void Capture_DoesNotOpenLazyOrHeavyMetadata()
    {
        var map = new CGameCtnChallenge
        {
            LightmapVersion = 8, LightmapCacheData = new ZlibData(100, [255], null),
            ZoneGenealogyData = new RawData([255], null), EmbeddedZipData = [255],
            Thumbnail = [255], Xml = "<malformed", Blocks = [new()], AnchoredObjects = [new()],
            ScriptMetadata = new(),
        };
        var before = MapMetadataSnapshot.Capture(map);
        map.EmbeddedZipData = [0];
        map.Xml = "different";
        map.Blocks.Clear();
        Assert.Empty(MapMetadataSnapshot.Compare(before, MapMetadataSnapshot.Capture(map)));
        Assert.False(map.LightmapCacheData.Parsed);
        Assert.Null(map.LightmapCacheData.Exception);
        Assert.False(map.ZoneGenealogyData.Parsed);
        Assert.Null(map.ZoneGenealogyData.Exception);
    }

    [Fact]
    public void Compare_IsOrdinalCultureIndependentAndSourceGeneratedJsonSafe()
    {
        var map = new CGameCtnChallenge { Comments = "<tag>\u001b[31m $f00", AuthorTime = new TimeInt32(-12345), Editor = (CGameCtnChallenge.EditorMode)128 };
        var left = MapMetadataSnapshot.Capture(new());
        var right = MapMetadataSnapshot.Capture(map);
        var expected = JsonSerializer.Serialize(MapMetadataSnapshot.Compare(left, right), MetadataTestJsonContext.Default.IReadOnlyListMapMetadataChange);
        var culture = CultureInfo.CurrentCulture;
        try
        {
            var custom = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            custom.NumberFormat.NegativeSign = "negative";
            CultureInfo.CurrentCulture = custom;
            var actual = JsonSerializer.Serialize(MapMetadataSnapshot.Compare(left, MapMetadataSnapshot.Capture(map)), MetadataTestJsonContext.Default.IReadOnlyListMapMetadataChange);
            Assert.Equal(expected, actual);
            using var json = JsonDocument.Parse(actual);
            var changes = json.RootElement;
            Assert.Equal("display.comments", changes[0].GetProperty("Path").GetString());
            Assert.Equal(map.Comments, changes[0].GetProperty("Right").GetProperty("Text").GetString());
            Assert.Equal(128, changes[1].GetProperty("Right").GetProperty("Integer").GetInt64());
            Assert.Equal(-12345, changes[2].GetProperty("Right").GetProperty("Integer").GetInt64());
            Assert.Equal(right.Values.Keys.Order(StringComparer.Ordinal), right.Values.Keys);
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [Fact]
    public void Capture_RealMapSelfDiffAndMedalEdit()
    {
        SampleMap.SkipUnlessAvailable();
        Gbx.LZO ??= new GBX.NET.LZO.Lzo();
        Gbx.ZLib = new GBX.NET.ZLib.ZLib();
        var map = Gbx.Parse<CGameCtnChallenge>(SampleMap.Path).Node;
        var before = MapMetadataSnapshot.Capture(map);
        Assert.Empty(MapMetadataSnapshot.Compare(before, MapMetadataSnapshot.Capture(map)));
        Assert.Equal(map.MapName, before.Values["map.name"]!.Text);
        map.AuthorTime = new TimeInt32((map.AuthorTime?.TotalMilliseconds ?? 0) + 1);
        var change = Assert.Single(MapMetadataSnapshot.Compare(before, MapMetadataSnapshot.Capture(map)));
        Assert.Equal("medals.authorMs", change.Path);
        Assert.Equal(map.AuthorTime.Value.TotalMilliseconds, change.Right!.Integer);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(123456789012)]
    public void Capture_PreservesDayTimeTicksAndAbsentVersusMidnight(long ticks)
    {
        var absent = MapMetadataSnapshot.Capture(new());
        var present = MapMetadataSnapshot.Capture(new() { DayTime = TimeSpan.FromTicks(ticks) });
        var change = Assert.Single(MapMetadataSnapshot.Compare(absent, present));
        Assert.Equal("lighting.dayTimeTicks", change.Path);
        Assert.Null(change.Left);
        Assert.Equal(new MapMetadataValue(Integer: ticks), change.Right);
        var serialized = JsonSerializer.Serialize(new[] { change }, MetadataTestJsonContext.Default.IReadOnlyListMapMetadataChange);
        using var json = JsonDocument.Parse(serialized);
        Assert.Equal(ticks, json.RootElement[0].GetProperty("Right").GetProperty("Integer").GetInt64());
    }

    [Fact]
    public void Capture_ReportsHashedProtectionWithoutExposingHashesOrChangingLegacyPasswordPresence()
    {
        var map = new CGameCtnChallenge();
        var absent = MapMetadataSnapshot.Capture(map);
        map.HashedPassword = Checksum128.Zero;
        Assert.Empty(MapMetadataSnapshot.Compare(absent, MapMetadataSnapshot.Capture(map)));
        map.HashedPassword = new Checksum128(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        var protectedMap = MapMetadataSnapshot.Capture(map);
        var change = Assert.Single(MapMetadataSnapshot.Compare(absent, protectedMap));
        Assert.Equal("security.hashedPasswordPresent", change.Path);
        Assert.Equal(new MapMetadataValue(Boolean: false), change.Left);
        Assert.Equal(new MapMetadataValue(Boolean: true), change.Right);
        Assert.Equal(new MapMetadataValue(Boolean: false), protectedMap.Values["security.passwordPresent"]);
        map.HashedPassword = new Checksum128(new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        Assert.Empty(MapMetadataSnapshot.Compare(protectedMap, MapMetadataSnapshot.Capture(map)));
        map.RemovePassword();
        Assert.Empty(MapMetadataSnapshot.Compare(absent, MapMetadataSnapshot.Capture(map)));
        Assert.Equal(new MapMetadataChange(change.Path, change.Right, change.Left),
            Assert.Single(MapMetadataSnapshot.Compare(protectedMap, MapMetadataSnapshot.Capture(map))));
    }

    [Fact]
    public void Capture_PreservesMedalMillisecondsAndAbsentVersusZero()
    {
        var left = MapMetadataSnapshot.Capture(new CGameCtnChallenge());
        var right = MapMetadataSnapshot.Capture(new CGameCtnChallenge
        {
            BronzeTime = new TimeInt32(90001), SilverTime = new TimeInt32(80002),
            GoldTime = new TimeInt32(70003), AuthorTime = new TimeInt32(0), AuthorScore = 42,
        });

        var changes = MapMetadataSnapshot.Compare(left, right);
        Assert.Equal(new[] { "medals.authorMs", "medals.authorScore", "medals.bronzeMs", "medals.goldMs", "medals.silverMs" },
            changes.Select(c => c.Path));
        Assert.Null(changes[0].Left);
        Assert.Equal(0, changes[0].Right!.Integer);
        Assert.Equal(42, changes[1].Right!.Integer);
        Assert.Equal(90001, changes[2].Right!.Integer);
        Assert.Equal(70003, changes[3].Right!.Integer);
        Assert.Equal(80002, changes[4].Right!.Integer);
        Assert.Empty(MapMetadataSnapshot.Compare(right, right));
    }
}

[JsonSerializable(typeof(IReadOnlyList<MapMetadataChange>))]
internal partial class MetadataTestJsonContext : JsonSerializerContext;
