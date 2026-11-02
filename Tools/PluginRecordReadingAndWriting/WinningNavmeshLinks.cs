namespace AddedObjectRemover;

/// <summary>
/// The reverse links among the winning records of the plugin spaces: the navmeshes whose edge-link table names a navmesh and the
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
    public static Dictionary<RecordKey, List<RecordKey>> IndexNavmeshesLinkingTo(IEnumerable<KeyValuePair<RecordKey, NavmeshVersion>> navmeshes) =>
        IndexLinkingTo(navmeshes, version => (version.Record.Data?.EdgeLinks.Select(link => link.Mesh.FormKey.KeyAsWritten()) ?? []).Distinct());

    /// <summary>For each navmesh, the given doors whose navmesh link names it, in key order.</summary>
    public static Dictionary<RecordKey, List<RecordKey>> IndexDoorsLinkingTo(IEnumerable<KeyValuePair<RecordKey, NavDoorLinkFact>> links) =>
        IndexLinkingTo(links, link => [link.Navmesh]);

    /// <summary>For each navmesh the given records link to, the records that link to it, in key order.</summary>
    /// <param name="linkedOf">The navmeshes a record links to, each once.</param>
    private static Dictionary<RecordKey, List<RecordKey>> IndexLinkingTo<T>(IEnumerable<KeyValuePair<RecordKey, T>> records, Func<T, IEnumerable<RecordKey>> linkedOf)
    {
        var index = new Dictionary<RecordKey, List<RecordKey>>();
        foreach (var (key, record) in records.OrderBy(entry => entry.Key, RecordKeyOrder.Comparer))
        {
            foreach (var navmesh in linkedOf(record)) KeyedGroups.GetOrAddList(index, navmesh).Add(key);
        }
        return index;
    }
}
