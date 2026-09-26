namespace AddedObjectRemover;

/// <summary>
/// The touch search among target objects shared by AnyTouch and Anchoring: the broad phase over the
/// spaces that contain a seed, the narrow phase, and the triangle trees they read. Targets without
/// a mesh never take part.
/// </summary>
internal sealed class TouchSearch
{
    private readonly ParallelOptions _parallelOptions;

    private TouchSearch(
        TargetMeshPaths meshPaths,
        TouchCandidateFinder candidateFinder,
        TouchPairTester tester,
        TriangleTreeCache cache,
        ParallelOptions parallelOptions)
    {
        MeshPaths = meshPaths;
        CandidateFinder = candidateFinder;
        Tester = tester;
        Cache = cache;
        _parallelOptions = parallelOptions;
    }

    public TargetMeshPaths MeshPaths { get; }

    public TouchCandidateFinder CandidateFinder { get; }

    public TouchPairTester Tester { get; }

    public TriangleTreeCache Cache { get; }

    /// <param name="excluded">Targets that never take part, in addition to those without a mesh.</param>
    public static TouchSearch Create(
        IReadOnlyList<TargetObject> targets,
        IReadOnlyList<int> seeds,
        IReadOnlyList<int> excluded,
        BaseObjectShapeProvider shapes,
        float tolerance,
        ParallelOptions parallelOptions)
    {
        var meshPaths = new TargetMeshPaths(targets, shapes);
        var cache = new TriangleTreeCache(shapes.ReadGeometry);
        var candidateFinder = TouchCandidateFinder.Create(
            targets,
            seeds.Select(seed => targets[seed].SpaceKey).ToHashSet(),
            MarkExcluded(targets.Count, excluded, meshPaths),
            shapes,
            tolerance,
            parallelOptions);
        var tester = new TouchPairTester(targets, meshPaths, cache, tolerance);
        return new TouchSearch(meshPaths, candidateFinder, tester, cache, parallelOptions);
    }

    private static bool[] MarkExcluded(int count, IReadOnlyList<int> excluded, TargetMeshPaths meshPaths)
    {
        var marks = new bool[count];
        for (var i = 0; i < count; i++) marks[i] = !meshPaths.HasMesh(i);
        foreach (var target in excluded) marks[target] = true;
        return marks;
    }

    /// <returns>(frontier node, neighbor) pairs in frontier order, then neighbor order, without the neighbors <paramref name="skip"/> accepts.</returns>
    public List<TargetPair> CollectFrontierPairs(IReadOnlyList<int> frontier, Func<int, bool> skip)
    {
        var neighbors = FindNeighborsOfAll(frontier);
        var pairs = new List<TargetPair>();
        for (var i = 0; i < frontier.Count; i++)
        {
            foreach (var neighbor in neighbors[i])
            {
                if (!skip(neighbor)) pairs.Add(new TargetPair(frontier[i], neighbor));
            }
        }
        return pairs;
    }

    public List<int>[] FindNeighborsOfAll(IReadOnlyList<int> nodes)
    {
        var neighbors = new List<int>[nodes.Count];
        Parallel.For(0, nodes.Count, _parallelOptions, i => neighbors[i] = CandidateFinder.FindNeighbors(nodes[i]));
        return neighbors;
    }
}
