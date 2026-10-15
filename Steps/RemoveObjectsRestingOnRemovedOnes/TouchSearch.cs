using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

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
        MeshPairTester tester,
        ITriangleMeshes cache,
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

    public MeshPairTester Tester { get; }

    public ITriangleMeshes Cache { get; }

    /// <param name="spaces">The spaces whose targets take part.</param>
    /// <param name="excluded">Targets that never take part, in addition to those without a mesh or not visible.</param>
    public static TouchSearch Create(
        IReadOnlyList<TargetObject> targets,
        IReadOnlySet<RecordKey> spaces,
        IReadOnlyList<int> excluded,
        IBaseObjectShapes shapes,
        ITriangleMeshes cache,
        float tolerance,
        Execution execution)
    {
        var meshPaths = new TargetMeshPaths(targets, shapes);
        var candidateFinder = TouchCandidateFinder.Create(
            targets,
            spaces,
            MarkExcluded(targets, shapes, excluded, meshPaths),
            shapes,
            tolerance,
            execution);
        var tester = new MeshPairTester(cache, meshPaths.Get, target => targets[target].Transform, tolerance);
        return new TouchSearch(meshPaths, candidateFinder, tester, cache, execution);
    }

    /// <remarks>
    /// An invisible target (marker, light, sound emitter, ...) can still have a mesh, for example an
    /// idle marker or a map marker reference; it neither holds up nor touches anything in game.
    /// </remarks>
    internal static bool[] MarkExcluded(
        IReadOnlyList<TargetObject> targets, IBaseObjectShapes shapes, IReadOnlyList<int> excluded, TargetMeshPaths meshPaths)
    {
        var marks = new bool[targets.Count];
        for (var i = 0; i < marks.Length; i++) marks[i] = !meshPaths.HasMesh(i) || !shapes.VisibilityOf(targets[i]).IsVisible;
        foreach (var target in excluded) marks[target] = true;
        return marks;
    }

    /// <summary>
    /// The undecided objects whose mesh touches an object removed in the previous round, each paired with the first
    /// removed object (in target order, then neighbour order) that touches it.
    /// </summary>
    public (List<IndexPair> Found, PairTestStats Work) FindUndecidedTouchingRemoved(
        IRemovalDecisions decisions, ImmutableArray<TargetId> removedLastRound, bool alsoWhenCentreEnclosed)
    {
        var frontier = removedLastRound.Order().Select(target => target.Index).ToList();
        var pairs = CollectFrontierPairs(frontier, skip: node => decisions.IsDecided(new TargetId(node)));
        return Tester.FindFirstInContact(pairs, alsoWhenCentreEnclosed, _execution);
    }

    /// <returns>(frontier node, neighbor) pairs in frontier order, then neighbor order, without the neighbors <paramref name="skip"/> accepts.</returns>
    public List<IndexPair> CollectFrontierPairs(IReadOnlyList<int> frontier, Func<int, bool> skip)
    {
        var neighbors = FindNeighborsOfAll(frontier);
        var pairs = new List<IndexPair>();
        for (var i = 0; i < frontier.Count; i++)
        {
            foreach (var neighbor in neighbors[i])
            {
                if (!skip(neighbor)) pairs.Add(new IndexPair(frontier[i], neighbor));
            }
        }
        return pairs;
    }

    public List<int>[] FindNeighborsOfAll(IReadOnlyList<int> nodes)
    {
        return ParallelMap.Run(_execution, nodes.Count, i => CandidateFinder.FindNeighbors(nodes[i]), ParallelMap.AutomaticRangeSize);
    }
}
