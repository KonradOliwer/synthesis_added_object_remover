namespace AddedObjectRemover;

/// <summary>
/// The reverse links among the winning records of the target spaces: the navmeshes whose edge-link table names a navmesh and the
/// placed records whose navmesh link names it, each built once, on first use, and shared by every final world over the same read.
/// Thread-safe.
/// </summary>
internal sealed class WinningNavmeshLinks(IReadOnlyDictionary<RecordKey, NavmeshVersion> winners, IReadOnlyDictionary<RecordKey, NavDoorLinkFact> winningDoorLinks)
{
    private readonly ComputedOnce<Dictionary<RecordKey, List<RecordKey>>> _navmeshesLinkingTo = new(() => IndexNavmeshesLinkingTo(winners));
    private readonly ComputedOnce<Dictionary<RecordKey, List<RecordKey>>> _doorsLinkingTo = new(() => IndexDoorsLinkingTo(winningDoorLinks));

    /// <returns>In key order.</returns>
    public IReadOnlyList<RecordKey> NavmeshesLinkingTo(RecordKey navmesh) => _navmeshesLinkingTo.Value.GetValueOrDefault(navmesh) ?? [];

    /// <returns>In key order.</returns>
    public IReadOnlyList<RecordKey> DoorsLinkingTo(RecordKey navmesh) => _doorsLinkingTo.Value.GetValueOrDefault(navmesh) ?? [];

    /// <summary>For each navmesh, the given navmeshes whose edge-link table names it, in key order.</summary>
    public static Dictionary<RecordKey, List<RecordKey>> IndexNavmeshesLinkingTo(IEnumerable<KeyValuePair<RecordKey, NavmeshVersion>> navmeshes)
    {
        var index = new Dictionary<RecordKey, List<RecordKey>>();
        foreach (var (key, version) in navmeshes.OrderBy(entry => entry.Key, RecordKeyOrder.Comparer))
        {
            var linked = version.Record.Data?.EdgeLinks.Select(link => link.Mesh.FormKey.KeyAsWritten()) ?? [];
            foreach (var navmesh in linked.Distinct()) KeyedGroups.GetOrAddList(index, navmesh).Add(key);
        }
        return index;
    }

    /// <summary>For each navmesh, the given doors whose navmesh link names it, in key order.</summary>
    public static Dictionary<RecordKey, List<RecordKey>> IndexDoorsLinkingTo(IEnumerable<KeyValuePair<RecordKey, NavDoorLinkFact>> links)
    {
        var index = new Dictionary<RecordKey, List<RecordKey>>();
        foreach (var (door, link) in links.OrderBy(entry => entry.Key, RecordKeyOrder.Comparer)) KeyedGroups.GetOrAddList(index, link.Navmesh).Add(door);
        return index;
    }
}
