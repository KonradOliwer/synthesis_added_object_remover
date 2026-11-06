using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

/// <summary>What keeps a placed record whose area is cleared as the cleaned mod made it.</summary>
public enum PlacedGuard
{
    /// <summary>
    /// Where it goes back to, it comes too close to an object that stays, or its going back makes an object stand that an object of
    /// the cleaned mod that stays, or a copy, is too close to, by the collision settings of the objects the cleaned mod changed. An
    /// object going back is never a cause, and an object a removal step restored to its earlier version is support only, so it never
    /// blocks.
    /// </summary>
    TooCloseToWhatStays,

    /// <summary>Where it goes back to, it stands in a navmesh that stays as the cleaned mod made it, so actors would walk into it.</summary>
    StandsInChangedNavmesh,

    /// <summary>
    /// With KeepSupportingObject: leaving where it stands now, it leaves an object of a mod trusted in full, or of a master of one,
    /// without support, directly or through what goes with it: the object named.
    /// </summary>
    HoldsUpOtherModsObject,

    /// <summary>
    /// Where it goes back to, it would lose its support, since what it rested on there, an object or the ground, stays as the cleaned mod
    /// made it or goes with the follow-up, and it may not be pulled down or removed there: the cleaned mod moved it far enough to count among its targets, or it
    /// is not of a plugin trusted in full, or the handling of objects left without support or its references keep it.
    /// </summary>
    ArrivesWithoutSupport,

    /// <summary>
    /// Where it goes back to, ground that stays as the cleaned mod made it, or that the restore blends towards such ground, rises into it,
    /// which the ground before the cleaned mod did not.
    /// </summary>
    ArrivesInGround,

    /// <summary>
    /// Where it goes back to, it lies partly between a cell's water level before the cleaned mod and the cleaned mod's level, which stays.
    /// </summary>
    ArrivesAtWaterLine,

    /// <summary>
    /// Where it goes back to, it would lose its support and be pulled down, and the place it would land on fails a guard of a place: too
    /// close or navmesh. It carries what that guard found.
    /// </summary>
    WouldNotFitWherePulledDown,

    /// <summary>
    /// Its going back makes the follow-up restore an object the cleaned mod changed (the object named) to its earlier version, and that
    /// version would not fit where it goes back to: it would lose its support there, end in ground or at a water line that stays as the
    /// cleaned mod made it, or fail a guard there.
    /// </summary>
    TakesAlongObjectThatWouldNotFit,

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
/// The object, navmesh, LAND record or cell the guard found; null when the check stopped, or for a guard that names none.
/// </param>
/// <param name="Failure">Why the check stopped unexpectedly, which keeps the record too; null when it did not.</param>
public sealed record PlacedRefusal(PlacedGuard Guard, RecordKey? By, string? Failure);

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
/// <param name="JudgedPlace">
/// The area that decides whether it goes back: around the earlier version's place when its position goes back, around the place where
/// it stands when a later plugin holds the position; null when <paramref name="Never"/> is set.
/// </param>
/// <param name="WhereItIsNow">
/// For a target the removal steps left undecided, the area where it stands now; null otherwise. It decides nothing: it only says
/// whether a copy is needed there.
/// </param>
/// <param name="Refused">The first guard that keeps it, once <paramref name="JudgedPlace"/> is cleared; null when none does.</param>
/// <param name="IsRestored">Its undone parts go back: no never-rule holds, <paramref name="JudgedPlace"/> is cleared and no guard keeps it.</param>
/// <param name="Kind">What its restore puts back.</param>
/// <param name="KeptForArea">
/// When no never-rule holds and <paramref name="JudgedPlace"/> is not cleared, its verdict; null otherwise.
/// </param>
/// <param name="PlacedBack">
/// For a record that is a target object, the placement the patch writes when it goes back, as the cleaned mod placed the object:
/// each of position, rotation and scale from the earlier version only when it goes back; null otherwise.
/// </param>
/// <param name="Cloning">
/// For another mod's object the cleaned mod moved whose old place goes back while the cleaned mod's content where it stands now
/// needs a copy of it, and that has no copy yet: whether a copy is planned there, and why not. A refused copy leaves the object
/// going back without one. Null for every other record.
/// </param>
/// <remarks>
/// When it stays, exactly one of <paramref name="Never"/>, <paramref name="Refused"/> and <paramref name="KeptForArea"/> says why.
/// A record with a planned copy goes back together with that copy: <paramref name="IsRestored"/> with an unrefused
/// <paramref name="Cloning"/> means the patch writes both.
/// </remarks>
public sealed record PlacedChangeJudged(
    RecordChangedByCleanedMod Change,
    NeverRestoredReason? Never,
    AreaShare? JudgedPlace,
    AreaShare? WhereItIsNow,
    PlacedRefusal? Refused,
    bool IsRestored,
    PlacedRestoreKind Kind,
    AreaVerdict? KeptForArea,
    EulerPlacement? PlacedBack,
    CloneJudged? Cloning = null);
