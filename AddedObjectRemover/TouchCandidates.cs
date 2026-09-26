using System.Collections.Concurrent;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>Two target objects whose grown oriented boxes overlap; First &lt; Second.</summary>
internal readonly record struct CandidatePair(int First, int Second);

/// <summary>A node's neighbor in the candidate graph and the index of the pair joining them.</summary>
internal readonly record struct CandidateNeighbor(int Node, int Pair);

/// <summary>
/// The broad-phase candidate graph: pairs sorted by (First, Second), and per node its neighbors
/// sorted by node index. Pairs are spatially local in this order, because targets are listed in
/// scan order (cell by cell).
/// </summary>
internal sealed class TouchCandidates(List<CandidatePair> pairs, List<CandidateNeighbor>?[] neighbors)
{
    public IReadOnlyList<CandidatePair> Pairs { get; } = pairs;

    public IReadOnlyList<CandidateNeighbor> NeighborsOf(int node) => neighbors[node] ?? [];
}

/// <summary>
/// Broad phase of the touch search: each target's oriented box grown by the tolerance, tested
/// against nearby targets of the same space (spatial hash of world AABBs, then an exact oriented
/// box test). Only spaces that contain a seed are indexed.
/// </summary>
internal sealed class TouchCandidateFinder
{
    private readonly IReadOnlyList<TargetObject> _targets;
    private readonly bool[] _excluded;
    private readonly float _tolerance;
    private readonly ParallelOptions _parallelOptions;
    private readonly OrientedBox[] _boxes;
    private readonly bool[] _indexed;
    private readonly Dictionary<FormKey, (SpatialGrid Grid, List<int> Members)> _grids = new();

    private TouchCandidateFinder(IReadOnlyList<TargetObject> targets, bool[] excluded, float tolerance, ParallelOptions parallelOptions)
    {
        _targets = targets;
        _excluded = excluded;
        _tolerance = tolerance;
        _parallelOptions = parallelOptions;
        _boxes = new OrientedBox[targets.Count];
        _indexed = new bool[targets.Count];
    }

    /// <param name="excluded">Targets that never take part in a pair.</param>
    public static TouchCandidateFinder Create(
        IReadOnlyList<TargetObject> targets,
        HashSet<FormKey> seedSpaces,
        bool[] excluded,
        BaseObjectShapeProvider shapes,
        float tolerance,
        ParallelOptions parallelOptions)
    {
        var finder = new TouchCandidateFinder(targets, excluded, tolerance, parallelOptions);
        finder.BuildBoxes(seedSpaces, shapes);
        finder.BuildGrids();
        return finder;
    }

    public TouchCandidates FindPairs()
    {
        var partnersOf = new List<int>[_targets.Count];
        Parallel.For(
            0,
            _targets.Count,
            _parallelOptions,
            () => (Scratch: new List<int>(), Seen: new HashSet<int>()),
            (node, _, local) =>
            {
                partnersOf[node] = _indexed[node] && !_excluded[node] ? FindLaterOverlapping(node, local.Scratch, local.Seen) : [];
                return local;
            },
            _ => { });
        return AssembleGraph(partnersOf);
    }

    private void BuildBoxes(HashSet<FormKey> seedSpaces, BaseObjectShapeProvider shapes)
    {
        Parallel.ForEach(Partitioner.Create(0, _targets.Count), _parallelOptions, range =>
        {
            for (var i = range.Item1; i < range.Item2; i++)
            {
                if (!seedSpaces.Contains(_targets[i].SpaceKey)) continue;
                _boxes[i] = OrientedBox.FromLocal(shapes.GetLocalBox(_targets[i].Base), _targets[i].Transform);
                _indexed[i] = true;
            }
        });
    }

    /// <summary>Per-space grids of grown world AABBs of the indexed targets.</summary>
    private void BuildGrids()
    {
        var membersBySpace = new Dictionary<FormKey, List<int>>();
        for (var i = 0; i < _targets.Count; i++)
        {
            if (!_indexed[i]) continue;
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

    /// <returns>Sorted indices greater than <paramref name="node"/> whose boxes overlap its grown box.</returns>
    private List<int> FindLaterOverlapping(int node, List<int> scratch, HashSet<int> seen)
    {
        var (grid, members) = _grids[_targets[node].SpaceKey];
        scratch.Clear();
        seen.Clear();
        grid.Collect(_boxes[node].WorldAabb(_tolerance), scratch);
        var found = new List<int>();
        foreach (var slot in scratch)
        {
            var other = members[slot];
            if (other <= node || _excluded[other] || !seen.Add(other)) continue;
            if (_boxes[node].Intersects(_boxes[other], _tolerance)) found.Add(other);
        }
        found.Sort();
        return found;
    }

    /// <remarks>
    /// Pairs are appended in (First, Second) order, so every neighbor list comes out sorted: a
    /// node first receives its smaller neighbors (as Second, in First order), then its larger ones.
    /// </remarks>
    private TouchCandidates AssembleGraph(List<int>[] partnersOf)
    {
        var pairs = new List<CandidatePair>();
        var neighbors = new List<CandidateNeighbor>?[_targets.Count];
        for (var first = 0; first < partnersOf.Length; first++)
        {
            foreach (var second in partnersOf[first])
            {
                var pair = pairs.Count;
                pairs.Add(new CandidatePair(first, second));
                (neighbors[first] ??= []).Add(new CandidateNeighbor(second, pair));
                (neighbors[second] ??= []).Add(new CandidateNeighbor(first, pair));
            }
        }
        return new TouchCandidates(pairs, neighbors);
    }
}
