using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover;

/// <summary>
/// A placed record's base object: its FormKey plus the record type of the placed record's base
/// link (e.g. IPlaceableObjectGetter for REFR, INpcGetter for ACHR), used for narrowly typed
/// link-cache lookups.
/// </summary>
internal readonly record struct BaseRef(FormKey FormKey, Type LinkType);

/// <summary>Target-plugin object that may be removed.</summary>
internal sealed record TargetObject(
    IPlacedGetter Record,
    FormKey SpaceKey,
    string? CellName,
    PlacedTransform Transform,
    BaseRef? Base,
    bool IsTeleportDoor);

/// <summary>Where a target object's override is written: through its cell's winning context, into the same child list.</summary>
internal sealed record TargetLocation(
    IModContext<ISkyrimMod, ISkyrimModGetter, ICell, ICellGetter> WinningCell,
    bool InPersistentList);

/// <summary>Compact snapshot of another mod's placed object (winning version).</summary>
internal readonly record struct OtherObject(
    FormKey FormKey,
    ModKey WinningMod,
    string? EditorId,
    BaseRef? Base,
    Vector3 Position,
    P3Float Rotation,
    float Scale,
    bool IsPrimitive);

/// <summary>A too-close target (index into the scanned targets) and the first other object found.</summary>
internal readonly record struct TooCloseHit(int TargetIndex, OtherObject TooCloseTo);

/// <summary>A target object to remove (index into the scanned targets).</summary>
internal abstract record Removal(int TargetIndex);

internal sealed record TooCloseRemoval(int TargetIndex, OtherObject TooCloseTo) : Removal(TargetIndex);

internal sealed record TouchingRemoval(int TargetIndex, int TouchedTargetIndex) : Removal(TargetIndex);

/// <summary>A target object that would be removed but stays because it is referenced.</summary>
/// <param name="TouchedTargetIndex">The removed target it touches, when it was reached by the touch test.</param>
internal sealed record KeptTarget(int TargetIndex, string Reason, int? TouchedTargetIndex);

internal static class PlacedRecordExtensions
{
    public static IEnumerable<(IPlacedGetter Record, bool Persistent)> EnumeratePlaced(this ICellGetter cell)
    {
        foreach (var record in cell.Persistent) yield return (record, true);
        foreach (var record in cell.Temporary) yield return (record, false);
    }

    public static bool IsInitiallyDisabled(this IPlacedGetter record) =>
        (record.MajorRecordFlagsRaw & (int)SkyrimMajorRecord.SkyrimMajorRecordFlag.InitiallyDisabled) != 0;

    /// <summary>Base object of any placed record type, with the base link's own record type.</summary>
    public static BaseRef? GetBaseRef(this IPlacedGetter record) => record switch
    {
        IPlacedObjectGetter placedObject => ToBaseRef(placedObject.Base),
        IPlacedNpcGetter placedNpc => ToBaseRef(placedNpc.Base),
        IPlacedHazardGetter hazard => ToBaseRef(hazard.Hazard),
        IPlacedArrowGetter arrow => ToBaseRef(arrow.Projectile),
        IPlacedBarrierGetter barrier => ToBaseRef(barrier.Projectile),
        IPlacedBeamGetter beam => ToBaseRef(beam.Projectile),
        IPlacedConeGetter cone => ToBaseRef(cone.Projectile),
        IPlacedFlameGetter flame => ToBaseRef(flame.Projectile),
        IPlacedMissileGetter missile => ToBaseRef(missile.Projectile),
        IPlacedTrapGetter trap => ToBaseRef(trap.Projectile),
        _ => null,
    };

    private static BaseRef? ToBaseRef<TGetter>(IFormLinkGetter<TGetter> link)
        where TGetter : class, IMajorRecordGetter =>
        link.IsNull ? null : new BaseRef(link.FormKey, typeof(TGetter));
}
