using System.Diagnostics;

namespace AddedObjectRemover;

internal sealed record TouchStats(
    int Components,
    int ComponentsWithTouching,
    int LargestComponent,
    int CandidatePairs,
    int PairsTested,
    int TouchingPairs,
    int PairsWithoutGeometry,
    long TrianglePairsTested,
    TriangleTreeStats Meshes,
    TimeSpan Setup,
    TimeSpan BroadPhase,
    TimeSpan NarrowPhase,
    TimeSpan Clusters);

internal sealed record TouchClusters(List<TouchingRemoval> Removals, List<KeptTarget> Kept, TouchStats Stats, TouchDiagnosticsData? Diagnostics);

/// <summary>
/// Raw exploration data kept only when touch diagnostics output was requested (see
/// <see cref="TouchDiagnosticsWriter"/>); otherwise null and nothing extra is tracked.
/// </summary>
/// <param name="ComponentId">Target index -&gt; component id, or -1 if never reached.</param>
/// <param name="ParentOf">Target index -&gt; the target index it was reached through, or -1 for the component's first (root) seed.</param>
/// <param name="Depth">Target index -&gt; its distance (edge count) from the component's root seed.</param>
/// <param name="ComponentMembers">Component id -&gt; every target index reached in it (seed, touch-removed or kept), in discovery order.</param>
internal sealed record TouchDiagnosticsData(
    int[] ComponentId,
    int[] ParentOf,
    int[] Depth,
    List<List<int>> ComponentMembers,
    TouchCandidates Candidates,
    PairTouch[] PairResults);

/// <summary>
/// Connected components of the "touches" graph (target objects of one space) that contain a
/// too-close removal. The graph is built first: broad phase over all targets of the seeds'
/// spaces, then the narrow phase on every candidate pair the search can reach (see
/// <see cref="SeedReachability"/>). The components are then explored breadth first from each
/// seed in removal order; within a level, frontier nodes are taken in order and each reached
/// node is attributed to the first frontier node (in neighbor order) that touches it, so the
/// result is deterministic. Touching is decided from mesh triangles only, so a pair where either
/// object has no mesh geometry never touches. Referenced objects stay and do not propagate.
/// </summary>
internal sealed class TouchClusterFinder
{
    private readonly IReadOnlyList<TargetObject> _targets;
    private readonly KeepReferencedRule _keepRule;
    private readonly TouchCandidates _candidates;
    private readonly PairTouch[] _pairResults;
    private readonly bool[] _isSeed;
    private readonly bool[] _visited;

    private readonly List<TouchingRemoval> _removals = [];
    private readonly List<KeptTarget> _kept = [];
    private int _components;
    private int _componentsWithTouching;
    private int _largestComponent;

    // Diagnostics-only bookkeeping; null (and never written to) unless requested.
    private readonly int[]? _componentId;
    private readonly int[]? _parentOf;
    private readonly int[]? _depth;
    private readonly List<List<int>>? _componentMembers;

    private TouchClusterFinder(
        IReadOnlyList<TargetObject> targets,
        KeepReferencedRule keepRule,
        TouchCandidates candidates,
        PairTouch[] pairResults,
        bool[] isSeed,
        bool[] visited,
        bool collectDiagnostics)
    {
        _targets = targets;
        _keepRule = keepRule;
        _candidates = candidates;
        _pairResults = pairResults;
        _isSeed = isSeed;
        _visited = visited;

        if (!collectDiagnostics) return;
        _componentId = new int[targets.Count];
        Array.Fill(_componentId, -1);
        _parentOf = new int[targets.Count];
        Array.Fill(_parentOf, -1);
        _depth = new int[targets.Count];
        _componentMembers = [];
    }

    /// <param name="seeds">Target indices of the too-close removals, in removal order.</param>
    /// <param name="keptTooClose">Too-close targets kept as referenced: already logged and counted, never propagated.</param>
    public static TouchClusters Find(
        IReadOnlyList<TargetObject> targets,
        IReadOnlyList<int> seeds,
        IReadOnlyList<int> keptTooClose,
        BaseObjectShapeProvider shapes,
        KeepReferencedRule keepRule,
        float tolerance,
        ParallelOptions parallelOptions,
        bool collectDiagnostics = false)
    {
        var isSeed = MarkAll(targets.Count, seeds);
        var keptBefore = MarkAll(targets.Count, keptTooClose);

        var setupTimer = Stopwatch.StartNew();
        var seedSpaces = seeds.Select(seed => targets[seed].SpaceKey).ToHashSet();
        var candidateFinder = TouchCandidateFinder.Create(targets, seedSpaces, keptBefore, shapes, tolerance, parallelOptions);
        setupTimer.Stop();

        var broadTimer = Stopwatch.StartNew();
        var candidates = candidateFinder.FindPairs();
        var propagates = FindPropagatingTargets(targets, isSeed, keepRule);
        var needed = SeedReachability.FindPairsToTest(candidates, propagates, seeds);
        broadTimer.Stop();

        var narrowTimer = Stopwatch.StartNew();
        var narrow = TouchPairTester.TestPairs(targets, candidates, needed, shapes, tolerance, parallelOptions);
        narrowTimer.Stop();

        var clusterTimer = Stopwatch.StartNew();
        var finder = new TouchClusterFinder(
            targets, keepRule, candidates, narrow.Results, isSeed, visited: (bool[])keptBefore.Clone(), collectDiagnostics);
        finder.ExploreComponents(seeds);
        clusterTimer.Stop();

        var stats = finder.CreateStats(candidates, narrow, setupTimer.Elapsed, broadTimer.Elapsed, narrowTimer.Elapsed, clusterTimer.Elapsed);
        var diagnostics = collectDiagnostics
            ? new TouchDiagnosticsData(finder._componentId!, finder._parentOf!, finder._depth!, finder._componentMembers!, candidates, narrow.Results)
            : null;
        return new TouchClusters(finder._removals, finder._kept, stats, diagnostics);
    }

