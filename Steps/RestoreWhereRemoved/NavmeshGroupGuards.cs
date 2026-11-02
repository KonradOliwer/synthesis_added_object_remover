using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// The guards a group of navmeshes is checked against once, on the facts read: later changes, the record format, edge links, only
/// what existed comes back, navmesh info map and preferred paths, map entries and doors. A navmesh outside the target spaces has no
/// facts, so what a link or door into it needs is left to the consistency check. Thread-safe.
/// </summary>
/// <param name="changed">Every navmesh the cleaned mod changed or added, by key.</param>
/// <param name="sunk">Every candidate navmesh the patch sinks when its group goes back; a group never checks whether another one goes back.</param>
/// <param name="changedRecords">The placed records of other plugins the cleaned mod changed, by key.</param>
/// <param name="removedByRun">Whether the run removes a placed record, which the patch then disables.</param>
/// <param name="links">Who writes each door's navmesh link, and the restores of the doors that are placed candidates.</param>
/// <param name="doorsIfAllGoBack">The doors as they end when every placed record whose areas let it go back goes back.</param>
internal sealed class NavmeshGroupGuards(
    IReadOnlyDictionary<RecordKey, NavmeshChangedByCleanedMod> changed,
    IReadOnlySet<RecordKey> sunk,
    IReadOnlyDictionary<RecordKey, RecordChangedByCleanedMod> changedRecords,
    Func<RecordKey, bool> removedByRun,
    DoorLinks links,
    DoorsAsTheyEnd doorsIfAllGoBack,
    LoadOrderPlugins plugins,
    PluginName cleanedMod)
{
    /// <summary>For each navmesh, the navmeshes the cleaned mod changed whose winning map entry merges with it, in key order.</summary>
    private readonly ILookup<RecordKey, RecordKey> _changedMergingWith = changed.Values
        .OrderBy(navmesh => navmesh.Navmesh, RecordKeyOrder.Comparer)
        .SelectMany(navmesh => MergesOf(navmesh.WinningMapEntry).Distinct().Select(merged => (Merged: merged, By: navmesh.Navmesh)))
        .ToLookup(entry => entry.Merged, entry => entry.By);

    /// <summary>
    /// The group refused for the first guard on the facts read it fails, in the order of <see cref="NavmeshGuard"/>; otherwise with
    /// what the patch writes for it, refused when the consistency check over what that alone would leave finds a fault. The check
    /// sees the doors as the door guard does: the removals the run writes and the restores of the doors that are placed candidates.
    /// The guards on the facts come first, so a fault they name more precisely is reported by them.
    /// </summary>
    /// <param name="findFaultIfWritten">The consistency check's first fault over the world the trial's writes alone would leave; null when none.</param>
    public NavmeshGroup Check(NavmeshGroup group, Func<NavmeshWriteTrial, NavmeshFault?> findFaultIfWritten)
    {
        if (FirstRefusal(group) is { } refusal) return group.RefusedFor(refusal);
        var write = group.CreateWrite(LinksToClear(group), links.WrittenByGroup);
        var trial = new NavmeshWriteTrial(RemovedDoorsOf(group), [.. links.RestoresOf(group.Doors.Select(door => door.Door))], write);
        return findFaultIfWritten(trial) is { } fault ? group.RefusedFor(InconsistentData(group, fault)) : group with { Write = write };
    }

    /// <summary>
    /// The cleaned mod's doors the run removes whose navmesh link names a sunk member, so no removed door keeps a link into it. The
    /// run removes no record of the cleaned mod that a later plugin overrides (it is no target), so no door a later plugin wins is cleared.
    /// </summary>
    private IEnumerable<RecordKey> LinksToClear(NavmeshGroup group) =>
        group.Doors
            .Where(door => door.Door.Plugin == cleanedMod && removedByRun(door.Door) && door.Current is { } link && group.MemberOf(link.Navmesh)?.Undo == NavmeshUndo.Sunk)
            .Select(door => door.Door);

    /// <summary>The doors of the group the run removes, so the consistency check sees those doors as the patch leaves them.</summary>
    private ImmutableArray<RecordKey> RemovedDoorsOf(NavmeshGroup group) =>
        [.. group.Doors.Where(door => removedByRun(door.Door)).Select(door => door.Door)];

    /// <summary>A fault in a member whose earlier triangles come back lies in the data before the cleaned mod; any other lies in the data as it stands.</summary>
    private static NavmeshRefusal InconsistentData(NavmeshGroup group, NavmeshFault fault) =>
        new(group.MemberOf(fault.Navmesh)?.StandsAsBefore == true ? NavmeshGuard.EarlierDataInconsistent : NavmeshGuard.DataAsItStandsInconsistent, fault.Navmesh, fault);

    private NavmeshRefusal? FirstRefusal(NavmeshGroup group) =>
        ChangedLater(group)
        ?? StoredInAnotherRecordFormat(group)
        ?? UnmatchedEdgeLink(group)
        ?? DidNotExistBefore(group)
        ?? PreferredPaths(group)
        ?? MapEntries(group)
        ?? Doors(group, doorsIfAllGoBack);

    /// <summary>A later plugin changed a member's geometry, presence or map entry, or a linked door's navmesh link or presence.</summary>
    private NavmeshRefusal? ChangedLater(NavmeshGroup group)
    {
        var member = group.Members.FirstOrDefault(member =>
            member.Change.Geometry is PartOutcome.LeftToLaterPlugin || member.Change.Presence is PartOutcome.LeftToLaterPlugin || member.Change.MapEntry is PartOutcome.LeftToLaterPlugin);
        if (member is not null) return Refusal(NavmeshGuard.ChangedLater, member.Key);
        var door = group.Doors.FirstOrDefault(door => door.NavDoorLink is PartOutcome.LeftToLaterPlugin || PresenceLeftToLater(door.Door));
        return door is null ? null : Refusal(NavmeshGuard.ChangedLater, door.Door);
    }

    private static NavmeshRefusal? StoredInAnotherRecordFormat(NavmeshGroup group) => FirstMember(
        group, member => member.Undo == NavmeshUndo.Geometry && member.Change.Geometry is PartOutcome.KeptForRecordFormat, NavmeshGuard.StoredInAnotherRecordFormat);

    /// <summary>Every edge link between a member as planned and a navmesh that stays, either way, is answered over the same edge.</summary>
    private static NavmeshRefusal? UnmatchedEdgeLink(NavmeshGroup group)
    {
        foreach (var member in group.Members)
        {
            if ((FirstUnansweredLinkOut(group, member) ?? FirstUnansweredLinkIn(group, member)) is { } staying) return Refusal(NavmeshGuard.EdgeLinkUnmatched, staying);
        }
        return null;
    }

    /// <returns>The first navmesh that stays whose triangle a planned link of the member leads into and that does not link back.</returns>
    private static RecordKey? FirstUnansweredLinkOut(NavmeshGroup group, NavmeshMember member)
    {
        foreach (var link in member.Planned?.EdgeLinks ?? [])
        {
            if (group.Holds(link.Neighbour)) continue;
            var staying = member.Change.Neighbours.FirstOrDefault(neighbour => neighbour.Navmesh == link.Neighbour);
            if (staying is not null && !LinksBack(staying.Winner, link.NeighbourTriangle, member.Key, link)) return link.Neighbour;
        }
        return null;
    }

    /// <returns>The first navmesh that stays, in key order, that links into the member where the member as planned does not link back.</returns>
    private static RecordKey? FirstUnansweredLinkIn(NavmeshGroup group, NavmeshMember member)
    {
        foreach (var staying in member.Change.Neighbours.Where(neighbour => !group.Holds(neighbour.Navmesh)))
        {
            foreach (var link in staying.Winner.EdgeLinks.Where(link => link.Neighbour == member.Key))
            {
                if (member.Planned is not { } planned || !LinksBack(planned, link.NeighbourTriangle, staying.Navmesh, link)) return staying.Navmesh;
            }
        }
        return null;
    }

    /// <summary>Whether the shape's triangle links to the navmesh and triangle the link leaves, over an edge that coincides with the link's.</summary>
    private static bool LinksBack(NavmeshShape shape, int triangle, RecordKey from, NavmeshEdgeLinkFact link) =>
        shape.EdgeLinks.Any(back =>
            back.Triangle == triangle && back.Neighbour == from && back.NeighbourTriangle == link.Triangle
            && NavmeshConsistency.EdgesCoincide((back.From, back.To), (link.From, link.To)));

    /// <summary>
    /// No member whose navmesh record is written goes into a cell some version deletes; a member that goes back existed before
    /// the cleaned mod, in the cell holding it now, with a map entry when one goes back; a replacement of a deleted member is in the
    /// group; a sunk member lies in a cell that exists below the cleaned mod.
    /// </summary>
    private NavmeshRefusal? DidNotExistBefore(NavmeshGroup group)
    {
        foreach (var member in group.Members)
        {
            if (FirstMissingBefore(group, member) is { } by) return Refusal(NavmeshGuard.DidNotExistBefore, by);
        }
        return null;
    }

    /// <returns>The member, its cell or its replacement that is missing; null when none is.</returns>
    private RecordKey? FirstMissingBefore(NavmeshGroup group, NavmeshMember member)
    {
        var change = member.Change;
        if (member.WritesNavmeshRecord && change.CellDeletedInAVersion) return change.Cell;
        if (member.Undo == NavmeshUndo.Sunk) return change.Current.Cell.Plugin == cleanedMod ? change.Current.Cell : null;
        if (change.Earlier is not { IsDeleted: false } earlier || earlier.Cell != change.Cell) return member.Key;
        if (member.CopiesEarlierEntry && earlier.MapEntry is null) return member.Key;
        return change.ReplacedBy.Where(replacement => !group.Holds(replacement)).Select(replacement => (RecordKey?)replacement).FirstOrDefault();
    }

    /// <summary>
    /// No plugin after the patch overrides the navmesh info map, no linked door's winner loads after the patch, and the winning
    /// preferred paths name no member whose Preferred triangles change.
    /// </summary>
    private NavmeshRefusal? PreferredPaths(NavmeshGroup group)
    {
        var first = group.Members[0];
        if (first.Change.WinningMapMod is { } map && plugins.LoadsAfterPatch(map)) return Refusal(NavmeshGuard.PreferredPaths, first.Key);
        var door = group.Doors.FirstOrDefault(door => changedRecords.GetValueOrDefault(door.Door) is { } record && plugins.LoadsAfterPatch(record.WinningMod));
        if (door is not null) return Refusal(NavmeshGuard.PreferredPaths, door.Door);
        return FirstMember(group, member => member.Change.NamedInPreferredPaths && !member.PreferredNow.SetEquals(member.PreferredPlanned), NavmeshGuard.PreferredPaths);
    }

    /// <summary>
    /// The planned entries merge only with navmeshes that end walkable, and no navmesh that stays, next to the group or changed by
    /// the cleaned mod, merges with a sunk member in its winning entry.
    /// </summary>
    /// <remarks>
    /// A navmesh with no fact (neither changed by the cleaned mod nor edge-linked to a navmesh it changed) is not read, so its entry
    /// is not checked here; the consistency check reaches it only one edge link away.
    /// </remarks>
    private NavmeshRefusal? MapEntries(NavmeshGroup group)
    {
        foreach (var entry in group.Members.Select(member => member.PlannedEntry).OfType<NavmeshMapEntryFact>())
        {
            var merged = entry.MergedTo.Concat(entry.PreferredMerges).Where(navmesh => !EndsWalkable(group, navmesh)).Select(navmesh => (RecordKey?)navmesh).FirstOrDefault();
            if (merged is { } navmesh) return Refusal(NavmeshGuard.MapEntry, navmesh);
        }
        var sunkMembers = group.Members.Where(member => member.Undo == NavmeshUndo.Sunk).Select(member => member.Key).ToHashSet();
        var neighbours = group.Members
            .SelectMany(member => member.Change.Neighbours)
            .Where(neighbour => MergesOf(neighbour.Winner.MapEntry).Any(sunkMembers.Contains))
            .Select(neighbour => neighbour.Navmesh);
        var staying = neighbours.Concat(sunkMembers.SelectMany(sunkMember => _changedMergingWith[sunkMember]))
            .Where(navmesh => !group.Holds(navmesh))
            .Order(RecordKeyOrder.Comparer)
            .Select(navmesh => (RecordKey?)navmesh)
            .FirstOrDefault();
        return staying is { } by ? Refusal(NavmeshGuard.MapEntry, by) : null;
    }

    private static IEnumerable<RecordKey> MergesOf(NavmeshMapEntryFact? entry) => entry is null ? [] : entry.MergedTo.Concat(entry.PreferredMerges);

    /// <summary>A navmesh ends walkable unless the patch sinks it or it is one the cleaned mod deleted that no group brings back.</summary>
    private bool EndsWalkable(NavmeshGroup group, RecordKey navmesh) =>
        group.MemberOf(navmesh) is { } member
            ? member.Undo != NavmeshUndo.Sunk
            : !sunk.Contains(navmesh) && !(changed.TryGetValue(navmesh, out var other) && other.Current.IsDeleted);

    /// <summary>
    /// A door a member that goes back names stands; every standing door's final navmesh link names a navmesh that is not sunk
    /// and, in the group, a triangle whose door triangle names the door, and the other way round; a door whose link the group writes
    /// leaves no navmesh that stays and lies in no deleted cell. A removed door's link is not checked; the patch clears it on the
    /// cleaned mod's own doors that link into a sunk member. A door whose link goes with it joins the group this way: the group goes
    /// back only while the door's link as it ends fits the group.
    /// </summary>
    /// <param name="doors">Whether each door stands and where its link points, given which placed records go back.</param>
    public NavmeshRefusal? Doors(NavmeshGroup group, DoorsAsTheyEnd doors)
    {
        foreach (var named in group.DoorsNamedByMembersThatGoBack())
        {
            if (!doors.Stands(named)) return Refusal(NavmeshGuard.Door, named);
        }
        var door = group.Doors.FirstOrDefault(door =>
            (doors.Stands(door.Door) && doors.FinalLinkOf(door) is { } link && !DoorTriangleAnswers(group, link, door.Door))
            || (links.WrittenByGroup(door) && (door.InDeletedCell || (door.Current is { } now && !group.Holds(now.Navmesh)))));
        if (door is not null) return Refusal(NavmeshGuard.Door, door.Door);

        foreach (var member in group.Members)
        {
            foreach (var triangle in member.Planned?.DoorTriangles ?? [])
            {
                var fact = group.Doors.FirstOrDefault(door => door.Door == triangle.Door);
                if (fact is not null && doors.FinalLinkOf(fact) != new NavDoorLinkFact(member.Key, triangle.Triangle)) return Refusal(NavmeshGuard.Door, triangle.Door);
            }
        }
        return null;
    }

    /// <summary>A link into a navmesh that stays is answered as it stands; one into a member by its planned door triangles.</summary>
    private bool DoorTriangleAnswers(NavmeshGroup group, NavDoorLinkFact link, RecordKey door) =>
        group.MemberOf(link.Navmesh) is { } member
            ? member.Planned is { } planned && planned.DoorTriangles.Contains(new NavmeshDoorTriangleFact(link.Triangle, door))
            : !sunk.Contains(link.Navmesh);

    private bool PresenceLeftToLater(RecordKey door) =>
        changedRecords.GetValueOrDefault(door)?.Earlier.LeftToLaterPlugins.ContainsKey(PlacedParts.Presence) == true;

    private static NavmeshRefusal? FirstMember(NavmeshGroup group, Func<NavmeshMember, bool> fails, NavmeshGuard guard) =>
        group.Members.FirstOrDefault(fails) is { } member ? Refusal(guard, member.Key) : null;

    private static NavmeshRefusal Refusal(NavmeshGuard guard, RecordKey by) => new(guard, by, Fault: null);
}
