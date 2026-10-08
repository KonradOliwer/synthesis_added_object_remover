using System.Collections.Immutable;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RemovalDecisionList;

/// <summary>
/// The decisions of one run: every target object's decision, stamped with the round that made it.
/// A value is a view of the rounds and moves added up to it, over one store shared by the whole run,
/// so an older view never changes. Only the newest view may add to the run; the run is linear.
/// </summary>
internal sealed class RemovalDecisions : IRemovalDecisions
{
    private sealed class Store(IObjectsToKeep protection, int targetCount)
    {
        public IObjectsToKeep Protection { get; } = protection;
        public Decision?[] Decisions { get; } = new Decision?[targetCount];
        public List<Round> Rounds { get; } = [];
        public List<ImmutableArray<TargetId>> RemovedIn { get; } = [];
        public List<ImmutableArray<TargetId>> KeptIn { get; } = [];
        public List<MarkerMove> Moves { get; } = [];
    }

    private readonly Store _store;
    private readonly int _roundCount;
    private readonly int _moveCount;

    private RemovalDecisions(Store store, int roundCount, int moveCount)
    {
        _store = store;
        _roundCount = roundCount;
        _moveCount = moveCount;
    }

    public static RemovalDecisions Start(IObjectsToKeep protection, int targetCount) =>
        new(new Store(protection, targetCount), roundCount: 0, moveCount: 0);

    public ImmutableArray<Round> Rounds => [.. _store.Rounds.Take(_roundCount)];

    public ImmutableArray<MarkerMove> Moves => [.. _store.Moves.Take(_moveCount)];

    public Decision? Of(TargetId id) => _store.Decisions[id.Index] is { } decision && decision.Round.Number <= _roundCount ? decision : null;

    public bool IsDecided(TargetId id) => Of(id) != null;

    public bool IsRemoved(TargetId id) => Of(id) is Decision.Removed;

    public bool IsProtected(TargetId id) => _store.Protection.IsProtected(id.Index);

    public ImmutableArray<TargetId> RemovedIn(Round round) => _store.RemovedIn[VisibleIndexOf(round)];

    public ImmutableArray<TargetId> KeptIn(Round round) => _store.KeptIn[VisibleIndexOf(round)];

    public IEnumerable<(TargetId Target, Decision Decision)> All()
    {
        for (var index = 0; index < _store.Decisions.Length; index++)
        {
            var id = new TargetId(index);
            if (Of(id) is { } decision) yield return (id, decision);
        }
    }

    public ImmutableArray<TargetId> RemovedTargets() => [.. All().Where(entry => entry.Decision is Decision.Removed).Select(entry => entry.Target)];

    public ImmutableArray<Round> RoundsAfter(IRemovalDecisions older) => [.. Rounds.Skip(older.Rounds.Length)];

    /// <summary>
    /// Opens the next round in two phases. Direct: proposals in target order (for a target proposed
    /// twice the first proposal counts) decide each undecided target: a protected one is kept,
    /// any other removed. Spread: the undecided members of the linked group of each target removed
    /// directly are removed as linked to the group's lowest-id direct removal. A protected group
    /// never has a removed member, since protection covers whole groups.
    /// </summary>
    /// <returns>The view that includes the new round.</returns>
    public RemovalDecisions Apply(RoundKind kind, IReadOnlyList<ProposedRemoval> proposals)
    {
        RequireNewestView();

        var round = new Round(_roundCount + 1, kind);
        _store.Rounds.Add(round);
        var next = new RemovalDecisions(_store, round.Number, _moveCount);
        var (removed, kept) = next.DecideDirectly(round, proposals);
        _store.RemovedIn.Add([.. removed, .. next.SpreadToLinkedMembers(round, removed)]);
        _store.KeptIn.Add([.. kept]);
        return next;
    }

