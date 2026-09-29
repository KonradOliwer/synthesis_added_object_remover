using System.Collections.Concurrent;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>
/// Broad phase of the touch search: a target's oriented box grown by the tolerance, tested against
/// nearby targets of the same space (spatial hash of grown world AABBs, then an exact oriented box
/// test). Only targets in the given spaces and not excluded take part. Read-only after
/// creation and safe to query from many threads at once.
/// </summary>
internal sealed class TouchCandidateFinder
{
    private readonly IReadOnlyList<TargetObject> _targets;
    private readonly float _tolerance;
    private readonly OrientedBox[] _boxes;
    private readonly bool[] _included;
    private readonly Dictionary<FormKey, (SpatialGrid Grid, List<int> Members)> _grids = new();

    private TouchCandidateFinder(IReadOnlyList<TargetObject> targets, float tolerance)
    {
        _targets = targets;
        _tolerance = tolerance;
        _boxes = new OrientedBox[targets.Count];
        _included = new bool[targets.Count];
    }

    /// <param name="spaces">The spaces whose targets take part.</param>
    /// <param name="excluded">Targets that never take part in a pair.</param>
    public static TouchCandidateFinder Create(
        IReadOnlyList<TargetObject> targets,
        IReadOnlySet<FormKey> spaces,
        bool[] excluded,
        ShapeCatalog shapes,
        float tolerance,
        ParallelOptions parallelOptions)
    {
        var finder = new TouchCandidateFinder(targets, tolerance);
        finder.BuildBoxes(spaces, excluded, shapes, parallelOptions);
        finder.BuildGrids();
        return finder;
    }

    /// <summary>World oriented box of an included target.</summary>
    public OrientedBox BoxOf(int target) => _boxes[target];

    /// <returns>Sorted indices of the other included targets whose boxes come within the tolerance of <paramref name="node"/>'s box; empty for a target that is not included.</returns>
    public List<int> FindNeighbors(int node)
    {
        if (!_included[node]) return [];

        var (grid, members) = _grids[_targets[node].SpaceKey];
        var slots = new List<int>();
        grid.Collect(_boxes[node].WorldAabb(_tolerance), slots);
        var neighbors = slots
            .Select(slot => members[slot])
            .Distinct()
            .Where(other => other != node && AreBoxesClose(node, other))
            .ToList();
        neighbors.Sort();
        return neighbors;
    }

    /// <remarks>The oriented box test grows only one box, so it always grows the lower index's box to give each pair one answer.</remarks>
    private bool AreBoxesClose(int a, int b) =>
        _boxes[Math.Min(a, b)].Intersects(_boxes[Math.Max(a, b)], _tolerance);

    private void BuildBoxes(IReadOnlySet<FormKey> spaces, bool[] excluded, ShapeCatalog shapes, ParallelOptions parallelOptions)
    {
        Parallel.ForEach(Partitioner.Create(0, _targets.Count), parallelOptions, range =>
        {
            for (var i = range.Item1; i < range.Item2; i++)
            {
                if (excluded[i] || !spaces.Contains(_targets[i].SpaceKey)) continue;
                _boxes[i] = OrientedBox.FromLocal(shapes.GetLocalBox(_targets[i].Base), _targets[i].Transform);
                _included[i] = true;
            }
        });
    }

    /// <summary>Per-space grids of the included targets' grown world AABBs.</summary>
    private void BuildGrids()
    {
        var membersBySpace = new Dictionary<FormKey, List<int>>();
        for (var i = 0; i < _targets.Count; i++)
        {
            if (!_included[i]) continue;
            if (!membersBySpace.TryGetValue(_targets[i].SpaceKey, out var members))
            {
                members = [];
                membersBySpace[_targets[i].SpaceKey] = members;
            }
            members.Add(i);
        }
        foreach (var (spaceKey, members) in membersBySpace)
        {
            var aabbs = members.Select(i => _boxes[i].WorldAabb(_tolerance)).ToArray();
            _grids[spaceKey] = (SpatialGrid.FromBoxes(aabbs), members);
        }
    }
}
