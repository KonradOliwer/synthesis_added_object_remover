namespace AddedObjectRemover;

/// <summary>
/// The navmesh consistency check over what write orders change: every navmesh they write or give a map entry, and every navmesh a
/// door whose navmesh link they write names once written. A placed record an order sets Initially Disabled is removed, the way the
/// patch removes a record, so it does not stand.
/// </summary>
public static class NavmeshWriteConsistency
{
    /// <param name="doorsLeftRemoved">Placed records the world leaves removed without an order: they do not stand either.</param>
    /// <param name="readWorld">The world with the orders written; read only when an order writes a navmesh, a map entry or a door's navmesh link.</param>
    /// <returns>The first fault (<see cref="NavmeshConsistency.FirstFault"/>); null when every checked navmesh passes or nothing navmesh-related is written.</returns>
    public static NavmeshFault? FirstFault(IReadOnlyList<WriteOrder> orders, IReadOnlySet<RecordKey> doorsLeftRemoved, Func<INavmeshWorldView> readWorld)
    {
        var navmeshes = WrittenNavmeshes(orders).ToHashSet();
        var doors = orders.OfType<CopyEarlierPlacedParts>().Where(copy => copy.Parts.HasFlag(PlacedParts.NavDoorLink)).Select(copy => copy.Record).ToList();
        if (navmeshes.Count == 0 && doors.Count == 0) return null;

        var world = readWorld();
        navmeshes.UnionWith(doors.Select(door => world.DoorOf(door)?.Link).OfType<NavDoorLinkFact>().Select(link => link.Navmesh));
        var broughtBack = orders.Where(order => order is CopyEarlierNavmeshGeometry or CopyEarlierNavmesh).Select(order => order.Record).ToHashSet();
        var doorsThatDoNotStand = orders.OfType<SetInitiallyDisabled>().Select(order => order.Record).Concat(doorsLeftRemoved).ToHashSet();
        return NavmeshConsistency.FirstFault(world, navmeshes, broughtBack, doorsThatDoNotStand);
    }

    private static IEnumerable<RecordKey> WrittenNavmeshes(IReadOnlyList<WriteOrder> orders) =>
        orders.SelectMany(order => order switch
        {
            CopyEarlierNavmeshGeometry or CopyEarlierNavmesh or SetNavmeshZ => [order.Record],
            WriteNavmeshMapEntries entries => entries.Entries.Select(entry => entry.Navmesh),
            _ => [],
        });
}