    /// <summary>
    /// Applies also-remove rounds, each proposed from the objects the newest round removed (the last
    /// round already present seeds the first), until a round removes nothing.
    /// </summary>
    /// <returns>The view with the new rounds, and each round's details in round order.</returns>
    public (RemovalDecisions Decisions, ImmutableArray<TDetails> Details) ApplyRoundsUntilNothingRemoved<TDetails>(IRemovalRounds<TDetails> rounds)
    {
        var details = ImmutableArray.CreateBuilder<TDetails>();
        var decisions = this;
        var removedLastRound = decisions.RemovedIn(decisions.Rounds[^1]);
        while (!removedLastRound.IsEmpty)
        {
            var next = rounds.Next(decisions, removedLastRound);
            details.Add(next.Details);
            decisions = decisions.Apply(RoundKind.AlsoRemove, next.Proposals);
            removedLastRound = decisions.RemovedIn(decisions.Rounds[^1]);
        }
        return (decisions, details.ToImmutable());
    }

    /// <returns>The view that includes the new moves.</returns>
    public RemovalDecisions AddMoves(IReadOnlyList<MarkerMove> moves)
    {
        RequireNewestView();

        _store.Moves.AddRange(moves);
        return new RemovalDecisions(_store, _roundCount, _store.Moves.Count);
    }

    /// <summary>
    /// Removes every round and move added after this view, with the decisions they made, so this view is the newest again.
    /// Views of the removed rounds must not be used afterwards.
    /// </summary>
    public void DropNewerThanThisView()
    {
        for (var index = 0; index < _store.Decisions.Length; index++)
        {
            if (_store.Decisions[index] is { } decision && decision.Round.Number > _roundCount) _store.Decisions[index] = null;
        }
        _store.Rounds.RemoveRange(_roundCount, _store.Rounds.Count - _roundCount);
        _store.RemovedIn.RemoveRange(_roundCount, _store.RemovedIn.Count - _roundCount);
        _store.KeptIn.RemoveRange(_roundCount, _store.KeptIn.Count - _roundCount);
        _store.Moves.RemoveRange(_moveCount, _store.Moves.Count - _moveCount);
    }

    private void RequireNewestView()
    {
        if (_roundCount != _store.Rounds.Count || _moveCount != _store.Moves.Count)
        {
            throw new InvalidOperationException("Only the newest view of the removal decisions can be added to.");
        }
    }

    private (List<TargetId> Removed, List<TargetId> Kept) DecideDirectly(Round round, IReadOnlyList<ProposedRemoval> proposals)
    {
        var removed = new List<TargetId>();
        var kept = new List<TargetId>();
        foreach (var proposal in proposals.OrderBy(proposal => proposal.Target))
        {
            var target = proposal.Target;
            if (IsDecided(target)) continue;
            if (_store.Protection.TryGetKeepReason(target.Index, out var keepReason))
            {
                _store.Decisions[target.Index] = new Decision.Kept(round, proposal.Reason, keepReason);
                kept.Add(target);
            }
            else
            {
                _store.Decisions[target.Index] = new Decision.Removed(round, proposal.Reason);
                removed.Add(target);
            }
        }
        return (removed, kept);
    }

    /// <param name="directlyRemoved">In target order, so the first removal found in a group is its lowest-id one.</param>
    private List<TargetId> SpreadToLinkedMembers(Round round, IReadOnlyList<TargetId> directlyRemoved)
    {
        var linked = new List<TargetId>();
        foreach (var removed in directlyRemoved)
        {
            foreach (var member in _store.Protection.Groups.MembersOf(removed.Index))
            {
                var memberId = new TargetId(member);
                if (IsDecided(memberId)) continue;
                _store.Decisions[member] = new Decision.Removed(round, new RemovalReason.LinkedTo(removed));
                linked.Add(memberId);
            }
        }
        return linked;
    }

    private int VisibleIndexOf(Round round) =>
        round.Number <= _roundCount ? round.Number - 1 : throw new ArgumentOutOfRangeException(nameof(round), round, "The round is not part of this view.");
}
