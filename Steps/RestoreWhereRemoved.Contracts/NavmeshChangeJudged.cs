using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

/// <summary>What the patch writes for a navmesh of a group that goes back.</summary>
public enum NavmeshUndo
{
    /// <summary>The cleaned mod edited it: its geometry goes back, and its map entry when the cleaned mod last changed that too.</summary>
    Geometry,

    /// <summary>The cleaned mod edited only its map entry, which goes back.</summary>
    MapEntry,

    /// <summary>The cleaned mod deleted it: it comes back whole with its map entry.</summary>
    BroughtBack,

    /// <summary>The cleaned mod added it: it is lowered out of the walkable world and cut off.</summary>
    Sunk,
}

/// <summary>A navmesh the cleaned mod changed or added, and what its area says.</summary>
/// <param name="Never">Why it never goes back; null when it can.</param>
/// <param name="Area">The area around the centre of its bounds, or the whole interior; null when <paramref name="Never"/> is set or it has no vertices.</param>
/// <param name="Undo">What the patch writes when its group goes back; null when it is no candidate: a never-rule holds, its area is not cleared or nothing in it goes back.</param>
/// <param name="KeptForArea">When no never-rule holds and its area is not cleared, the area's verdict; null otherwise.</param>
/// <param name="NothingGoesBack">No never-rule holds and its area is cleared or it has no vertices to judge it at, but nothing in it goes back.</param>
/// <remarks>
/// When it is no candidate, exactly one of <paramref name="Never"/>, <paramref name="KeptForArea"/> and
/// <paramref name="NothingGoesBack"/> says why; a candidate goes back or stays with its group.
/// </remarks>
public sealed record NavmeshChangeJudged(
    NavmeshChangedByCleanedMod Change, NeverRestoredReason? Never, AreaShare? Area, NavmeshUndo? Undo, AreaVerdict? KeptForArea, bool NothingGoesBack);

/// <summary>What keeps a group of navmeshes as the cleaned mod made them.</summary>
public enum NavmeshGuard
{
    /// <summary>A later plugin changed a member or a door linked to one again.</summary>
    ChangedLater,

    /// <summary>A member's geometry would go back but is stored in another record format.</summary>
    StoredInAnotherRecordFormat,

    /// <summary>An edge link between a member as planned and a navmesh that stays would not be answered by a matching link back.</summary>
    EdgeLinkUnmatched,

    /// <summary>A member would come back where it did not exist before the cleaned mod, or without its earlier map entry or replacement.</summary>
    DidNotExistBefore,

    /// <summary>A plugin after the patch overrides the navmesh info map, or the preferred paths name a member whose Preferred triangles change.</summary>
    PreferredPaths,

    /// <summary>A map entry would merge with a navmesh that is sunk or deleted.</summary>
    MapEntry,

    /// <summary>A door named by a member would not stand, or a door and a navmesh would not name each other.</summary>
    Door,

    /// <summary>The navmesh data the group brings back from before the cleaned mod is not consistent.</summary>
    EarlierDataInconsistent,

    /// <summary>The navmesh data that stays as it stands is not consistent with the group.</summary>
    DataAsItStandsInconsistent,

    /// <summary>The ground under a member as planned ends more than the height tolerance from its height before the cleaned mod.</summary>
    Ground,

    /// <summary>An object that stays stands in a member as planned.</summary>
    Object,

    /// <summary>The water level of a member's cell does not end as it was before the cleaned mod.</summary>
    Water,
}

/// <summary>The first guard that keeps a group of navmeshes as the cleaned mod made them.</summary>
/// <param name="By">The navmesh, door, object or cell the guard found.</param>
/// <param name="Fault">For <see cref="NavmeshGuard.EarlierDataInconsistent"/> and <see cref="NavmeshGuard.DataAsItStandsInconsistent"/>, the check's first fault; null otherwise.</param>
public sealed record NavmeshRefusal(NavmeshGuard Guard, RecordKey By, NavmeshFault? Fault);

/// <summary>What the patch writes for one navmesh of a group that goes back.</summary>
/// <param name="EntryCopied">
/// The map entry the patch copies: the one in force before the cleaned mod, or the cleaned mod's for a sunk navmesh; null when none
/// is copied.
/// </param>
public sealed record NavmeshMemberWrite(RecordKey Navmesh, NavmeshUndo Undo, NavmeshMapEntryFact? EntryCopied);

/// <summary>What the patch writes for a group of navmeshes that goes back.</summary>
/// <param name="Members">In key order.</param>
/// <param name="DoorLinksCopied">The doors whose navmesh link takes its value before the cleaned mod with the group, in key order.</param>
/// <param name="DoorLinksCleared">The doors whose navmesh link is cleared, in key order.</param>
public sealed record NavmeshGroupWrite(ImmutableArray<NavmeshMemberWrite> Members, ImmutableArray<RecordKey> DoorLinksCopied, ImmutableArray<RecordKey> DoorLinksCleared);

/// <summary>Navmeshes that go back together or not at all, and what the patch writes for them.</summary>
/// <param name="Members">In key order.</param>
/// <param name="Write">What the patch writes to put the group back, which is what makes it go back; null exactly while it is refused.</param>
/// <param name="Refused">The first guard that keeps the group; null when none does.</param>
public sealed record NavmeshGroupJudged(ImmutableArray<RecordKey> Members, NavmeshGroupWrite? Write, NavmeshRefusal? Refused);
