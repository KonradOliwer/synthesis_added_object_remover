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

    /// <returns>The layer number of the reference's collision layer (XTRI, numbered as the COLL records' index); null when it stores none.</returns>
    public static uint? GetCollisionLayer(this IPlacedGetter record) => (record as IPlacedObjectGetter)?.CollisionLayer;

    public static bool IsCullingVolume(this IPlacedGetter record) =>
        record is IPlacedObjectGetter placed
        && (placed.IsMultiBoundPrimitive || placed.LinkedRooms is { Count: > 0 } || placed.Portals is { Count: > 0 } || placed.RoomPortal is not null);

    /// <summary>
    /// Every field of a placed object but the record header, flags, EditorID, base, placement and scale. Turned on by default, so a
    /// field a later Mutagen version adds is compared too.
    /// </summary>
    private static readonly PlacedObject.TranslationMask EverythingButPlacement = new(defaultOn: true)
    {
        FormKey = false,
        VersionControl = false,
        FormVersion = false,
        Version2 = false,
        MajorRecordFlagsRaw = false,
        SkyrimMajorRecordFlags = false,
        EditorID = false,
        Base = false,
        Placement = false,
        Scale = false,
    };

    /// <summary>A placed object that holds no field; only read, so every reader shares it.</summary>
    /// <remarks>The release sets only the record header's form version, which <see cref="EverythingButPlacement"/> leaves out.</remarks>
    private static readonly IPlacedObjectGetter Empty = new PlacedObject(FormKey.Null, SkyrimRelease.SkyrimSE);

    /// <summary>Compares the record with a placed object that holds no field, on every field but those it may hold.</summary>
    public static bool HoldsOnlyPlacement(this IPlacedGetter record) =>
        record is IPlacedObjectGetter placed && placed.Equals(Empty, EverythingButPlacement);

    /// <remarks>The bit means Visible When Distant for statics, trees, containers and items; no plain-scenery base uses it otherwise.</remarks>
    public static bool IsVisibleWhenDistant(this IPlacedGetter record) =>
        (record.MajorRecordFlagsRaw & (int)PlacedObject.StaticMajorFlag.VisibleWhenDistant) != 0;

    /// <returns>Null also when the enable parent's reference is null: the game then has no parent to follow.</returns>
    public static IEnableParentGetter? GetEnableParent(this IPlacedGetter record) =>
        record.EnableParent is { Reference.IsNull: false } parent ? parent : null;

    public static PresenceFacts GetPresence(this IPlacedGetter record, PluginNames names) => new(
        record.IsDeleted,
        record.IsInitiallyDisabled(),
        record.GetEnableParent() is { } parent
            ? new EnableParentFact(names.KeyOf(parent.Reference.FormKey), parent.Flags.HasFlag(EnableParent.Flag.SetEnableStateToOppositeOfParent))
            : null);

    public static bool IsInitiallyDisabled(this IPlacedGetter record) =>
        (record.MajorRecordFlagsRaw & (int)SkyrimMajorRecord.SkyrimMajorRecordFlag.InitiallyDisabled) != 0;

    /// <remarks>Every placed record type (REFR, ACHR and the placed traps) uses this same bit for Persistent.</remarks>
    public static bool IsPersistent(this IPlacedGetter record) =>
        (record.MajorRecordFlagsRaw & (int)PlacedObject.DefaultMajorFlag.Persistent) != 0;

    public static void SetPersistent(this IPlaced record, bool persistent)
    {
        if (persistent) record.MajorRecordFlagsRaw |= (int)PlacedObject.DefaultMajorFlag.Persistent;
        else record.MajorRecordFlagsRaw &= ~(int)PlacedObject.DefaultMajorFlag.Persistent;
    }

    /// <summary>Base object of any placed record type, with the kind of record the base link expects.</summary>
    public static BaseKey? GetBaseKey(this IPlacedGetter record, PluginNames names) => record switch
    {
        IPlacedObjectGetter placedObject => ToBaseKey(placedObject.Base, BaseLinkKind.PlaceableObject, names),
        IPlacedNpcGetter placedNpc => ToBaseKey(placedNpc.Base, BaseLinkKind.Npc, names),
        IPlacedHazardGetter hazard => ToBaseKey(hazard.Hazard, BaseLinkKind.Hazard, names),
        IPlacedArrowGetter arrow => ToBaseKey(arrow.Projectile, BaseLinkKind.Projectile, names),
        IPlacedBarrierGetter barrier => ToBaseKey(barrier.Projectile, BaseLinkKind.Projectile, names),
        IPlacedBeamGetter beam => ToBaseKey(beam.Projectile, BaseLinkKind.Projectile, names),
        IPlacedConeGetter cone => ToBaseKey(cone.Projectile, BaseLinkKind.Projectile, names),
        IPlacedFlameGetter flame => ToBaseKey(flame.Projectile, BaseLinkKind.Projectile, names),
        IPlacedMissileGetter missile => ToBaseKey(missile.Projectile, BaseLinkKind.Projectile, names),
        IPlacedTrapGetter trap => ToBaseKey(trap.Projectile, BaseLinkKind.Projectile, names),
        _ => null,
    };

    private static BaseKey? ToBaseKey(IFormLinkGetter link, BaseLinkKind kind, PluginNames names) =>
        link.IsNull ? null : new BaseKey(names.KeyOf(link.FormKey), kind);
}
