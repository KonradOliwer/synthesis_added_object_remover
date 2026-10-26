using System.Collections.Immutable;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RemovalDecisionList;

/// <summary>
/// The decisions of one run: every target object's decision, stamped with the round that made it.
/// A value is a view of the rounds and moves added up to it, over one store shared by the whole run.
/// Adding never changes an older view. Only the newest view may add to the run; the run is linear.
/// A working copy has a store of its own, so what is decided on it never reaches the run.
/// </summary>
public sealed class RemovalDecisions : IRemovalDecisions
{
    private sealed class Store(IObjectsToKeep protection, int targetCount)
    {
        public IObjectsToKeep Protection { get; } = protection;
        public Decision?[] Decisions { get; } = new Decision?[targetCount];
        public List<Round> Rounds { get; } = [];
        public List<ImmutableArray<TargetId>> RemovedIn { get; } = [];
        public List<ImmutableArray<TargetId>> KeptIn { get; } = [];
        public List<ImmutableArray<TargetId>> FittedIn { get; } = [];
        public List<MarkerMove> Moves { get; } = [];

        /// <summary>By target index: the fit an also-remove round replaced, which the views before that round still show.</summary>
        public Decision?[] Replaced { get; } = new Decision?[targetCount];
    }

    /// <param name="newest">The newest view over the copy's own store.</param>
    private sealed class Copy(RemovalDecisions newest) : IRemovalDecisionsCopy
    {
        private RemovalDecisions _newest = newest;

        public IRemovalDecisions DecideRound(RoundKind kind, IReadOnlyList<ProposedRemoval> proposals)
        {
            _newest = _newest.Apply(kind, proposals);
            return _newest;
        }

        public (IRemovalDecisions Decisions, ImmutableArray<RoundProposals<TDetails>> Rounds) DecideRoundsUntilNothingRemoved<TDetails>(
            IRemovalRounds<TDetails> rounds, ImmutableArray<TargetId> seeds)
        {
            var decided = ImmutableArray.CreateBuilder<RoundProposals<TDetails>>();
            var removedLastRound = seeds;
            while (!removedLastRound.IsEmpty)
            {
                var next = rounds.Next(_newest, removedLastRound);
                decided.Add(next);
                DecideRound(RoundKind.AlsoRemove, next.Proposals);
                removedLastRound = _newest.RemovedIn(_newest.Rounds[^1]);
            }
            return (_newest, decided.ToImmutable());
        }
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

    public Decision? Of(TargetId id)
    {
        if (_store.Decisions[id.Index] is { } decision && decision.Round.Number <= _roundCount) return decision;
        return _store.Replaced[id.Index] is { } replaced && replaced.Round.Number <= _roundCount ? replaced : null;
    }

    public bool IsDecided(TargetId id) => Of(id) != null;

    public bool MayStillBeRemoved(TargetId id) => Of(id) is null or Decision.Fitted;

    public bool IsRemoved(TargetId id) => Of(id) is Decision.Removed;

    public bool IsProtected(TargetId id) => _store.Protection.IsProtected(id.Index);

    public ImmutableArray<TargetId> RemovedIn(Round round) => _store.RemovedIn[VisibleIndexOf(round)];

    public ImmutableArray<TargetId> KeptIn(Round round) => _store.KeptIn[VisibleIndexOf(round)];

    public ImmutableArray<TargetId> FittedIn(Round round) => [.. FittedAtFirstIn(round).Where(target => Of(target) is Decision.Fitted)];

    public ImmutableArray<TargetId> FittedAtFirstIn(Round round) => _store.FittedIn[VisibleIndexOf(round)];

    public ImmutableArray<TargetId> RemovedTargets() => [.. All().Where(entry => entry.Decision is Decision.Removed).Select(entry => entry.Target)];

    public IEnumerable<(TargetId Target, Decision Decision)> All()
    {
        for (var index = 0; index < _store.Decisions.Length; index++)
        {
            var id = new TargetId(index);
            if (Of(id) is { } decision) yield return (id, decision);
        }
    }

    public ImmutableArray<Round> RoundsAfter(IRemovalDecisions older) => [.. Rounds.Skip(older.Rounds.Length)];

