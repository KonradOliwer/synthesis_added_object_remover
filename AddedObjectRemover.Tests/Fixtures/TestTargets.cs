using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Noggog;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Target objects with FormKeys in one target plugin.</summary>
internal static class TestTargets
{
    public static readonly ModKey TargetMod = ModKey.FromNameAndExtension("Target.esp");
    public static readonly FormKey Space = new(TargetMod, 0x100);

    public static FormKey Key(int index) => new(TargetMod, 0x800 + (uint)index);

    public static TargetLink Link(int from, int to) => new(new TargetId(from), new TargetId(to));

    /// <summary>The keep reasons of <paramref name="count"/> targets, by target index; null where nothing depends on the target.</summary>
    public static KeepReason?[] References(int count, IReadOnlyDictionary<int, KeepReason>? reasons = null) =>
        Enumerable.Range(0, count).Select(index => reasons?.GetValueOrDefault(index)).ToArray();

    public static TargetObject Create(int index, PlacedTransform transform, bool isTeleportDoor = false) =>
        Create(index, transform, baseRef: null, Space, isTeleportDoor);

    public static TargetObject Create(int index, PlacedTransform transform, BaseRef? baseRef, FormKey space, bool isTeleportDoor = false) =>
        new(
            new TargetId(index),
            Key(index),
            EditorId: null,
            space,
            CellName: null,
            transform,
            Rotation: default,
            baseRef,
            isTeleportDoor,
            IsPrimitive: false,
            HasMapMarker: false,
            OwnReach: null);

    public static List<TargetObject> CreateMany(int count) =>
        Enumerable.Range(0, count).Select(index => Create(index, At(Vector3.Zero))).ToList();

    public static PlacedTransform At(Vector3 position, float zRadians = 0f, float scale = 1f) =>
        new(position, AddedObjectRemover.Geometry.RotationFromEuler(new P3Float(0, 0, zRadians)), scale);
}
