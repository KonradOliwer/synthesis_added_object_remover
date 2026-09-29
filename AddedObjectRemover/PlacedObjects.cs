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
/// <param name="CellName">Null for an interior, whose cell is the space itself.</param>
/// <param name="Rotation">The placement's Euler angles in radians, as the record stores them.</param>
/// <param name="HasMapMarker">The placed reference itself carries map marker data (XMRK).</param>
/// <param name="OwnReach">How far the reference itself says it acts (see <see cref="InvisibleObjectReach"/>); null when it does not say.</param>
internal sealed record TargetObject(
    TargetId Id,
    FormKey Key,
    string? EditorId,
    FormKey SpaceKey,
    string? CellName,
    PlacedTransform Transform,
    P3Float Rotation,
    BaseRef? Base,
    bool IsTeleportDoor,
    bool IsPrimitive,
    bool HasMapMarker,
    float? OwnReach);

/// <summary>Where a target object's override is written: through its cell's winning context, into the same child list.</summary>
internal sealed record TargetLocation(
    IModContext<ISkyrimMod, ISkyrimModGetter, ICell, ICellGetter> WinningCell,
    bool InPersistentList);

/// <summary>Compact snapshot of another mod's placed object (winning version).</summary>
/// <param name="HasMapMarker">The placed reference itself carries map marker data (XMRK), e.g. a fast-travel marker.</param>
internal readonly record struct OtherObject(
    OtherId Id,
    FormKey FormKey,
    FormKey SpaceKey,
    ModKey WinningMod,
    string? EditorId,
    BaseRef? Base,
    Vector3 Position,
    P3Float Rotation,
    float Scale,
    bool IsPrimitive,
    bool HasMapMarker)
{
    public PlacedTransform Transform => new(Position, Geometry.RotationFromEuler(Rotation), Scale);

    /// <summary>A placed NPC or creature (ACHR), the only placed type whose base link is an NPC.</summary>
    public bool IsPlacedNpc => Base is { LinkType: var linkType } && linkType == typeof(INpcGetter);
}

/// <summary>A too-close target (index into the scanned targets) and the first other object found.</summary>
internal readonly record struct TooCloseHit(int TargetIndex, OtherObject TooCloseTo);

/// <summary>A target object to remove (index into the scanned targets).</summary>
internal abstract record Removal(int TargetIndex);

internal sealed record TooCloseRemoval(int TargetIndex, OtherObject TooCloseTo) : Removal(TargetIndex);

internal sealed record TouchingRemoval(int TargetIndex, int TouchedTargetIndex) : Removal(TargetIndex);

/// <param name="RemovedShare">Fraction of the object's support held by removed objects.</param>
/// <param name="MainRemovedSupporter">The removed target holding the largest share of its support.</param>
internal sealed record AnchoringRemoval(int TargetIndex, float RemovedShare, int MainRemovedSupporter) : Removal(TargetIndex);

/// <summary>An invisible target object inside another mod's object or whose surrounding visible target objects were removed.</summary>
internal sealed record LeftoverRemoval(int TargetIndex, LeftoverEvaluation Evaluation) : Removal(TargetIndex);

/// <summary>A member of the linked group of a removed target object.</summary>
/// <param name="LinkedToTargetIndex">The removed member whose removal took the group along.</param>
internal sealed record LinkedRemoval(int TargetIndex, int LinkedToTargetIndex) : Removal(TargetIndex);

/// <summary>A target object that would be removed but stays because it, or a member of its linked group, is referenced.</summary>
/// <param name="TouchedTargetIndex">The removed target it touches or rests on most, if any.</param>
internal sealed record KeptTarget(int TargetIndex, KeepReason Reason, int? TouchedTargetIndex);

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
