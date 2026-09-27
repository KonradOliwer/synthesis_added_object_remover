namespace AddedObjectRemover;

internal sealed record TouchStats(
    int Components,
    int ComponentsWithTouching,
    int LargestComponent,
    int Levels,
    PairTestStats Pairs,
    TimeSpan Setup,
    TimeSpan BroadPhase,
    TimeSpan NarrowPhase,
    TimeSpan DiagnosticsEdges);

internal sealed record TouchClusters(List<TouchingRemoval> Removals, List<KeptTarget> Kept, TouchStats Stats, TouchDiagnosticsData? Diagnostics);

/// <summary>A touching pair of targets explored in the same component; First &lt; Second.</summary>
internal readonly record struct TouchEdge(int ComponentId, TargetPair Pair, float MinSurfaceDistance);

/// <param name="ComponentId">Target index -&gt; component id, or -1 if never reached.</param>
/// <param name="ParentOf">Target index -&gt; the target index it was reached through, or -1 for the component's first (root) seed.</param>
/// <param name="Depth">Target index -&gt; its distance (edge count) from the component's root seed.</param>
/// <param name="ComponentMembers">Component id -&gt; every target index reached in it (seed, touch-removed or kept), in discovery order.</param>
/// <param name="Edges">Every touching pair with both ends in the same component.</param>
internal sealed record TouchDiagnosticsData(
    int[] ComponentId,
    int[] ParentOf,
    int[] Depth,
    List<List<int>> ComponentMembers,
    List<TouchEdge> Edges);

/// <summary>
/// Connected components of the "touches" graph (target objects of one space) that contain a
/// too-close removal, explored breadth first from each seed in removal order. Each level tests,
/// in parallel, only the candidate pairs between the current frontier and unvisited targets; the
/// results are then applied in frontier order, and each reached node is attributed to the first
/// frontier node (in neighbor order) that touches it, so the result is deterministic. Touching is
/// decided from mesh triangles only, so targets without a mesh never take part, and neither do
/// invisible ones. Referenced
/// objects stay and do not propagate.
/// </summary>
internal sealed class TouchClusterFinder
{
    private readonly KeepReferencedRule _keepRule;
    private readonly TouchSearch _search;
    private readonly ParallelOptions _parallelOptions;
    private readonly bool[] _isSeed;
    private readonly bool[] _visited;

    private readonly int[] _componentId;
    private readonly int[] _parentOf;
    private readonly int[] _depth;
    private readonly List<List<int>> _componentMembers = [];

    private readonly List<TouchingRemoval> _removals = [];
    private readonly List<KeptTarget> _kept = [];
    private int _componentsWithTouching;
    private int _largestComponent;
    private int _levels;
    private TimeSpan _broadPhase;
    private TimeSpan _narrowPhase;

    private TouchClusterFinder(
        int targetCount,
        KeepReferencedRule keepRule,
        TouchSearch search,
        ParallelOptions parallelOptions,
        bool[] isSeed,
        bool[] visited)
    {
        _keepRule = keepRule;
        _search = search;
        _parallelOptions = parallelOptions;
        _isSeed = isSeed;
        _visited = visited;
        _componentId = new int[targetCount];
        Array.Fill(_componentId, -1);
        _parentOf = new int[targetCount];
        Array.Fill(_parentOf, -1);
        _depth = new int[targetCount];
    }

    /// <param name="visibility">Parallel to <paramref name="targets"/>.</param>
    /// <param name="seeds">Target indices of the earlier removals (too close, and their linked groups), in removal order.</param>
    /// <param name="keptTooClose">Too-close targets kept as referenced: already logged and counted, never propagated.</param>
    public static TouchClusters Find(
        IReadOnlyList<TargetObject> targets,
        IReadOnlyList<ObjectVisibility> visibility,
        IReadOnlyList<int> seeds,
        IReadOnlyList<int> keptTooClose,
        BaseObjectShapeProvider shapes,
        TriangleTreeCache meshCache,
        KeepReferencedRule keepRule,
        float tolerance,
        ParallelOptions parallelOptions,
        bool collectDiagnostics)
    {
        var (search, setup) = Timing.Measure(() => TouchSearch.Create(
            targets, visibility, seeds, excluded: keptTooClose, shapes, meshCache, tolerance, parallelOptions));
        var finder = new TouchClusterFinder(
            targets.Count,
            keepRule,
            search,
            parallelOptions,
            MarkAll(targets.Count, seeds),
            visited: MarkAll(targets.Count, keptTooClose));
        finder.ExploreComponents(seeds);
        return finder.CreateClusters(setup, collectDiagnostics);
    }

    private static bool[] MarkAll(int count, IEnumerable<int> indices)
    {
        var marked = new bool[count];
        foreach (var index in indices) marked[index] = true;
        return marked;
    }

