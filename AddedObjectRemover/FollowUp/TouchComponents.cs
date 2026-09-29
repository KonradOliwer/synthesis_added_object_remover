using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <param name="ComponentsWithRemovals">Components with more than one removed object.</param>
/// <param name="LargestComponent">The most removed objects in one component.</param>
/// <param name="Levels">Follow-up rounds that reached at least one object.</param>
/// <param name="MaxDepth">The longest chain, in steps, from a seed to an object reached from it.</param>
internal sealed record TouchStats(
    int Components,
    int ComponentsWithRemovals,
    int LargestComponent,
    int Levels,
    int MaxDepth,
    PairTestStats Pairs,
    TimeSpan Setup,
    TimeSpan BroadPhase,
    TimeSpan NarrowPhase,
    TimeSpan DiagnosticsEdges);

/// <param name="Removals">The follow-up rounds' removals: touching ones and the linked group members removed with them.</param>
/// <param name="Kept">The objects the follow-up rounds held.</param>
internal sealed record TouchClusters(List<Removal> Removals, List<KeptTarget> Kept, TouchStats Stats, TouchDiagnosticsData? Diagnostics);

/// <summary>A touching pair of targets in the same component; First &lt; Second.</summary>
internal readonly record struct TouchEdge(int ComponentId, TargetPair Pair, float MinSurfaceDistance);

/// <param name="ComponentId">Target index -&gt; component id, or -1 if not a node.</param>
/// <param name="ParentOf">Target index -&gt; the target index it was reached through (its Touching or Linked cause), or -1 for a seed.</param>
/// <param name="Depth">Target index -&gt; its distance (edge count) from a seed.</param>
/// <param name="ComponentMembers">Component id -&gt; every target index in it (seed, removed or held): seeds, then round by round, each in target order.</param>
/// <param name="Edges">Every touching pair with both ends in the same component.</param>
internal sealed record TouchDiagnosticsData(
    int[] ComponentId,
    int[] ParentOf,
    int[] Depth,
    List<List<int>> ComponentMembers,
    List<TouchEdge> Edges);

/// <summary>
/// The connected components of the touch cascade, read from the ledger. Nodes are the seeds (every
/// removal of the too-close round) and every object the follow-up rounds removed or held. Edges
/// join every touching pair of removed objects, whether or not the touch was an object's cause,
/// and each follow-up removal linked to a member of its group; the too-close round's linked
/// removals are seeds of their own. Held objects are leaves in the component of the object they
/// touch and join nothing. Components are numbered by their lowest seed.
/// </summary>
internal sealed class TouchComponents
{
    private const int None = -1;

    private readonly Ledger _ledger;
    private readonly TouchSearch _search;
    private readonly ParallelOptions _parallelOptions;
    private readonly List<int> _nodes = [];
    private readonly int[] _componentId;
    private readonly int[] _parentOf;
    private readonly int[] _depth;
    private readonly List<List<int>> _componentMembers = [];

    private TouchComponents(Ledger ledger, int targetCount, TouchSearch search, ParallelOptions parallelOptions)
    {
        _ledger = ledger;
        _search = search;
        _parallelOptions = parallelOptions;
        _componentId = new int[targetCount];
        Array.Fill(_componentId, None);
        _parentOf = new int[targetCount];
        Array.Fill(_parentOf, None);
        _depth = new int[targetCount];
    }

    /// <param name="ledger">The ledger after the follow-up rounds.</param>
    /// <param name="seedRound">The too-close round.</param>
    /// <param name="followUpRounds">The rounds of the touch cascade, in order.</param>
    /// <param name="evidence">The cascade's evidence, one per follow-up round.</param>
    /// <param name="pairs">The pair test counts of the cascade itself, taken before the components are tested.</param>
    public static TouchClusters Find(
        Ledger ledger,
        LedgerReport report,
        int targetCount,
        Round seedRound,
        ImmutableArray<Round> followUpRounds,
        ImmutableArray<RoundEvidence> evidence,
        TouchSearch search,
        PairTestStats pairs,
        TouchTimes times,
        ParallelOptions parallelOptions,
        bool collectDiagnostics)
    {
        var components = new TouchComponents(ledger, targetCount, search, parallelOptions);
        components.RecordDiscovery(seedRound, followUpRounds);
        components.AssignComponents(ledger.RemovedIn(seedRound), followUpRounds);
        var stats = components.CreateStats(evidence, pairs, times);
        var removals = followUpRounds.SelectMany(report.RemovalsIn).ToList();
        var kept = followUpRounds.SelectMany(round => LedgerReport.KeptIn(ledger, round)).ToList();
        if (!collectDiagnostics) return new TouchClusters(removals, kept, stats, null);

        var (diagnostics, elapsed) = Timing.Measure(components.CreateDiagnostics);
        return new TouchClusters(removals, kept, stats with { DiagnosticsEdges = elapsed }, diagnostics);
    }

