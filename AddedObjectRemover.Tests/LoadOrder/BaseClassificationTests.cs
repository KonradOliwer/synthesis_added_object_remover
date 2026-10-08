using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.LoadOrder;

public class BaseClassificationTests
{
    private static readonly ModKey Mod = ModKey.FromNameAndExtension("Bases.esp");
    private static readonly FormKey TriggerBase = new(Mod, 0x801);
    private static readonly FormKey FlaggedMarkerBase = new(Mod, 0x802);
    private static readonly FormKey AcousticSpaceBase = new(Mod, 0x803);

    [Fact]
    public void PrimitiveOfABaseWithoutGeometryIsATriggerBox()
    {
        var shapes = CreateShapes();
        var reference = new BaseKey(TriggerBase.ToRecordKey(), BaseLinkKind.PlaceableObject);

        Assert.Equal(InvisibleObjectKind.TriggerBoxes, shapes.VisibilityOf(reference, isPrimitive: true, hasMapMarker: false).Kind);
        Assert.Equal(InvisibleObjectKind.OtherMarkers, shapes.VisibilityOf(reference, isPrimitive: false, hasMapMarker: false).Kind);
    }

    [Fact]
    public void PrimitiveKeepsTheKindOfItsMarkerFlagOrRecordType()
    {
        var shapes = CreateShapes();

        var flagged = shapes.VisibilityOf(new BaseKey(FlaggedMarkerBase.ToRecordKey(), BaseLinkKind.PlaceableObject), isPrimitive: true, hasMapMarker: false);
        var acoustic = shapes.VisibilityOf(new BaseKey(AcousticSpaceBase.ToRecordKey(), BaseLinkKind.PlaceableObject), isPrimitive: true, hasMapMarker: false);

        Assert.Equal(InvisibleObjectKind.OtherMarkers, flagged.Kind);
        Assert.Equal(InvisibleObjectKind.AcousticSpaces, acoustic.Kind);
    }

    [Theory]
    [InlineData("CritterSpawnFish", true)]
    [InlineData("crittersPAWNbird", true)]
    [InlineData("OtherScript", false)]
    public void OnlyActivatorsRunningACritterSpawnScriptAreCritterSpawners(string script, bool expected)
    {
        var activator = Facts(0x806, BaseRecordKind.Activator) with { ScriptNames = [script] };

        var shape = BaseObjectRules.ClassifyShape(activator, Box.Zero, meshPath: null, hasModel: false, meshWithoutGeometry: false);

        Assert.Equal(expected ? InvisibleObjectKind.CritterSpawners : InvisibleObjectKind.OtherMarkers, shape.InvisibleKind);
    }

    [Fact]
    public void PrimitiveOfACritterSpawnerStaysACritterSpawner()
    {
        var spawner = Facts(0x806, BaseRecordKind.Activator) with { ScriptNames = ["CritterSpawnFish"] };

        var shape = BaseObjectRules.ClassifyShape(spawner, Box.Zero, meshPath: null, hasModel: false, meshWithoutGeometry: false);

        Assert.Equal(InvisibleObjectKind.CritterSpawners, shape.InvisibleKind);
        Assert.False(shape.InvisibleForLackOfGeometry);
    }

    [Fact]
    public void LightWhoseMeshHasNoRenderGeometryIsALight()
    {
        var light = Facts(0x804, BaseRecordKind.Light);

        var shape = BaseObjectRules.ClassifyShape(light, Box.Zero, meshPath: null, hasModel: true, meshWithoutGeometry: true);

        Assert.Equal(InvisibleObjectKind.Lights, shape.InvisibleKind);
        Assert.False(shape.InvisibleForLackOfGeometry);
    }

    [Fact]
    public void LightWithAVisibleMeshIsVisible()
    {
        var light = Facts(0x805, BaseRecordKind.Light);

        var shape = BaseObjectRules.ClassifyShape(light, new Box(Vector3.Zero, Vector3.One), "meshes\\lamp.nif", hasModel: true, meshWithoutGeometry: false);

        Assert.Null(shape.InvisibleKind);
    }

    private static BaseFacts Facts(uint id, BaseRecordKind kind) =>
        new(new FormKey(Mod, id).ToRecordKey(), Resolved: true, kind, kind.ToString(), null, null, null, null, ImmutableArray<string>.Empty, null, null);

    private static IBaseObjectShapes CreateShapes()
    {
        var mod = new SkyrimMod(Mod, SkyrimRelease.SkyrimSE);
        mod.Activators.Add(new Mutagen.Bethesda.Skyrim.Activator(TriggerBase, SkyrimRelease.SkyrimSE));
        mod.Activators.Add(new Mutagen.Bethesda.Skyrim.Activator(FlaggedMarkerBase, SkyrimRelease.SkyrimSE)
        {
            MajorFlags = Mutagen.Bethesda.Skyrim.Activator.MajorFlag.IsMarker,
        });
        mod.AcousticSpaces.Add(new AcousticSpace(AcousticSpaceBase, SkyrimRelease.SkyrimSE));

        return TestShapes.Catalog(new BaseFactsReader(mod.ToImmutableLinkCache()), Path.GetTempPath(), Mod);
    }
}
