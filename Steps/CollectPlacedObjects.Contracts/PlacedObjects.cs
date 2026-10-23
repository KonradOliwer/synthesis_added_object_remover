using System.Numerics;

namespace AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

/// <summary>Target-plugin object that may be removed.</summary>
/// <param name="Cell">Null for an interior, whose cell is the space itself.</param>
/// <param name="Rotation">The placement's Euler angles in radians, as the record stores them.</param>
/// <param name="HasMapMarker">The placed reference itself carries map marker data (XMRK).</param>
/// <param name="IsPersistent">The reference's own Persistent flag is set.</param>
/// <param name="HasEnableParent">The winning version has an enable parent whose reference is not empty.</param>
/// <param name="NamedByScript">The first quest or scene that a script on the reference itself names; null when none does.</param>
/// <param name="HasLocationRefType">The reference itself has at least one location ref type (XLRT).</param>
/// <param name="ReferenceRadius">The reference's own radius (XRDS) as the record stores it; null when it has none.</param>
/// <param name="PrimitiveBounds">The reference's primitive box, full sizes as the record exposes them; null when it is not a primitive.</param>
/// <param name="Area">Where it is below its space, named from the winning cells.</param>
public sealed record TargetObject(
    TargetId Id,
    RecordKey Key,
    string? EditorId,
    RecordKey SpaceKey,
    CellFact? Cell,
    PlacedTransform Transform,
    Vector3 Rotation,
    BaseKey? Base,
    bool IsTeleportDoor,
    bool IsPrimitive,
    bool HasMapMarker,
    bool IsPersistent,
    bool HasEnableParent,
    ScriptNamedRecord? NamedByScript,
    bool HasLocationRefType,
    float? ReferenceRadius,
    Vector3? PrimitiveBounds,
    TargetArea Area);

/// <summary>How the game shows another mod's object differently from its accepted values.</summary>
/// <param name="ChangingMod">
/// The plugin of the first version above the accepted one that holds the value the game shows; null when only the patch holds it,
/// so an earlier patcher in this group made the change.
/// </param>
/// <param name="AcceptedFrom">
/// The plugin of the accepted version; null when no version is trusted for the value, so the object is new to the world the
/// cleaned mod was made for.
/// </param>
public readonly record struct ObjectChange(PluginName? ChangingMod, PluginName? AcceptedFrom);

/// <summary>Compact snapshot of another mod's placed object (winning version).</summary>
/// <param name="HasMapMarker">The placed reference itself carries map marker data (XMRK), e.g. a fast-travel marker.</param>
/// <param name="Change">Why it can cause removals; null for a support-only object.</param>
public readonly record struct OtherObject(
    OtherId Id,
    RecordKey Key,
    RecordKey SpaceKey,
    PluginName WinningMod,
    string? EditorId,
    BaseKey? Base,
    Vector3 Position,
    Vector3 Rotation,
    float Scale,
    bool IsPrimitive,
    bool HasMapMarker,
    ObjectChange? Change)
{
    public PlacedTransform Transform => new(Position, Mat3.FromEuler(Rotation), Scale);

    /// <summary>A placed NPC or creature (ACHR), the only placed type whose base link is an NPC.</summary>
    public bool IsPlacedNpc => Base is { Kind: BaseLinkKind.Npc };
}