    /// <summary>
    /// Opens the next round in two phases. Direct: proposals in target order (for a target proposed
    /// twice the first proposal counts) decide each undecided target: a protected one is kept,
    /// any other removed. An object kept shrunk or turned where it stands still stands there, so an also-remove round decides it
    /// like an undecided one; the rounds that judge a clash with other mods skip it, since it fits. Spread: the undecided members of the linked group of each target removed
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
        _store.FittedIn.Add([]);
        return next;
    }

    /// <summary>
    /// Opens a fit round, when there is a fit: each fitted object is decided as kept at its new placement, so no later round
    /// judges it, except an also-remove round (see <see cref="Apply"/>). A fit may only be proposed once for an undecided object that is not protected and has no linked partner, so
    /// fitting never splits a linked group; anything else is a bug.
    /// </summary>
    /// <returns>The view that includes the new round; this view when there is no fit.</returns>
    public RemovalDecisions ApplyFits(IReadOnlyList<ProposedFit> fits)
    {
        if (fits.Count == 0) return this;
        RequireNewestView();

        var round = new Round(_roundCount + 1, RoundKind.Fit);
        _store.Rounds.Add(round);
        var fitted = new List<TargetId>();
        foreach (var fit in fits.OrderBy(fit => fit.Target))
        {
            RequireFittable(fit.Target);
            _store.Decisions[fit.Target.Index] = new Decision.Fitted(round, fit.Clash, fit.To);
            fitted.Add(fit.Target);
        }
        _store.RemovedIn.Add([]);
        _store.KeptIn.Add([]);
        _store.FittedIn.Add([.. fitted]);
        return new RemovalDecisions(_store, round.Number, _moveCount);
    }

    private void RequireFittable(TargetId target)
    {
        if (_store.Decisions[target.Index] is not null || IsProtected(target) || _store.Protection.Groups.IsLinked(target.Index))
        {
            throw new InvalidOperationException($"Target {target.Index} cannot be fitted: it is decided, protected or linked.");
        }
    }

    /// <summary>Opens one round per entry of <paramref name="rounds"/>, in order, each as <see cref="Apply"/> does.</summary>
    /// <returns>The view that includes the new rounds.</returns>
    public RemovalDecisions ApplyRounds(RoundKind kind, IReadOnlyList<IReadOnlyList<ProposedRemoval>> rounds) =>
        rounds.Aggregate(this, (decisions, proposals) => decisions.Apply(kind, proposals));

    public IReadOnlyDictionary<TargetId, Decision> DecideRoundOnCopy(RoundKind kind, IReadOnlyList<ProposedRemoval> proposals)
    {
        var decided = WorkingCopy().DecideRound(kind, proposals);
        var round = decided.Rounds[^1];
        return decided.RemovedIn(round).Concat(decided.KeptIn(round)).ToDictionary(target => target, target => decided.Of(target)!);
    }

    public IRemovalDecisionsCopy WorkingCopy() => new Copy(new RemovalDecisions(CopyStore(), _roundCount, _moveCount));

    /// <returns>The view that includes the new moves.</returns>
    public RemovalDecisions AddMoves(IReadOnlyList<MarkerMove> moves)
    {
        RequireNewestView();

        _store.Moves.AddRange(moves);
        return new RemovalDecisions(_store, _roundCount, _store.Moves.Count);
    }

    /// <summary>A store holding exactly what this view shows.</summary>
    private Store CopyStore()
    {
        var copy = new Store(_store.Protection, _store.Decisions.Length);
        foreach (var (target, decision) in All()) copy.Decisions[target.Index] = decision;
        copy.Rounds.AddRange(_store.Rounds.Take(_roundCount));
        copy.RemovedIn.AddRange(_store.RemovedIn.Take(_roundCount));
        copy.KeptIn.AddRange(_store.KeptIn.Take(_roundCount));
        copy.FittedIn.AddRange(_store.FittedIn.Take(_roundCount));
        copy.Moves.AddRange(_store.Moves.Take(_moveCount));
        return copy;
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
            if (round.Kind == RoundKind.AlsoRemove ? !MayStillBeRemoved(target) : IsDecided(target)) continue;
            if (Of(target) is Decision.Fitted fitted) _store.Replaced[target.Index] = fitted;
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
