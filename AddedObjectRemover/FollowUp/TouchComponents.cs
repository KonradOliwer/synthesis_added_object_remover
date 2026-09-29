using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <param name="ComponentsWithRemovals">Components with more than one removed object.</param>
/// <param name="LargestComponent">The most removed objects in one component.</param>
/// <param name="Levels">Follow-up rounds that reached at least one object.</param>
/// <param name="MaxDepth">The longest chain, in steps, from a seed to an object reached from it.</param>
/// <param name="Pairs">The follow-up rounds' pair tests; the components' own tests are not counted.</param>
internal sealed record TouchStats(
    int Components,
    int ComponentsWithRemovals,
    int LargestComponent,
    int Levels,
    int MaxDepth,
    PairTestStats Pairs);

/// <summary>A touching pair of targets in the same component; First &lt; Second.</summary>
internal readonly record struct TouchEdge(int ComponentId, TargetPair Pair, float MinSurfaceDistance);

/// <summary>
/// The connected components of the touch cascade, read from the ledger. Nodes are the seeds (every
/// removal of the too-close round) and every object the follow-up rounds removed or held. Edges
/// join every touching pair of removed objects, whether or not the touch was an object's cause,
/// and each follow-up removal to its cause: the object it touched or the member of its group it
/// was linked to. The too-close round's linked removals are seeds of their own. Held objects are
/// leaves in the component of the object they touch and join nothing. Components are numbered by
/// their lowest seed.
/// </summary>
internal sealed class TouchComponents
{
    private readonly Ledger _ledger;
    private readonly TouchSearch _search;
    private readonly Execution _execution;
    private readonly List<int> _nodes = [];
    private readonly int[] _componentId;
    private readonly int[] _parentOf;
    private readonly int[] _depth;
    private readonly List<List<int>> _componentMembers = [];

    private TouchComponents(Ledger ledger, int targetCount, TouchSearch search, Execution execution)
    {
        _ledger = ledger;
        _search = search;
        _execution = execution;
        _componentId = new int[targetCount];
        Array.Fill(_componentId, TouchComponentSet.None);
        _parentOf = new int[targetCount];
        Array.Fill(_parentOf, TouchComponentSet.None);
        _depth = new int[targetCount];
    }

    /// <param name="followUp">A result of the touch rounds; its ledger ends with them, right after the too-close round.</param>
    public static TouchComponentSet Find(FollowUpResult followUp, int targetCount, Execution execution)
    {
        var context = followUp.Context ?? throw new ArgumentException("Touch components need the touch rounds' context.", nameof(followUp));
        var ledger = followUp.Ledger;
        var seedRound = ledger.Rounds[ledger.Rounds.Length - followUp.Rounds.Length - 1];
        var components = new TouchComponents(ledger, targetCount, context.Search, execution);
        components.RecordDiscovery(seedRound, followUp.Rounds);
        components.AssignComponents(ledger.RemovedIn(seedRound), followUp.Rounds);
        return components.CreateSet([.. followUp.Evidence.Cast<TouchRound>()]);
    }

    /// <returns>The candidate pairs from each of <paramref name="nodes"/> to a larger neighbour <paramref name="include"/> accepts, each once with First &lt; Second.</returns>
    public static List<TargetPair> CollectCandidatePairs(TouchSearch search, IReadOnlyList<int> nodes, Func<int, int, bool> include)
    {
        var neighbors = search.FindNeighborsOfAll(nodes);
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

    /// <summary>Seeds in target order at depth 0, then each follow-up round's objects in target order, one step beyond their cause.</summary>
    private void RecordDiscovery(Round seedRound, ImmutableArray<Round> followUpRounds)
    {
        foreach (var seed in _ledger.RemovedIn(seedRound).Order()) _nodes.Add(seed.Index);
        foreach (var round in followUpRounds)
        {
            var reached = _ledger.RemovedIn(round).Concat(_ledger.HeldIn(round)).ToList();
            foreach (var node in reached) RecordCause(node);
            _nodes.AddRange(reached.Order().Select(node => node.Index));
        }
    }

    /// <remarks>Needs the cause's target to have its depth already: the ledger lists a round's linked removals after its direct ones.</remarks>
    private void RecordCause(TargetId node)
    {
        var parent = _ledger.Of(node)!.Cause switch
        {
            Cause.Touching touching => touching.Touched.Index,
            Cause.Linked linked => linked.To.Index,
            var cause => throw new InvalidOperationException($"A touch round has no cause {cause}."),
        };
        _parentOf[node.Index] = parent;
        _depth[node.Index] = _depth[parent] + 1;
    }

    private void AssignComponents(ImmutableArray<TargetId> seeds, ImmutableArray<Round> followUpRounds)
    {
        var removed = _nodes.Where(node => _ledger.IsRemoved(new TargetId(node))).ToList();
        var roots = new UnionFind(_componentId.Length);
        foreach (var pair in FindTouchingPairs(removed)) roots.Join(pair.First, pair.Second);
        // A pair near the touch distance can test apart here although its round found it touching, so the causes are edges too.
        foreach (var node in followUpRounds.SelectMany(round => _ledger.RemovedIn(round))) roots.Join(node.Index, _parentOf[node.Index]);

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
        var pairs = CollectCandidatePairs(_search, nodes, (_, second) => inSet.Contains(second));
        var (results, _) = _search.Tester.TestPairs(pairs, _execution);
        return pairs.Where((_, k) => results[k] == PairTouch.Touching).ToList();
    }

    private TouchComponentSet CreateSet(IReadOnlyList<TouchRound> rounds) => new(
        CreateStats(rounds),
        [.. _componentId],
        [.. _parentOf],
        [.. _depth],
        [.. _componentMembers.Select(members => members.ToImmutableArray())]);

    private TouchStats CreateStats(IReadOnlyList<TouchRound> rounds)
    {
        var removedPerComponent = _componentMembers
            .Select(members => members.Count(node => _ledger.IsRemoved(new TargetId(node))))
            .ToList();
        return new TouchStats(
            _componentMembers.Count,
            removedPerComponent.Count(removed => removed > 1),
            removedPerComponent.DefaultIfEmpty(0).Max(),
            rounds.Count(round => !round.Reached.IsEmpty),
            _nodes.Select(node => _depth[node]).DefaultIfEmpty(0).Max(),
            PairTestStats.Sum(rounds.Select(round => round.Work)));
    }
}
