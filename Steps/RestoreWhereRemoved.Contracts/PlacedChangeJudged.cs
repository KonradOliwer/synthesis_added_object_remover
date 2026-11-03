using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

/// <summary>What keeps a placed record whose area is cleared as the cleaned mod made it.</summary>
public enum PlacedGuard
{
    /// <summary>Where it goes back to, it comes too close to an object that stays, by the collision settings of the objects the cleaned mod changed.</summary>
    TooCloseToWhatStays,

    /// <summary>
    /// Where it goes back to, ground that stays changed lies under it or the area around it: ground the terrain plan keeps as the
    /// cleaned mod made it, or ground a later plugin changed.
    /// </summary>
    OnGroundThatStaysChanged,

    /// <summary>Where it goes back to, it lies partly between a cell's water level before the cleaned mod and the one that stays.</summary>
    AtWaterLineThatStaysChanged,

    /// <summary>Where it goes back to, it holds an invisible object of the cleaned mod that stays.</summary>
    HoldsInvisibleObjectThatStays,

    /// <summary>Where it goes back to, it stands in a navmesh that stays as the cleaned mod made it, so actors would walk into it.</summary>
    StandsInChangedNavmesh,

    /// <summary>An object that stays rests on it where it stands now, and would float once it goes back.</summary>
    CarriesObjectThatStays,

    /// <summary>It is a door whose navmesh link goes back with it, and a later plugin changed that link again.</summary>
    DoorLinkChangedLater,

    /// <summary>
    /// It is a door whose navmesh link goes back with it, and its link before the cleaned mod would not match the navmeshes as they end:
    /// the navmesh it names goes back in no group and does not answer it, or a navmesh that stays names the door elsewhere.
    /// </summary>
    DoorLinkDoesNotMatch,
}

/// <summary>The first guard that keeps a placed record as the cleaned mod made it.</summary>
/// <param name="By">
/// The object, navmesh, LAND record or cell the guard found; null when the check stopped, or for ground of a LAND record the cleaned
/// mod does not override.
/// </param>
/// <param name="Failure">Why the check stopped unexpectedly, which keeps the record too; null when it did not.</param>
/// <param name="Plugin">For ground that stays changed, the plugin of the winning LAND record that keeps it changed; null for any other guard.</param>
public sealed record PlacedRefusal(PlacedGuard Guard, RecordKey? By, string? Failure, PluginName? Plugin = null);

/// <summary>What a placed record's restore puts back, by the first of these its undone parts hold.</summary>
public enum PlacedRestoreKind
{
    /// <summary>Its base object with every field no other part holds.</summary>
    OwnModel,

    /// <summary>Its presence, which makes a record removed now present again.</summary>
    ShownAgain,

    /// <summary>Its presence, which makes a record present now removed again.</summary>
    HiddenAgain,

    /// <summary>Its position.</summary>
    MovedBack,

    /// <summary>Its rotation or scale.</summary>
    TurnedOrRescaledBack,
}

/// <summary>A placed record of another plugin with parts that go back where the cleaned mod's objects are removed, and what became of it.</summary>
/// <param name="Never">Why it never goes back; null when it can.</param>
/// <param name="OldPlace">The area around the earlier version's place; null when <paramref name="Never"/> is set.</param>
/// <param name="WhereItIsNow">For a target the removal steps left undecided, the area where it stands now; null otherwise.</param>
/// <param name="Refused">The first guard that keeps it, once every area judged is cleared; null when none does.</param>
/// <param name="IsRestored">Its undone parts go back: no never-rule holds, every area judged is cleared and no guard keeps it.</param>
/// <param name="Kind">What its restore puts back.</param>
/// <param name="KeptForArea">
/// When no never-rule holds and an area judged is not cleared, the verdict of the area that keeps it: one where the cleaned mod's
/// objects still stand before one with none of them around; null otherwise.
/// </param>
/// <param name="PlacedBack">
/// For a record that is a target object, the placement the patch writes when it goes back, as the cleaned mod placed the object:
/// each of position, rotation and scale from the earlier version only when it goes back; null otherwise.
/// </param>
/// <remarks>When it stays, exactly one of <paramref name="Never"/>, <paramref name="Refused"/> and <paramref name="KeptForArea"/> says why.</remarks>
public sealed record PlacedChangeJudged(
    RecordChangedByCleanedMod Change,
    NeverRestoredReason? Never,
    AreaShare? OldPlace,
    AreaShare? WhereItIsNow,
    PlacedRefusal? Refused,
    bool IsRestored,
    PlacedRestoreKind Kind,
    AreaVerdict? KeptForArea,
    EulerPlacement? PlacedBack);
