using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>A candidate navmesh and what the patch writes for it when its group goes back.</summary>
internal sealed record NavmeshMember(NavmeshChangedByCleanedMod Change, NavmeshUndo Undo)
{
    public RecordKey Key => Change.Navmesh;

    /// <summary>Its triangles end where they lay before the cleaned mod.</summary>
    public bool StandsAsBefore => Undo is NavmeshUndo.Geometry or NavmeshUndo.BroughtBack;

    /// <summary>The shape it ends with; null for a sunk one, which keeps no edge link, door triangle or Preferred triangle.</summary>
    public NavmeshShape? Planned => Undo switch
    {
        NavmeshUndo.Geometry or NavmeshUndo.BroughtBack => Change.Earlier,
        NavmeshUndo.MapEntry => Change.Current,
        _ => null,
    };

    /// <summary>The entry the patch copies: the one in force before the cleaned mod, or the cleaned mod's for a sunk one; null when none is copied.</summary>
    public NavmeshMapEntryFact? EntryCopied => Undo switch
    {
        NavmeshUndo.Geometry => Change.MapEntry is PartOutcome.Undo ? Change.Earlier?.MapEntry : null,
        NavmeshUndo.MapEntry or NavmeshUndo.BroughtBack => Change.Earlier?.MapEntry,
        _ => Change.Current.MapEntry,
    };

    /// <summary>The entry in force once written; null for a sunk one, whose entry names no navmesh or door.</summary>
    public NavmeshMapEntryFact? PlannedEntry => Undo == NavmeshUndo.Sunk ? null : EntryCopied ?? Change.Current.MapEntry;

    /// <summary>The entry it gets is the one in force before the cleaned mod.</summary>
    public bool CopiesEarlierEntry => Undo is NavmeshUndo.MapEntry or NavmeshUndo.BroughtBack || (Undo == NavmeshUndo.Geometry && Change.MapEntry is PartOutcome.Undo);

    /// <summary>The patch writes its navmesh record; it does not when only its map entry goes back.</summary>
    public bool WritesNavmeshRecord => Undo != NavmeshUndo.MapEntry;

    public NavmeshMemberWrite Write => new(Key, Undo, EntryCopied);

    /// <summary>The triangles flagged Preferred as the game shows them now; none while the navmesh is deleted.</summary>
    public ImmutableHashSet<int> PreferredNow => Change.Current.IsDeleted ? [] : Change.Current.PreferredTriangles();

    /// <summary>The triangles flagged Preferred once written.</summary>
    public ImmutableHashSet<int> PreferredPlanned => Planned?.PreferredTriangles() ?? [];
}

/// <summary>Navmeshes that go back together or not at all.</summary>
/// <param name="Members">In key order.</param>
/// <param name="Doors">The doors linked to a member from either side, in key order.</param>
/// <param name="Write">What the patch writes to put the group back; null until the group passes its check, and while it is refused.</param>
/// <param name="Refused">The first guard that keeps the group; null when none does.</param>
internal sealed record NavmeshGroup(ImmutableArray<NavmeshMember> Members, ImmutableArray<DoorLinkChange> Doors, NavmeshGroupWrite? Write, NavmeshRefusal? Refused)
{
    public bool Holds(RecordKey navmesh) => Members.Any(member => member.Key == navmesh);

    public NavmeshMember? MemberOf(RecordKey navmesh) => Members.FirstOrDefault(member => member.Key == navmesh);

    /// <summary>The doors the door triangles of the members whose triangles go back name, and those the entries copied name, in key order.</summary>
    public IEnumerable<RecordKey> DoorsNamedByMembersThatGoBack() =>
        Members.Where(member => member.StandsAsBefore).SelectMany(member => member.Planned!.DoorTriangles.Select(triangle => triangle.Door))
            .Concat(Members.Where(member => member.Undo != NavmeshUndo.Sunk).SelectMany(member => member.EntryCopied?.LinkedDoors ?? []))
            .Distinct()
            .Order(RecordKeyOrder.Comparer);

    /// <summary>The group kept as the cleaned mod made it, for the first reason found; a group refused before keeps its first reason.</summary>
    public NavmeshGroup RefusedFor(NavmeshRefusal refusal) => Refused is null ? this with { Refused = refusal, Write = null } : this;

    /// <param name="linksToClear">The doors whose navmesh link is cleared, in key order.</param>
    /// <param name="writesLink">Whether the group writes the door's link before the cleaned mod.</param>
    public NavmeshGroupWrite CreateWrite(IEnumerable<RecordKey> linksToClear, Func<DoorLinkChange, bool> writesLink) =>
        new([.. Members.Select(member => member.Write)], [.. Doors.Where(writesLink).Select(door => door.Door)], [.. linksToClear]);
}