    /// <summary>Seeds in target order at depth 0, then each follow-up round's objects in target order, one step beyond their cause.</summary>
    private void RecordDiscovery(Round seedRound, ImmutableArray<Round> followUpRounds)
    {
        foreach (var seed in _ledger.RemovedIn(seedRound).Order()) _nodes.Add(seed.Index);
        foreach (var round in followUpRounds)
        {
            foreach (var node in _ledger.RemovedIn(round).Concat(_ledger.HeldIn(round)).Order())
            {
                var parent = _ledger.Of(node)!.Cause switch
                {
                    Cause.Touching touching => touching.Touched.Index,
                    Cause.Linked linked => linked.To.Index,
                    var cause => throw new InvalidOperationException($"A touch round has no cause {cause}."),
                };
                _parentOf[node.Index] = parent;
                _depth[node.Index] = _depth[parent] + 1;
                _nodes.Add(node.Index);
            }
        }
    }

    private void AssignComponents(ImmutableArray<TargetId> seeds, ImmutableArray<Round> followUpRounds)
    {
        var removed = _nodes.Where(node => _ledger.IsRemoved(new TargetId(node))).ToList();
        var roots = new UnionFind(_componentId.Length);
        foreach (var pair in FindTouchingPairs(removed)) roots.Join(pair.First, pair.Second);
        foreach (var round in followUpRounds)
        {
            foreach (var node in _ledger.RemovedIn(round))
            {
                if (_ledger.Of(node)!.Cause is Cause.Linked linked) roots.Join(node.Index, linked.To.Index);
            }
        }

        var componentOfRoot = new Dictionary<int, int>();
        foreach (var seed in seeds.Order())
        {
            var root = roots.Find(seed.Index);
            if (componentOfRoot.ContainsKey(root)) continue;
            componentOfRoot[root] = _componentMembers.Count;
            _componentMembers.Add([]);
        }
        foreach (var node in _nodes)
        {
            var component = _ledger.IsRemoved(new TargetId(node)) ? componentOfRoot[roots.Find(node)] : _componentId[_parentOf[node]];
            _componentId[node] = component;
            _componentMembers[component].Add(node);
        }
    }

    /// <returns>The touching candidate pairs among <paramref name="nodes"/>, each once with First &lt; Second.</returns>
    private List<TargetPair> FindTouchingPairs(IReadOnlyList<int> nodes)
    {
        var inSet = nodes.ToHashSet();
        var pairs = CollectCandidatePairs(nodes, (first, second) => inSet.Contains(second));
        var results = _search.Tester.TestPairs(pairs, _parallelOptions);
        return pairs.Where((_, k) => results[k] == PairTouch.Touching).ToList();
    }

    private List<TargetPair> CollectCandidatePairs(IReadOnlyList<int> nodes, Func<int, int, bool> include)
    {
        var neighbors = _search.FindNeighborsOfAll(nodes);
        var pairs = new List<TargetPair>();
        for (var i = 0; i < nodes.Count; i++)
        {
            foreach (var neighbor in neighbors[i])
            {
                if (neighbor > nodes[i] && include(nodes[i], neighbor)) pairs.Add(new TargetPair(nodes[i], neighbor));
            }
        }
        return pairs;
    }

    private TouchStats CreateStats(ImmutableArray<RoundEvidence> evidence, PairTestStats pairs, TouchTimes times)
    {
        var removedPerComponent = _componentMembers
            .Select(members => members.Count(node => _ledger.IsRemoved(new TargetId(node))))
            .ToList();
        return new TouchStats(
            _componentMembers.Count,
            removedPerComponent.Count(removed => removed > 1),
            removedPerComponent.DefaultIfEmpty(0).Max(),
            evidence.Count(round => round is TouchRound { Reached.IsEmpty: false }),
            _nodes.Select(node => _depth[node]).DefaultIfEmpty(0).Max(),
            pairs,
            times.Setup,
            times.BroadPhase,
            times.NarrowPhase,
            TimeSpan.Zero);
    }

    /// <summary>The touching pairs with both ends, held objects included, in the same component, with their distances.</summary>
    private TouchDiagnosticsData CreateDiagnostics()
    {
        var pairs = CollectCandidatePairs(_nodes, (first, second) => _componentId[second] != None && _componentId[second] == _componentId[first]);
        var results = _search.Tester.TestPairs(pairs, _parallelOptions);
        var touching = pairs.Where((_, k) => results[k] == PairTouch.Touching).ToList();
        var distances = MeasureMinSurfaceDistances(touching);
        var edges = touching.Select((pair, i) => new TouchEdge(_componentId[pair.First], pair, distances[i])).ToList();
        return new TouchDiagnosticsData(_componentId, _parentOf, _depth, _componentMembers, edges);
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

/// <summary>Where the touch search spent its time; for the log only.</summary>
internal readonly record struct TouchTimes(TimeSpan Setup, TimeSpan BroadPhase, TimeSpan NarrowPhase);
