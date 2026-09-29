using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>
/// The touch search among target objects of the follow-up removal: the broad phase over the given
/// spaces, the narrow phase, and the triangle trees they read. Targets without a mesh or not visible
/// never take part.
/// </summary>
internal sealed class TouchSearch
{
    private readonly ParallelOptions _parallelOptions;

    private TouchSearch(
        TargetMeshPaths meshPaths,
        TouchCandidateFinder candidateFinder,
        TouchPairTester tester,
        TriangleStore cache,
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

    public TriangleStore Cache { get; }

    /// <param name="visibility">Parallel to <paramref name="targets"/>.</param>
    /// <param name="spaces">The spaces whose targets take part.</param>
    /// <param name="excluded">Targets that never take part, in addition to those without a mesh or not visible.</param>
    public static TouchSearch Create(
        IReadOnlyList<TargetObject> targets,
        IReadOnlyList<ObjectVisibility> visibility,
        IReadOnlySet<FormKey> spaces,
        IReadOnlyList<int> excluded,
        ShapeCatalog shapes,
        TriangleStore cache,
        float tolerance,
        ParallelOptions parallelOptions)
    {
        var meshPaths = new TargetMeshPaths(targets, shapes);
        var candidateFinder = TouchCandidateFinder.Create(
            targets,
            spaces,
            MarkExcluded(visibility, excluded, meshPaths),
            shapes,
            tolerance,
            parallelOptions);
        var tester = new TouchPairTester(targets, meshPaths, cache, tolerance);
        return new TouchSearch(meshPaths, candidateFinder, tester, cache, parallelOptions);
    }

    /// <remarks>
    /// An invisible target (marker, light, sound emitter, ...) can still have a mesh, for example an
    /// idle marker or a map marker reference; it neither holds up nor touches anything in game.
    /// </remarks>
    internal static bool[] MarkExcluded(IReadOnlyList<ObjectVisibility> visibility, IReadOnlyList<int> excluded, TargetMeshPaths meshPaths)
    {
        var marks = new bool[visibility.Count];
        for (var i = 0; i < marks.Length; i++) marks[i] = !meshPaths.HasMesh(i) || !visibility[i].IsVisible;
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
