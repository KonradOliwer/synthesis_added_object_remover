using System.Numerics;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

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
        var reference = new BaseRef(TriggerBase, typeof(IActivatorGetter));

        Assert.Equal(InvisibleObjectKind.TriggerBoxes, shapes.GetVisibility(reference, isPrimitive: true, hasMapMarker: false).Kind);
        Assert.Equal(InvisibleObjectKind.OtherMarkers, shapes.GetVisibility(reference, isPrimitive: false, hasMapMarker: false).Kind);
    }

    [Fact]
    public void PrimitiveKeepsTheKindOfItsMarkerFlagOrRecordType()
    {
        var shapes = CreateShapes();

        var flagged = shapes.GetVisibility(new BaseRef(FlaggedMarkerBase, typeof(IActivatorGetter)), isPrimitive: true, hasMapMarker: false);
        var acoustic = shapes.GetVisibility(new BaseRef(AcousticSpaceBase, typeof(IAcousticSpaceGetter)), isPrimitive: true, hasMapMarker: false);

        Assert.Equal(InvisibleObjectKind.OtherMarkers, flagged.Kind);
        Assert.Equal(InvisibleObjectKind.AcousticSpaces, acoustic.Kind);
    }

    [Fact]
    public void PrimitiveOfACritterSpawnerStaysACritterSpawner()
    {
        var spawner = new Mutagen.Bethesda.Skyrim.Activator(new FormKey(Mod, 0x806), SkyrimRelease.SkyrimSE)
        {
            VirtualMachineAdapter = new VirtualMachineAdapter { Scripts = { new ScriptEntry { Name = "CritterSpawnFish" } } },
        };

        var shape = BaseObjectShapeProvider.ClassifyShape(spawner, Box.Zero, meshPath: null, hasModel: false, meshWithoutGeometry: false);

        Assert.Equal(InvisibleObjectKind.CritterSpawners, shape.InvisibleKind);
        Assert.False(shape.InvisibleForLackOfGeometry);
    }

    [Fact]
    public void LightWhoseMeshHasNoRenderGeometryIsALight()
    {
        var light = new Light(new FormKey(Mod, 0x804), SkyrimRelease.SkyrimSE);

        var shape = BaseObjectShapeProvider.ClassifyShape(light, Box.Zero, meshPath: null, hasModel: true, meshWithoutGeometry: true);

        Assert.Equal(InvisibleObjectKind.Lights, shape.InvisibleKind);
        Assert.False(shape.InvisibleForLackOfGeometry);
    }

    [Fact]
    public void LightWithAVisibleMeshIsVisible()
    {
        var light = new Light(new FormKey(Mod, 0x805), SkyrimRelease.SkyrimSE);

        var shape = BaseObjectShapeProvider.ClassifyShape(light, new Box(Vector3.Zero, Vector3.One), "meshes\\lamp.nif", hasModel: true, meshWithoutGeometry: false);

        Assert.Null(shape.InvisibleKind);
    }

    private static BaseObjectShapeProvider CreateShapes()
    {
        var mod = new SkyrimMod(Mod, SkyrimRelease.SkyrimSE);
        mod.Activators.Add(new Mutagen.Bethesda.Skyrim.Activator(TriggerBase, SkyrimRelease.SkyrimSE));
        mod.Activators.Add(new Mutagen.Bethesda.Skyrim.Activator(FlaggedMarkerBase, SkyrimRelease.SkyrimSE)
        {
            MajorFlags = Mutagen.Bethesda.Skyrim.Activator.MajorFlag.IsMarker,
        });
        mod.AcousticSpaces.Add(new AcousticSpace(AcousticSpaceBase, SkyrimRelease.SkyrimSE));

        var messages = new MeshMessageLog(enabled: false);
        var meshFiles = new MeshFileSource(Path.GetTempPath(), GameRelease.SkyrimSE, [Mod], messages);
        return new BaseObjectShapeProvider(mod.ToImmutableLinkCache(), meshFiles, messages);
    }
}
