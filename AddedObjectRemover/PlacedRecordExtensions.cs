using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

internal static class PlacedRecordExtensions
{
    public static IEnumerable<(IPlacedGetter Record, bool Persistent)> EnumeratePlaced(this ICellGetter cell)
    {
        foreach (var record in cell.Persistent) yield return (record, true);
        foreach (var record in cell.Temporary) yield return (record, false);
    }

    public static Vector3 GetPosition(this IPlacementGetter placement) =>
        new(placement.Position.X, placement.Position.Y, placement.Position.Z);

    public static Vector3 GetRotation(this IPlacementGetter placement) =>
        new(placement.Rotation.X, placement.Rotation.Y, placement.Rotation.Z);

    public static float? GetReferenceRadius(this IPlacedGetter record) => (record as IPlacedObjectGetter)?.Radius;

    public static Vector3? GetPrimitiveBounds(this IPlacedGetter record) =>
        record is IPlacedObjectGetter { Primitive: { } primitive }
            ? new Vector3(primitive.Bounds.X, primitive.Bounds.Y, primitive.Bounds.Z)
            : null;

    public static bool IsInitiallyDisabled(this IPlacedGetter record) =>
        (record.MajorRecordFlagsRaw & (int)SkyrimMajorRecord.SkyrimMajorRecordFlag.InitiallyDisabled) != 0;

    /// <summary>Base object of any placed record type, with the kind of record the base link expects.</summary>
    public static BaseKey? GetBaseKey(this IPlacedGetter record) => record switch
    {
        IPlacedObjectGetter placedObject => ToBaseKey(placedObject.Base, BaseLinkKind.PlaceableObject),
        IPlacedNpcGetter placedNpc => ToBaseKey(placedNpc.Base, BaseLinkKind.Npc),
        IPlacedHazardGetter hazard => ToBaseKey(hazard.Hazard, BaseLinkKind.Hazard),
        IPlacedArrowGetter arrow => ToBaseKey(arrow.Projectile, BaseLinkKind.Projectile),
        IPlacedBarrierGetter barrier => ToBaseKey(barrier.Projectile, BaseLinkKind.Projectile),
        IPlacedBeamGetter beam => ToBaseKey(beam.Projectile, BaseLinkKind.Projectile),
        IPlacedConeGetter cone => ToBaseKey(cone.Projectile, BaseLinkKind.Projectile),
        IPlacedFlameGetter flame => ToBaseKey(flame.Projectile, BaseLinkKind.Projectile),
        IPlacedMissileGetter missile => ToBaseKey(missile.Projectile, BaseLinkKind.Projectile),
        IPlacedTrapGetter trap => ToBaseKey(trap.Projectile, BaseLinkKind.Projectile),
        _ => null,
    };

    private static BaseKey? ToBaseKey(IFormLinkGetter link, BaseLinkKind kind) =>
        link.IsNull ? null : new BaseKey(link.FormKey.ToRecordKey(), kind);
}
