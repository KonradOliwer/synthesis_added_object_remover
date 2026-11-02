using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.WriteThePatch;

/// <summary>
/// How a group of navmeshes goes back where the cleaned mod was removed: an edited navmesh takes its earlier geometry, a deleted one
/// comes back whole, an added one is lowered out of the walkable world and cut off; each takes the map entry copied for it, a sunk
/// one's lowered too; and the group's doors get their earlier navmesh link or lose it.
/// </summary>
internal static class NavmeshUndoWrite
{
    /// <summary>
    /// The members' navmesh orders in key order, then one map entry order per map, in map key order, then the door links copied,
    /// then those cleared.
    /// </summary>
    public static IEnumerable<WriteOrder> OrdersFor(NavmeshGroupWrite group) =>
    [
        .. group.Members.Select(NavmeshOrderFor).OfType<WriteOrder>(),
        .. group.Members
            .Where(member => member.EntryCopied is not null)
            .GroupBy(member => member.EntryCopied!.Map)
            .OrderBy(map => map.Key, RecordKeyOrder.Comparer)
            .Select(map => new WriteNavmeshMapEntries(map.Key, [.. map.Select(EntryOrderFor)])),
        .. group.DoorLinksCopied.Select(door => PlacedUndoWrite.PartsOrderFor(new PlacedPartsRestored(door, PlacedParts.NavDoorLink))),
        .. group.DoorLinksCleared.Select(door => new ClearNavDoorLink(door)),
    ];

    /// <returns>Null when only the member's map entry goes back.</returns>
    private static WriteOrder? NavmeshOrderFor(NavmeshMemberWrite member)
    {
        if (SunkHeightOf(member) is { } z) return new FlattenAndDetachNavmesh(member.Navmesh, z);
        return member.Undo switch
        {
            NavmeshUndo.Geometry => new CopyEarlierNavmeshGeometry(member.Navmesh),
            NavmeshUndo.BroughtBack => new CopyEarlierNavmesh(member.Navmesh),
            _ => null,
        };
    }

    private static NavmeshEntryOrder EntryOrderFor(NavmeshMemberWrite member) =>
        new(member.Navmesh, member.EntryCopied!.Plugin, SunkHeightOf(member));

    /// <returns>The height the navmesh and its map entry are lowered to; null unless the member is sunk.</returns>
    private static float? SunkHeightOf(NavmeshMemberWrite member) => member.Undo == NavmeshUndo.Sunk ? RemovalConvention.SunkNavmeshZ : null;
}
