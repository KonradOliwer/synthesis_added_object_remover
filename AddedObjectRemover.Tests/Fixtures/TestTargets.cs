using System.Numerics;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Target objects backed by in-memory Mutagen records.</summary>
internal static class TestTargets
{
    public static readonly ModKey TargetMod = ModKey.FromNameAndExtension("Target.esp");
    public static readonly FormKey Space = new(TargetMod, 0x100);

    public static FormKey Key(int index) => new(TargetMod, 0x800 + (uint)index);

    public static PlacedObject Record(int index) => new(Key(index), SkyrimRelease.SkyrimSE);

    public static TargetObject Create(int index, PlacedTransform transform, bool isTeleportDoor = false) =>
        Create(Record(index), transform, isTeleportDoor);

    public static TargetObject Create(IPlacedGetter record, PlacedTransform transform, bool isTeleportDoor = false) =>
        new(record, Space, CellName: null, transform, Base: null, isTeleportDoor, IsPrimitive: false, HasMapMarker: false);

    public static List<TargetObject> CreateMany(int count) =>
        Enumerable.Range(0, count).Select(index => Create(index, At(Vector3.Zero))).ToList();

    public static PlacedTransform At(Vector3 position, float zRadians = 0f, float scale = 1f) =>
        new(position, AddedObjectRemover.Geometry.RotationFromEuler(new P3Float(0, 0, zRadians)), scale);
}
