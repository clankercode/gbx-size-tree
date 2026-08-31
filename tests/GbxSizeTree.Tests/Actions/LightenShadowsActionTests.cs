using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.Hms;
using GBX.NET.LZO;
using GBX.NET.ZLib;
using GbxSizeTree.Abstractions;
using GbxSizeTree.Actions;
using GbxSizeTree.Actions.Passes;
using GbxSizeTree.Analysis;
using GbxSizeTree.Tests.Fixtures;

namespace GbxSizeTree.Tests.Actions;

public sealed class LightenShadowsActionTests
{
    [Fact]
    public void Apply_FloorsOnlyShadowBrightnessChannel()
    {
        var shadowBrightness = new byte[] { 0, 99, 100, 200 };
        var otherChannel = new byte[] { 0, 20, 255 };
        var mapping = new CHmsLightMapCache.SMapping
        {
            ZlibData6Decompressed = [shadowBrightness, otherChannel],
        };
        var map = new CGameCtnChallenge
        {
            HasLightmaps = true,
            LightmapCache = new CHmsLightMapCache { Mapping = mapping },
        };
        var action = new LightenShadowsAction();
        var settings = Settings(100);

        var applicability = action.Detect(new ActionDetectContext(
            FakeAnalysis.Build(), settings, NullStatusSink.Instance, map));
        var result = action.Apply(new ActionApplyContext(
            new Gbx<CGameCtnChallenge>(map),
            map,
            FakeAnalysis.Build(),
            settings,
            NullStatusSink.Instance));

        Assert.True(applicability.Applies);
        Assert.True(result.Changed);
        Assert.Equal([100, 100, 100, 200], shadowBrightness);
        Assert.Equal([0, 20, 255], otherChannel);
        Assert.False(action.DefaultOn);
        Assert.False(action.RecommendByDefault);
        Assert.Equal(25, action.Order);
    }

    [Fact]
    public void Detect_WithoutExplicitFloorOrMap_DoesNotSurfaceAction()
    {
        var action = new LightenShadowsAction();

        var absent = action.Detect(new ActionDetectContext(
            FakeAnalysis.Build(),
            new Dictionary<string, string>(),
            NullStatusSink.Instance));
        var withoutMap = action.Detect(new ActionDetectContext(
            FakeAnalysis.Build(),
            Settings(100),
            NullStatusSink.Instance));

        Assert.False(absent.Applies);
        Assert.False(withoutMap.Applies);
    }

    [Fact]
    public void Sample_ApplySaveAndReparse_PreservesImagesAndOtherChannels()
    {
        SampleMap.SkipUnlessAvailable();
        EnsureCodecs();

        var analysis = MapAnalyzer.CreateDefault(NullStatusSink.Instance).Analyze(
            new MapSource.FromFile(SampleMap.Path),
            new AnalyzeOptions());
        var gbx = Gbx.Parse<CGameCtnChallenge>(SampleMap.Path);
        var map = gbx.Node;
        var channels = map.LightmapCache?.Mapping?.ZlibData6Decompressed;
        Assert.NotNull(channels);
        Assert.NotEmpty(channels);
        Assert.Contains(channels[0], value => value < 100);

        var otherChannels = channels.Skip(1).Select(channel => channel.ToArray()).ToArray();
        var images = SnapshotImages(map);
        var action = new LightenShadowsAction();
        var settings = Settings(100);

        var result = action.Apply(new ActionApplyContext(
            gbx,
            map,
            analysis,
            settings,
            NullStatusSink.Instance));

        Assert.True(result.Changed);
        Assert.All(channels[0], value => Assert.True(value >= 100));
        Assert.Equal(otherChannels, channels.Skip(1).Select(channel => channel.ToArray()).ToArray());
        Assert.Equal(images, SnapshotImages(map));

        using var saved = new MemoryStream();
        gbx.Save(saved);
        saved.Position = 0;
        var reparsed = Gbx.Parse<CGameCtnChallenge>(saved).Node;
        var reparsedChannels = reparsed.LightmapCache?.Mapping?.ZlibData6Decompressed;
        Assert.NotNull(reparsedChannels);
        Assert.All(reparsedChannels[0], value => Assert.True(value >= 100));
        Assert.Equal(otherChannels, reparsedChannels.Skip(1).Select(channel => channel.ToArray()).ToArray());
        Assert.Equal(images, SnapshotImages(reparsed));
    }

    private static IReadOnlyList<byte[]> SnapshotImages(CGameCtnChallenge map) =>
        map.LightmapFrames?
            .SelectMany(frame => new[] { frame.Data, frame.Data2, frame.Data3 })
            .Where(data => data is not null)
            .Select(data => data!.ToArray())
            .ToArray() ?? [];

    private static IReadOnlyDictionary<string, string> Settings(byte floor) =>
        new Dictionary<string, string>
        {
            [LightenShadowsAction.FloorSetting] = floor.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
        };

    private static void EnsureCodecs()
    {
        Gbx.LZO = new Lzo();
        Gbx.ZLib = new ZLib();
    }
}
