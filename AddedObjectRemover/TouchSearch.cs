using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>
/// The touch search among target objects of the follow-up removal: the broad phase over the given
/// spaces, the narrow phase, and the triangle trees they read. Targets without a mesh or not visible
/// never take part.
/// </summary>
internal sealed class TouchSearch
{
    private readonly Execution _execution;

    private TouchSearch(
        TargetMeshPaths meshPaths,
        TouchCandidateFinder candidateFinder,
        TouchPairTester tester,
        TriangleStore cache,
        Execution execution)
    {
        MeshPaths = meshPaths;
        CandidateFinder = candidateFinder;
        Tester = tester;
        Cache = cache;
        _execution = execution;
    }

    public TargetMeshPaths MeshPaths { get; }

    public TouchCandidateFinder CandidateFinder { get; }

    public TouchPairTester Tester { get; }

    public TriangleStore Cache { get; }

    /// <param name="looks">Parallel to <paramref name="targets"/>.</param>
    /// <param name="spaces">The spaces whose targets take part.</param>
    /// <param name="excluded">Targets that never take part, in addition to those without a mesh or not visible.</param>
    public static TouchSearch Create(
        IReadOnlyList<TargetObject> targets,
        TargetLooks looks,
        IReadOnlySet<FormKey> spaces,
        IReadOnlyList<int> excluded,
        ShapeCatalog shapes,
        TriangleStore cache,
        float tolerance,
        Execution execution)
    {
        var meshPaths = new TargetMeshPaths(targets, shapes);
        var candidateFinder = TouchCandidateFinder.Create(
            targets,
            spaces,
            MarkExcluded(looks, excluded, meshPaths),
            shapes,
            tolerance,
            execution);
        var tester = new TouchPairTester(targets, meshPaths, cache, tolerance);
        return new TouchSearch(meshPaths, candidateFinder, tester, cache, execution);
    }

    /// <remarks>
    /// An invisible target (marker, light, sound emitter, ...) can still have a mesh, for example an
    /// idle marker or a map marker reference; it neither holds up nor touches anything in game.
    /// </remarks>
    internal static bool[] MarkExcluded(TargetLooks looks, IReadOnlyList<int> excluded, TargetMeshPaths meshPaths)
    {
        var marks = new bool[looks.ByTarget.Length];
        for (var i = 0; i < marks.Length; i++) marks[i] = !meshPaths.HasMesh(i) || !looks.ByTarget[i].IsVisible;
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
        return ParallelMap.Run(_execution, nodes.Count, i => CandidateFinder.FindNeighbors(nodes[i]));
    }
}
