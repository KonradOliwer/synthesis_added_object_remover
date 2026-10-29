using System.Numerics;

namespace AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

/// <summary>A placed object the run judges and may remove: an object of the target plugin, or one of another plugin that the target plugin changed.</summary>
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
/// <param name="Origin">Where the object comes from.</param>
/// <param name="EarlierVersion">The version the game shows without the cleaned mod; null for the cleaned mod's own objects.</param>
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
    TargetArea Area,
    TargetOrigin Origin,
    EarlierVersion? EarlierVersion)
{
    /// <summary>The placement as the winning version stores it, the scale normalized.</summary>
    public EulerPlacement Placement => new(Transform.Position, Rotation, Transform.Scale);

    /// <summary>The same object standing at the given placement.</summary>
    public TargetObject PlacedAt(EulerPlacement placement) => this with { Transform = placement.Transform, Rotation = placement.Rotation };

    /// <summary>
    /// The same object as its earlier version has it: the placement, and when that restores the whole record also the base object
    /// and what the version says about the reference itself.
    /// </summary>
    public TargetObject RestoredAs(EarlierVersion earlier)
    {
        var restored = PlacedAt(earlier.Placement);
        if (!earlier.RestoresWholeRecord) return restored;

        var reference = earlier.Reference;
        return restored with
        {
            Base = earlier.Base,
            IsTeleportDoor = reference.IsTeleportDoor,
            IsPrimitive = reference.IsPrimitive,
            HasMapMarker = reference.HasMapMarker,
            ReferenceRadius = reference.ReferenceRadius,
            PrimitiveBounds = reference.PrimitiveBounds,
        };
    }
}

/// <summary>Where a target object comes from. <see cref="CleanedMod"/> is the only origin of the target plugin's own objects.</summary>
public enum TargetOrigin
{
    /// <summary>An object the target plugin creates.</summary>
    CleanedMod,

    /// <summary>Another plugin's object that the target plugin overrides and moves.</summary>
    Moved,

    /// <summary>Another plugin's object that the target plugin overrides and gives another base object without moving it farther than the moved distance.</summary>
    Replaced,
}

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

/// <summary>What else depends on a placed reference or controls it, as its record and the records linking to it show.</summary>
[Flags]
public enum ReferenceRoles
{
    None = 0,
    Persistent = 1,
    TeleportDoor = 2,
    HasEnableParent = 4,
    ScriptNamesQuestOrScene = 8,
    LocationRefType = 16,

    /// <summary>Another placed record of the target spaces links to it (as enable parent, linked reference, script property, ...).</summary>
    LinkedFromPlacedRecord = 32,

    /// <summary>The reference itself carries map marker data (XMRK).</summary>
    MapMarker = 64,
}

/// <summary>Compact snapshot of another mod's placed object (winning version).</summary>
/// <param name="HasMapMarker">The placed reference itself carries map marker data (XMRK), e.g. a fast-travel marker.</param>
/// <param name="Change">Why it can cause removals; null for a support-only object.</param>
/// <param name="Roles">What else depends on it or controls it.</param>
/// <param name="ChangeWithoutCleanedMod">
/// How the game shows it differently from the values accepted in the world without the cleaned mod: its objects, its overrides and
/// the objects of the plugins that need it count as changes there. Null when it is accepted there, and always when the scan did not
/// judge the old places or found no changed object.
/// </param>
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
    ObjectChange? Change,
    ReferenceRoles Roles,
    ObjectChange? ChangeWithoutCleanedMod)
{
    public PlacedTransform Transform => new(Position, Mat3.FromEuler(Rotation), Scale);

    /// <summary>A placed NPC or creature (ACHR), the only placed type whose base link is an NPC.</summary>
    public bool IsPlacedNpc => Base is { Kind: BaseLinkKind.Npc };
}
