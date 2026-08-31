using GBX.NET.Engines.Game;
using GbxSizeTree.Abstractions;
using GbxSizeTree.Model;

namespace GbxSizeTree.Drilldown;

/// <summary>
/// Summarizes the MediaTracker clips serialized by map chunk 0x03043049; see
/// <c>docs/FORMAT-NOTES.md</c>.
/// </summary>
public sealed class MediaTrackerDrilldown : IMediaTrackerDrilldown
{
    public MediaTrackerInfo? Inspect(CGameCtnChallenge map, long chunkBytes)
    {
        var parts = new List<string>();
        var clipCount = 0;

        AddSingleClip(map.ClipIntro, "intro", parts, ref clipCount);
        AddSingleClip(map.ClipPodium, "podium", parts, ref clipCount);
        AddClipGroup(map.ClipGroupInGame?.Clips, "in-game", parts, ref clipCount);
        AddClipGroup(map.ClipGroupEndRace?.Clips, "end-race", parts, ref clipCount);
        AddSingleClip(map.ClipAmbiance, "ambiance", parts, ref clipCount);

        if (chunkBytes == 0 && clipCount == 0)
        {
            return null;
        }

        return new MediaTrackerInfo(
            chunkBytes,
            clipCount,
            parts.Count == 0 ? null : string.Join(" + ", parts));
    }

    private static void AddSingleClip(
        object? clip,
        string name,
        List<string> parts,
        ref int clipCount)
    {
        if (clip is null)
        {
            return;
        }

        clipCount++;
        parts.Add(name);
    }

    private static void AddClipGroup<T>(
        IReadOnlyCollection<T>? clips,
        string name,
        List<string> parts,
        ref int clipCount)
    {
        if (clips is null || clips.Count == 0)
        {
            return;
        }

        clipCount += clips.Count;
        parts.Add($"{clips.Count} {name} clip{(clips.Count == 1 ? string.Empty : "s")}");
    }
}
