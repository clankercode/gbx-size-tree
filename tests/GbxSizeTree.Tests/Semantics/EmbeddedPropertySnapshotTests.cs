using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using GbxSizeTree.Semantics;

namespace GbxSizeTree.Tests.Semantics;

public sealed class EmbeddedPropertySnapshotTests
{
    [Fact]
    public void PrefabMaterialChange_HasReadableOccurrencePathAndPrimitiveValues()
    {
        var changes = EmbeddedPropertySnapshot.Compare(
            EmbeddedPropertySnapshot.CaptureParsed(Item("Grass")),
            EmbeddedPropertySnapshot.CaptureParsed(Item("Plastic")));
        var change = Assert.Single(changes.Changes);
        Assert.Equal("Item > EntityModel > Prefab > Ent#2 > Solid2Model > Material#1 > Name", change.Path);
        Assert.Equal("Grass", change.Left!.Text);
        Assert.Equal("Plastic", change.Right!.Text);
        Assert.Null(changes.ContentChanged);
        Assert.Contains(changes.LeftIssues, x => x.Code == "partial-coverage");
    }

    [Fact]
    public void SharedReferences_AreVisitedAtEveryOccurrenceButCyclesStop()
    {
        var shared = new CPlugSolid2Model { CustomMaterials = [new() { MaterialName = "Grass" }] };
        var prefab = new CPlugPrefab { Ents = [new() { Model = shared }, new() { Model = shared }] };
        var snapshot = EmbeddedPropertySnapshot.CaptureParsed(prefab);
        Assert.Equal("Grass", snapshot.Values["Prefab > Ent#1 > Solid2Model > Material#1 > Name"].Text);
        Assert.Equal("Grass", snapshot.Values["Prefab > Ent#2 > Solid2Model > Material#1 > Name"].Text);
        prefab.Ents = [new() { Model = prefab }];
        Assert.Contains(EmbeddedPropertySnapshot.CaptureParsed(prefab).Issues, x => x.Code == "cycle");
    }

    [Fact]
    public void Limits_AreExplicitAndNeverTurnUnreadPropertiesIntoRemovals()
    {
        var full = EmbeddedPropertySnapshot.CaptureParsed(Item("Grass"));
        foreach (var limits in new[]
        {
            new EmbeddedPropertyLimits { MaxDepth = 1 },
            new EmbeddedPropertyLimits { MaxNodes = 2 },
            new EmbeddedPropertyLimits { MaxValueBytes = 8 },
            new EmbeddedPropertyLimits { MaxValues = 2 },
        })
        {
            var limited = EmbeddedPropertySnapshot.CaptureParsed(Item("Plastic"), limits);
            Assert.Contains(limited.Issues, x => x.Code == "limit");
            Assert.Empty(EmbeddedPropertySnapshot.Compare(full, limited).Changes);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpaqueBytes_HaveFullHashAndExplicitParseDiagnostic(bool malformedGbx)
    {
        byte[] a = malformedGbx ? [.. "GBX"u8, 1, 2, 3] : [1, 2, 3, 4];
        var b = (byte[])a.Clone();
        b[^1] ^= 1;
        var left = EmbeddedPropertySnapshot.Capture(a);
        var right = EmbeddedPropertySnapshot.Capture(b);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(a)), left.ContentHash);
        Assert.NotEmpty(left.Issues);
        var result = EmbeddedPropertySnapshot.Compare(left, right);
        Assert.True(result.ContentChanged);
        Assert.Empty(result.Changes);
        Assert.False(EmbeddedPropertySnapshot.Compare(left, left).ContentChanged);
        Assert.NotEmpty(EmbeddedPropertySnapshot.Compare(left, left).LeftIssues);
    }

