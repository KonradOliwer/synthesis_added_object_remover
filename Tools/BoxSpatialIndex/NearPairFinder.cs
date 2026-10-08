namespace AddedObjectRemover;

/// <summary>
/// For each oriented box, the other boxes of its group that come within a tolerance of it: a grid of
/// the grown world AABBs first, then an exact oriented box test. Excluded boxes take part in no
/// pair. Read-only after creation and safe to query from many threads at once.
/// </summary>
public sealed class NearPairFinder
{
    private readonly IReadOnlyList<OrientedBox> _boxes;
    private readonly Func<int, RecordKey> _groupOf;
    private readonly float _tolerance;
    private readonly bool[] _excluded;
    private readonly Dictionary<RecordKey, (SpatialGrid Grid, List<int> Members)> _grids = new();

    private NearPairFinder(IReadOnlyList<OrientedBox> boxes, Func<int, RecordKey> groupOf, float tolerance, bool[] excluded)
    {
        _boxes = boxes;
        _groupOf = groupOf;
        _tolerance = tolerance;
        _excluded = excluded;
    }

    /// <param name="boxes">Box i belongs to item i; the box of an excluded item is never read.</param>
    /// <param name="groupOf">Only boxes of the same group can be neighbours.</param>
    /// <param name="excluded">Parallel to <paramref name="boxes"/>.</param>
    public static NearPairFinder Create(IReadOnlyList<OrientedBox> boxes, Func<int, RecordKey> groupOf, float tolerance, bool[] excluded)
    {
        var finder = new NearPairFinder(boxes, groupOf, tolerance, excluded);
        finder.BuildGrids();
        return finder;
    }

    /// <returns>Sorted indices of the other boxes whose box comes within the tolerance of box <paramref name="node"/>; empty for an excluded item.</returns>
    public List<int> NeighboursOf(int node)
    {
        if (_excluded[node]) return [];

        var (grid, members) = _grids[_groupOf(node)];
        var slots = new List<int>();
        grid.Collect(_boxes[node].WorldAabb(_tolerance), slots);
        var neighbours = slots
            .Select(slot => members[slot])
            .Distinct()
            .Where(other => other != node && AreClose(node, other))
            .ToList();
        neighbours.Sort();
        return neighbours;
    }

    /// <remarks>The oriented box test grows only one box, so it always grows the lower index's box to give each pair one answer.</remarks>
    private bool AreClose(int a, int b) =>
        _boxes[Math.Min(a, b)].Intersects(_boxes[Math.Max(a, b)], _tolerance);

    /// <summary>Per-group grids of the included boxes' grown world AABBs.</summary>
    private void BuildGrids()
    {
        var included = Enumerable.Range(0, _boxes.Count).Where(i => !_excluded[i]).ToList();
        foreach (var (group, positions) in KeyedGroups.IndicesByKey(included, _groupOf, EqualityComparer<RecordKey>.Default))
        {
            var members = positions.Select(position => included[position]).ToList();
            var aabbs = members.Select(i => _boxes[i].WorldAabb(_tolerance)).ToArray();
            _grids[group] = (SpatialGrid.FromBoxes(aabbs), members);
        }
    }
}
