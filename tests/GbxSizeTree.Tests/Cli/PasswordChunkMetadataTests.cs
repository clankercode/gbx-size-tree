using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.Serialization.Chunking;
using GbxSizeTree.Cli.Modes;
using GbxSizeTree.Container;
using Spectre.Console.Testing;

namespace GbxSizeTree.Tests.Cli;

public sealed class PasswordChunkMetadataTests
{
    [Fact]
    public void Capture_ChunkPresenceIsSeparateFromNullEmptyAndNonemptyPlaintext()
    {
        foreach (var password in new string?[] { null, "", "secret" })
        {
            var map = new CGameCtnChallenge { Password = password };
            var absent = MapMetadataSnapshot.Capture(map);
            map.Chunks.Create<CGameCtnChallenge.Chunk03043029>();
            var present = MapMetadataSnapshot.Capture(map);
            Assert.Null(map.HashedPassword);
            var change = Assert.Single(MapMetadataSnapshot.Compare(absent, present));
            Assert.Equal("security.passwordChunkPresent", change.Path);
            Assert.Equal(new MapMetadataValue(Boolean: false), change.Left);
            Assert.Equal(new MapMetadataValue(Boolean: true), change.Right);
            Assert.Equal(new MapMetadataValue(Boolean: password is not null), present.Values["security.passwordPresent"]);
        }
    }

    [Fact]
    public void Capture_OpaquePasswordChunkHasKnownPresenceButUnknownHash()
    {
        var map = new CGameCtnChallenge();
        ((ISkippableChunk)map.Chunks.Create<CGameCtnChallenge.Chunk03043029>()).Data = [255];
        var values = MapMetadataSnapshot.Capture(map).Values;
        Assert.Equal(new MapMetadataValue(Boolean: true), values["security.passwordChunkPresent"]);
        Assert.Null(values["security.hashedPasswordPresent"]);
        Assert.Equal("unavailable: opaque chunk 03043029", values["security.hashedPasswordPresent/status"]!.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompareFiles_ActualSerializedChunkRemoval_DefaultAndAll(bool all)
    {
        Gbx.LZO = new GBX.NET.LZO.Lzo();
        var map = new CGameCtnChallenge { HashedPassword = Checksum128.Zero };
        map.Chunks.Create<CGameCtnChallenge.Chunk03043029>();
        var gbx = new Gbx<CGameCtnChallenge>(map);
        var left = Path.GetTempFileName();
        var right = Path.GetTempFileName();
        try
        {
            gbx.Save(left);
            var parsed = Gbx.Parse<CGameCtnChallenge>(left);
            Assert.Null(parsed.Node.Password);
            Assert.Equal(Checksum128.Zero, parsed.Node.HashedPassword);
            Assert.True(parsed.Node.Chunks.Remove(0x03043029));
            parsed.Save(right);
            Assert.DoesNotContain(Gbx.Parse<CGameCtnChallenge>(right).Node.Chunks, c => c.Id == 0x03043029);
            var bodyBefore = DecompressedBody.GetBody(File.ReadAllBytes(left));
            var bodyAfter = DecompressedBody.GetBody(File.ReadAllBytes(right));
            Assert.Contains(new SkippableChunkScanner().Scan(bodyBefore).Regions, r => r.ChunkId == 0x03043029);
            Assert.DoesNotContain(new SkippableChunkScanner().Scan(bodyAfter).Regions, r => r.ChunkId == 0x03043029);
            var report = DiffMode.CompareFiles(left, right, all);
            var change = Assert.Single(report.MetadataChanges);
            Assert.Equal("security.passwordChunkPresent", change.Path);
            Assert.True(change.Left!.Boolean);
            Assert.False(change.Right!.Boolean);
            Assert.Null(report.Password);
            Assert.Equal(new MapMetadataChange(change.Path, change.Right, change.Left), Assert.Single(DiffMode.CompareFiles(right, left, all).MetadataChanges));
            Assert.Empty(DiffMode.CompareFiles(left, left, all).MetadataChanges);
            if (all) Assert.Contains(report.Chunks, c => c.Key == "body:03043029" && c.Right is null);
            else Assert.Empty(report.Chunks);
            using var json = JsonDocument.Parse(DiffMode.RenderJson(report));
            var row = Assert.Single(json.RootElement.GetProperty("MetadataChanges").EnumerateArray());
            Assert.True(row.GetProperty("Left").GetProperty("Boolean").GetBoolean());
            Assert.False(row.GetProperty("Right").GetProperty("Boolean").GetBoolean());
            Assert.Contains(change.Path, DiffRenderer.RenderHtml(report, "old", "new"));
            Assert.Contains(change.Path, DiffRenderer.RenderMarkdown(report, "old", "new"));
            var console = new TestConsole();
            DiffRenderer.Render(console, report, "old", "new");
            Assert.Contains(change.Path, console.Output);
            if (Environment.GetEnvironmentVariable("GBX_METADATA_ARTIFACT_DIR") is { Length: > 0 } dir)
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, $"password-{all}.html"), DiffRenderer.RenderHtml(report, "old", "new"));
            }
        }
        finally { File.Delete(left); File.Delete(right); }
    }

    [Fact]
    public void RemovePassword_ClearsProtectionAndRemovesChunk()
    {
        var map = new CGameCtnChallenge { HashedPassword = new Checksum128(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }) };
        map.Chunks.Create<CGameCtnChallenge.Chunk03043029>();
        var before = MapMetadataSnapshot.Capture(map);
        map.RemovePassword();
        var after = MapMetadataSnapshot.Capture(map);
        Assert.Equal(new MapMetadataValue(Boolean: false), after.Values["security.passwordChunkPresent"]);
        Assert.Equal(new[] { "security.hashedPasswordPresent", "security.passwordChunkPresent" },
            MapMetadataSnapshot.Compare(before, after).Select(c => c.Path));
    }

    [Fact]
    public void OpaqueFileFallback_DoesNotInventPasswordChunkAbsence()
    {
        Gbx.LZO = new GBX.NET.LZO.Lzo();
        var map = new CGameCtnChallenge();
        ((ISkippableChunk)map.Chunks.Create<CGameCtnChallenge.Chunk03043029>()).Data = [255];
        var left = Path.GetTempFileName();
        var right = Path.GetTempFileName();
        try
        {
            new Gbx<CGameCtnChallenge>(map).Save(left);
            new Gbx<CGameCtnChallenge>(new()).Save(right);
            Assert.ThrowsAny<Exception>(() => DiffMode.CompareFiles(left, right));
            var report = DiffMode.CompareFiles(left, right, all: true);
            Assert.Null(report.Password);
            Assert.Empty(report.MetadataChanges);
            Assert.Contains(report.Warnings, w => w.Contains("semantic fields unavailable"));
            Assert.Contains(report.Chunks, c => c.Key == "body:03043029" && c.Right is null);
        }
        finally { File.Delete(left); File.Delete(right); }
    }
}