    private static bool[] MarkAll(int count, IEnumerable<int> indices)
    {
        var marked = new bool[count];
        foreach (var index in indices) marked[index] = true;
        return marked;
    }

    /// <summary>Seeds and targets the keep rule would not keep: the only nodes that can pass a removal on.</summary>
    private static bool[] FindPropagatingTargets(IReadOnlyList<TargetObject> targets, bool[] isSeed, KeepReferencedRule keepRule)
    {
        var propagates = new bool[targets.Count];
        for (var i = 0; i < targets.Count; i++) propagates[i] = isSeed[i] || !keepRule.TryGetKeepReason(targets[i], out _);
        return propagates;
    }

    private void ExploreComponents(IReadOnlyList<int> seeds)
    {
        foreach (var seed in seeds)
        {
            // A seed reached from an earlier seed already belongs to that seed's component.
            if (_visited[seed]) continue;
            _visited[seed] = true;
            RecordMember(_components, seed, parent: -1, depth: 0);

            var componentSize = ExploreComponent(seed, _components);
            _components++;
            if (componentSize > 1) _componentsWithTouching++;
            _largestComponent = Math.Max(_largestComponent, componentSize);
        }
    }

    /// <summary>Diagnostics-only; a no-op unless diagnostics collection was requested.</summary>
    private void RecordMember(int componentId, int node, int parent, int depth)
    {
        if (_componentId == null) return;
        _componentId[node] = componentId;
        _parentOf![node] = parent;
        _depth![node] = depth;
        while (_componentMembers!.Count <= componentId) _componentMembers.Add([]);
        _componentMembers[componentId].Add(node);
    }

    /// <returns>Number of removed objects in the component, the seed included.</returns>
    private int ExploreComponent(int seed, int componentId)
    {
        var componentSize = 1;
        var frontier = new List<int> { seed };
        while (frontier.Count > 0)
        {
            var reached = ReachUnvisitedTouching(frontier);
            frontier = MergeLevel(reached, componentId, ref componentSize);
        }
        return componentSize;
    }

    /// <summary>Unvisited nodes touching a frontier node, each with the first frontier node that touches it; marks them visited.</summary>
    private List<(int From, int To)> ReachUnvisitedTouching(List<int> frontier)
    {
        var reached = new List<(int From, int To)>();
        foreach (var from in frontier)
        {
            foreach (var neighbor in _candidates.NeighborsOf(from))
            {
                if (_visited[neighbor.Node] || !IsTouching(neighbor.Pair)) continue;
                _visited[neighbor.Node] = true;
                reached.Add((from, neighbor.Node));
            }
        }
        return reached;
    }

    private bool IsTouching(int pair) => _pairResults[pair] switch
    {
        PairTouch.Touching => true,
        PairTouch.Untested => throw new UnreachableException($"Candidate pair {pair} is reachable from a seed but was not tested."),
        _ => false,
    };

    /// <summary>Applies the level's reached nodes in order and returns the next frontier.</summary>
    private List<int> MergeLevel(List<(int From, int To)> reached, int componentId, ref int componentSize)
    {
        var next = new List<int>();
        foreach (var (from, to) in reached)
        {
            var depth = (_depth?[from] ?? 0) + 1;
            if (_isSeed[to])
            {
                // Another too-close removal: same component, already removed.
                componentSize++;
                RecordMember(componentId, to, from, depth);
                next.Add(to);
                continue;
            }

            if (_keepRule.TryGetKeepReason(_targets[to], out var keepReason))
            {
                _kept.Add(new KeptTarget(to, keepReason, TouchedTargetIndex: from));
                RecordMember(componentId, to, from, depth);
                continue;
            }

            _removals.Add(new TouchingRemoval(to, TouchedTargetIndex: from));
            RecordMember(componentId, to, from, depth);
            componentSize++;
            next.Add(to);
        }
        return next;
    }

    private TouchStats CreateStats(
        TouchCandidates candidates,
        NarrowPhaseResult narrow,
        TimeSpan setup,
        TimeSpan broadPhase,
        TimeSpan narrowPhase,
        TimeSpan clusters) => new(
        _components,
        _componentsWithTouching,
        _largestComponent,
        candidates.Pairs.Count,
        narrow.PairsTested,
        narrow.Results.Count(r => r == PairTouch.Touching),
        narrow.Results.Count(r => r == PairTouch.NoGeometry),
        narrow.TrianglePairsTested,
        narrow.Meshes,
        setup,
        broadPhase,
        narrowPhase,
        clusters);
}
