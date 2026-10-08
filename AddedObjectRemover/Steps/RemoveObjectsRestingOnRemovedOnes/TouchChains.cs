using System.Collections.Immutable;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// The connected chains of the touch cascade, read from the removal decisions. Nodes are the seeds (every
/// removal of the too-close round) and every object the also-remove rounds removed or kept. Edges
/// join every touching pair of removed objects, whether or not the touch was an object's cause,
/// and each also-remove removal to its cause: the object it touched or the member of its group it
/// was linked to. The too-close round's linked removals are seeds of their own. Kept objects are
/// leaves in the chain of the object they touch and join nothing. Chains are numbered by
/// their lowest seed.
/// </summary>
internal sealed class TouchChains
{
    private readonly IRemovalDecisions _decisions;
    private readonly TouchSearch _search;
    private readonly Execution _execution;
    private readonly List<int> _nodes = [];
    private readonly int[] _componentId;
    private readonly int[] _parentOf;
    private readonly int[] _depth;
    private readonly List<List<int>> _componentMembers = [];

    private TouchChains(IRemovalDecisions decisions, int targetCount, TouchSearch search, Execution execution)
    {
        _decisions = decisions;
        _search = search;
        _execution = execution;
        _componentId = new int[targetCount];
        Array.Fill(_componentId, TouchChainSet.None);
        _parentOf = new int[targetCount];
        Array.Fill(_parentOf, TouchChainSet.None);
        _depth = new int[targetCount];
    }

    /// <param name="run">A run of the touch rounds; its decisions end with them, right after the too-close round.</param>
    public static TouchChainSet Find(RestingObjectsRun run, int targetCount, Execution execution)
    {
        var search = run.Search ?? throw new ArgumentException("Touch chains need the touch rounds' search.", nameof(run));
        var alsoRemove = run.Result;
        var decisions = alsoRemove.RemovalDecisions;
        var seedRound = decisions.Rounds[decisions.Rounds.Length - alsoRemove.Rounds.Length - 1];
        var chains = new TouchChains(decisions, targetCount, search, execution);
        chains.RecordDiscovery(seedRound, alsoRemove.Rounds);
        chains.AssignComponents(decisions.RemovedIn(seedRound), alsoRemove.Rounds);
        return chains.CreateSet([.. alsoRemove.Evidence.Cast<TouchRoundDetails>()]);
    }

    /// <returns>The candidate pairs from each of <paramref name="nodes"/> to a larger neighbour <paramref name="include"/> accepts, each once with First &lt; Second.</returns>
    public static List<IndexPair> CollectCandidatePairs(TouchSearch search, IReadOnlyList<int> nodes, Func<int, int, bool> include)
    {
        var neighbors = search.FindNeighborsOfAll(nodes);
        return AdjacencyPairs.From(nodes, neighbors, include, (first, second) => new IndexPair(first, second));
    }

    /// <summary>Seeds in target order at depth 0, then each also-remove round's objects in target order, one step beyond their cause.</summary>
    private void RecordDiscovery(Round seedRound, ImmutableArray<Round> alsoRemoveRounds)
    {
        foreach (var seed in _decisions.RemovedIn(seedRound).Order()) _nodes.Add(seed.Index);
        foreach (var round in alsoRemoveRounds)
        {
            var reached = _decisions.RemovedIn(round).Concat(_decisions.KeptIn(round)).ToList();
            foreach (var node in reached) RecordCause(node);
            _nodes.AddRange(reached.Order().Select(node => node.Index));
        }
    }

    /// <remarks>Needs the cause's target to have its depth already: the decisions list a round's linked removals after its direct ones.</remarks>
    private void RecordCause(TargetId node)
    {
        var parent = _decisions.Of(node)!.Reason switch
        {
            RemovalReason.Touching touching => touching.Touched.Index,
            RemovalReason.LinkedTo linked => linked.To.Index,
            var reason => throw new InvalidOperationException($"A touch round has no cause {reason}."),
        };
        _parentOf[node.Index] = parent;
        _depth[node.Index] = _depth[parent] + 1;
    }

    private void AssignComponents(ImmutableArray<TargetId> seeds, ImmutableArray<Round> alsoRemoveRounds)
    {
        var removed = _nodes.Where(node => _decisions.IsRemoved(new TargetId(node))).ToList();
        var roots = new UnionFind(_componentId.Length);
        foreach (var pair in FindTouchingPairs(removed)) roots.Join(pair.First, pair.Second);
        // A pair near the touch distance can test apart here although its round found it touching, so the causes are edges too.
        foreach (var node in alsoRemoveRounds.SelectMany(round => _decisions.RemovedIn(round))) roots.Join(node.Index, _parentOf[node.Index]);

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
            var component = _decisions.IsRemoved(new TargetId(node)) ? componentOfRoot[roots.Find(node)] : _componentId[_parentOf[node]];
            _componentId[node] = component;
            _componentMembers[component].Add(node);
        }
    }

    /// <returns>The touching candidate pairs among <paramref name="nodes"/>, each once with First &lt; Second.</returns>
    private List<IndexPair> FindTouchingPairs(IReadOnlyList<int> nodes)
    {
        var inSet = nodes.ToHashSet();
        var pairs = CollectCandidatePairs(_search, nodes, (_, second) => inSet.Contains(second));
        var (results, _) = _search.Tester.TestPairs(pairs, _execution);
        return ParallelResults.IndicesWhere(results, touch => touch == PairTouch.Touching).Select(k => pairs[k]).ToList();
    }

    private TouchChainSet CreateSet(IReadOnlyList<TouchRoundDetails> rounds) => new(
        CreateStats(rounds),
        [.. _componentId],
        [.. _parentOf],
        [.. _depth],
        [.. _componentMembers.Select(members => members.ToImmutableArray())]);

    private TouchChainStatistics CreateStats(IReadOnlyList<TouchRoundDetails> rounds)
    {
        var removedPerComponent = _componentMembers
            .Select(members => members.Count(node => _decisions.IsRemoved(new TargetId(node))))
            .ToList();
        return new TouchChainStatistics(
            _componentMembers.Count,
            removedPerComponent.Count(removed => removed > 1),
            removedPerComponent.DefaultIfEmpty(0).Max(),
            rounds.Count(round => !round.Reached.IsEmpty),
            _nodes.Select(node => _depth[node]).DefaultIfEmpty(0).Max(),
            Work.Sum(rounds.Select(round => round.Work)));
    }
}