    [Fact]
    public void InputLimit_DoesNotReturnPrefixHashOrReadWithoutBound()
    {
        var limits = new EmbeddedPropertyLimits { MaxInputBytes = 3 };
        var snapshot = EmbeddedPropertySnapshot.Capture(new byte[] { 1, 2, 3, 4 }, limits);
        Assert.Null(snapshot.ContentHash);
        Assert.Contains(snapshot.Issues, x => x.Code == "limit");
        using var stream = new MemoryStream([1, 2, 3, 4, 5, 6]);
        snapshot = EmbeddedPropertySnapshot.Capture(stream, limits);
        Assert.Null(snapshot.ContentHash);
        Assert.InRange(stream.Position, 3, 4);
        Assert.Null(EmbeddedPropertySnapshot.Compare(snapshot, snapshot).ContentChanged);
    }

    [Fact]
    public void AddedMaterial_IsDifferentFromAnUnreadableMaterial()
    {
        var left = new CPlugSolid2Model { CustomMaterials = [] };
        var right = new CPlugSolid2Model { CustomMaterials = [new() { MaterialName = "Grass" }] };
        var changes = EmbeddedPropertySnapshot.Compare(
            EmbeddedPropertySnapshot.CaptureParsed(left), EmbeddedPropertySnapshot.CaptureParsed(right)).Changes;
        Assert.Contains(changes, x => x.Path.EndsWith("Material#1 > Name") && x.Left is null && x.Right!.Text == "Grass");
    }

    [Fact]
    public void MaterialPhysicsAndRuntimeMesh_AreTypedAndJsonSourceGeneratable()
    {
        var a = new CPlugMaterialUserInst { SurfacePhysicId = CPlugSurface.MaterialId.Grass, IsNatural = false };
        var b = new CPlugMaterialUserInst { SurfacePhysicId = CPlugSurface.MaterialId.Plastic, IsNatural = true };
        var result = EmbeddedPropertySnapshot.Compare(
            EmbeddedPropertySnapshot.CaptureParsed(RuntimeItem(a)), EmbeddedPropertySnapshot.CaptureParsed(RuntimeItem(b)));
        Assert.Contains(result.Changes, x => x.Path.EndsWith("SurfacePhysics") && x.Left!.Text == "Grass" && x.Right!.Text == "Plastic");
        Assert.Contains(result.Changes, x => x.Path.EndsWith("IsNatural") && x.Left!.Boolean == false && x.Right!.Boolean == true);
        var json = JsonSerializer.Serialize(result, EmbeddedPropertyTestJsonContext.Default.EmbeddedPropertyDiff);
        Assert.Contains("Plastic", json);
    }

    [Fact]
    public void ZipEntries_AreIndependentEvenWithRepeatedNames()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var bytes in new byte[][] { [1, 2, 3], [1, 2, 4] })
            {
                using var entry = zip.CreateEntry("same.Item.Gbx").Open();
                entry.Write(bytes);
            }
        stream.Position = 0;
        using var read = new ZipArchive(stream, ZipArchiveMode.Read);
        var result = EmbeddedPropertySnapshot.Compare(
            EmbeddedPropertySnapshot.Capture(read.Entries[0]), EmbeddedPropertySnapshot.Capture(read.Entries[1]));
        Assert.True(result.ContentChanged);
    }

    private static CGameItemModel Item(string name) => new()
    {
        EntityModel = new CPlugPrefab
        {
            Ents = [new() { Model = new CPlugSolid2Model() }, new()
            {
                Model = new CPlugSolid2Model { CustomMaterials = [new() { MaterialName = name }] }
            }]
        }
    };

    private static CGameItemModel RuntimeItem(CPlugMaterialUserInst material) => new()
    {
        EntityModel = new CGameCommonItemEntityModel
        {
            StaticObject = new CPlugStaticObjectModel
            {
                Mesh = new CPlugSolid2Model { MaterialInsts = [material] }
            }
        }
    };
}

[JsonSerializable(typeof(EmbeddedPropertyDiff))]
internal partial class EmbeddedPropertyTestJsonContext : JsonSerializerContext;