    private void ExploreComponents(IReadOnlyList<int> seeds)
    {
        foreach (var seed in seeds)
        {
            // A seed reached from an earlier seed already belongs to that seed's component.
            if (_visited[seed]) continue;
            _visited[seed] = true;
            var componentId = _componentMembers.Count;
            _componentMembers.Add([]);
            RecordMember(componentId, seed, parent: -1);

            var componentSize = ExploreComponent(seed, componentId);
            if (componentSize > 1) _componentsWithTouching++;
            _largestComponent = Math.Max(_largestComponent, componentSize);
        }
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

    /// <summary>Unvisited nodes touching a frontier node, each paired with the first frontier node that touches it; marks them visited.</summary>
    private List<TargetPair> ReachUnvisitedTouching(List<int> frontier)
    {
        _levels++;
        var (pairs, broadPhase) = Timing.Measure(() => _search.CollectFrontierPairs(frontier, skip: node => _visited[node]));
        var (results, narrowPhase) = Timing.Measure(() => _search.Tester.TestPairs(pairs, _parallelOptions));
        _broadPhase += broadPhase;
        _narrowPhase += narrowPhase;

        var reached = new List<TargetPair>();
        for (var k = 0; k < pairs.Count; k++)
        {
            var (_, to) = pairs[k];
            if (results[k] != PairTouch.Touching || _visited[to]) continue;
            _visited[to] = true;
            reached.Add(pairs[k]);
        }
        return reached;
    }

    /// <summary>Applies the level's reached nodes in order and returns the next frontier.</summary>
    private List<int> MergeLevel(List<TargetPair> reached, int componentId, ref int componentSize)
    {
        var next = new List<int>();
        foreach (var (from, to) in reached)
        {
            RecordMember(componentId, to, from);
            if (_isSeed[to])
            {
                // Another earlier removal: same component, already removed.
                componentSize++;
                next.Add(to);
                continue;
            }

            if (_keepRule.TryGetKeepReason(to, out var keepReason))
            {
                _kept.Add(new KeptTarget(to, keepReason, TouchedTargetIndex: from));
                continue;
            }

            _removals.Add(new TouchingRemoval(to, TouchedTargetIndex: from));
            componentSize++;
            next.Add(to);
        }
        return next;
    }

    private void RecordMember(int componentId, int node, int parent)
    {
        _componentId[node] = componentId;
        _parentOf[node] = parent;
        _depth[node] = parent < 0 ? 0 : _depth[parent] + 1;
        _componentMembers[componentId].Add(node);
    }

    private TouchClusters CreateClusters(TimeSpan setup, bool collectDiagnostics)
    {
        var stats = new TouchStats(
            _componentMembers.Count,
            _componentsWithTouching,
            _largestComponent,
            _levels,
            _search.Tester.GetStats(),
            setup,
            _broadPhase,
            _narrowPhase,
            TimeSpan.Zero);
        if (!collectDiagnostics) return new TouchClusters(_removals, _kept, stats, null);

        var (diagnostics, elapsed) = Timing.Measure(CreateDiagnostics);
        return new TouchClusters(_removals, _kept, stats with { DiagnosticsEdges = elapsed }, diagnostics);
    }

    /// <remarks>The search tests no pair between two already reached nodes, so the edges inside each component are tested here.</remarks>
    private TouchDiagnosticsData CreateDiagnostics()
    {
        var pairs = CollectSameComponentCandidatePairs();
        var results = _search.Tester.TestPairs(pairs, _parallelOptions);
        var touching = pairs.Where((_, k) => results[k] == PairTouch.Touching).ToList();
        var distances = MeasureMinSurfaceDistances(touching);
        var edges = touching.Select((pair, i) => new TouchEdge(_componentId[pair.First], pair, distances[i])).ToList();
        return new TouchDiagnosticsData(_componentId, _parentOf, _depth, _componentMembers, edges);
    }

    /// <returns>Candidate pairs with both ends in the same component, each once with First &lt; Second.</returns>
    private List<TargetPair> CollectSameComponentCandidatePairs()
    {
        var members = _componentMembers.SelectMany(component => component).ToList();
        var neighbors = _search.FindNeighborsOfAll(members);
        var pairs = new List<TargetPair>();
        for (var i = 0; i < members.Count; i++)
        {
            foreach (var neighbor in neighbors[i])
            {
                if (neighbor > members[i] && _componentId[neighbor] == _componentId[members[i]]) pairs.Add(new TargetPair(members[i], neighbor));
            }
        }
        return pairs;
    }

    private float[] MeasureMinSurfaceDistances(List<TargetPair> pairs)
    {
        var distances = new float[pairs.Count];
        Parallel.For(
            0,
            pairs.Count,
            _parallelOptions,
            () => new TouchScratch(),
            (i, _, scratch) =>
            {
                distances[i] = _search.Tester.MeasureMinSurfaceDistance(pairs[i], scratch);
                return scratch;
            },
            _ => { });
        return distances;
    }
}
