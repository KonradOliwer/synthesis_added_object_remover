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

    /// <summary>The order that writes its navmesh record; null when only its map entry goes back.</summary>
    public WriteOrder? NavmeshOrder => Undo switch
    {
        NavmeshUndo.Geometry => new CopyEarlierNavmeshGeometry(Key),
        NavmeshUndo.BroughtBack => new CopyEarlierNavmesh(Key),
        NavmeshUndo.Sunk => new FlattenAndDetachNavmesh(Key, RemovalConvention.SunkNavmeshZ),
        _ => null,
    };

    /// <summary>The order that copies <see cref="EntryCopied"/>, lowered for a sunk one; null when no entry is copied.</summary>
    public NavmeshEntryOrder? EntryOrder =>
        EntryCopied is { } entry ? new NavmeshEntryOrder(Key, entry.Plugin, Undo == NavmeshUndo.Sunk ? RemovalConvention.SunkNavmeshZ : null) : null;

    /// <summary>The triangles flagged Preferred as the game shows them now; none while the navmesh is deleted.</summary>
    public ImmutableHashSet<int> PreferredNow => Change.Current.IsDeleted ? [] : Change.Current.PreferredTriangles();

    /// <summary>The triangles flagged Preferred once written.</summary>
    public ImmutableHashSet<int> PreferredPlanned => Planned?.PreferredTriangles() ?? [];
}

/// <summary>Navmeshes that go back together or not at all.</summary>
/// <param name="Members">In key order.</param>
/// <param name="Doors">The doors linked to a member from either side, in key order.</param>
/// <param name="Orders">The orders that put the group back; empty while it is refused.</param>
/// <param name="Refused">The first guard that keeps the group; null when none does.</param>
internal sealed record NavmeshGroup(ImmutableArray<NavmeshMember> Members, ImmutableArray<DoorLinkChange> Doors, ImmutableArray<WriteOrder> Orders, NavmeshRefusal? Refused)
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
    public NavmeshGroup RefusedFor(NavmeshRefusal refusal) => Refused is null ? this with { Refused = refusal, Orders = [] } : this;

    /// <summary>
    /// The members' navmesh orders in key order, then one map entry order per map, then the door links the group writes, then those
    /// cleared, each in key order.
    /// </summary>
    /// <param name="linksToClear">The doors whose navmesh link is cleared, in key order.</param>
    /// <param name="writesLink">Whether the group writes the door's link before the cleaned mod.</param>
    public ImmutableArray<WriteOrder> OrdersToPutBack(IEnumerable<RecordKey> linksToClear, Func<DoorLinkChange, bool> writesLink) =>
    [
        .. Members.Select(member => member.NavmeshOrder).OfType<WriteOrder>(),
        .. Members
            .Where(member => member.EntryCopied is not null)
            .GroupBy(member => member.EntryCopied!.Map)
            .OrderBy(map => map.Key, RecordKeyOrder.Comparer)
            .Select(map => new WriteNavmeshMapEntries(map.Key, [.. map.Select(member => member.EntryOrder!)])),
        .. Doors.Where(writesLink).Select(door => new CopyEarlierPlacedParts(door.Door, PlacedParts.NavDoorLink)),
        .. linksToClear.Select(door => new ClearNavDoorLink(door)),
    ];
}
