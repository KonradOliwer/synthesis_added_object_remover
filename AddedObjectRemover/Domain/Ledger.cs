using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>Why a step proposes to remove a target object.</summary>
internal abstract record Cause
{
    public sealed record TooClose(OtherId Rival) : Cause;

    public sealed record Touching(TargetId Touched) : Cause;

    /// <param name="RemovedShare">Fraction of the object's support held by removed objects.</param>
    /// <param name="MainSupporter">The removed target holding the largest share of its support.</param>
    public sealed record LostSupport(float RemovedShare, TargetId MainSupporter) : Cause;

    public sealed record InsideRival(OtherId Host) : Cause;

    public sealed record SurroundingsCleared : Cause;

    /// <param name="To">The member of the object's linked group whose removal took the group along.</param>
    public sealed record Linked(TargetId To) : Cause;
}

internal enum RoundKind { TooClose, FollowUp, Leftover }

/// <param name="Number">1 for the first round of a run, counting up.</param>
internal readonly record struct Round(int Number, RoundKind Kind);

/// <summary>What happened to a target object, once and for all.</summary>
internal abstract record Verdict(Round Round, Cause Cause)
{
    public sealed record Removed(Round Round, Cause Cause) : Verdict(Round, Cause);

    /// <summary>A step proposed to remove the object, but it is protected.</summary>
    public sealed record Held(Round Round, Cause Cause, KeepReason Reason) : Verdict(Round, Cause);
}

/// <summary>A step's proposal to remove one target object.</summary>
internal readonly record struct Proposal(TargetId Target, Cause Cause);

/// <summary>
/// The decisions of one run: every target object's verdict, stamped with the round that made it.
/// A value is a view of the rounds applied up to it, over one store shared by the whole run, so an
/// older view never changes. Only the newest view may apply the next round; the run is linear.
/// </summary>
internal sealed class Ledger
{
    private sealed class Store(Protection protection, int targetCount)
    {
        public Protection Protection { get; } = protection;
        public Verdict?[] Verdicts { get; } = new Verdict?[targetCount];
        public List<Round> Rounds { get; } = [];
        public List<ImmutableArray<TargetId>> RemovedIn { get; } = [];
        public List<ImmutableArray<TargetId>> HeldIn { get; } = [];
    }

    private readonly Store _store;
    private readonly int _roundCount;

    private Ledger(Store store, int roundCount)
    {
        _store = store;
        _roundCount = roundCount;
    }

    public static Ledger Start(Protection protection, int targetCount) => new(new Store(protection, targetCount), 0);

    public ImmutableArray<Round> Rounds => [.. _store.Rounds.Take(_roundCount)];

    public Verdict? Of(TargetId id) => _store.Verdicts[id.Index] is { } verdict && verdict.Round.Number <= _roundCount ? verdict : null;

    public bool IsDecided(TargetId id) => Of(id) != null;

    public bool IsRemoved(TargetId id) => Of(id) is Verdict.Removed;

    /// <summary>The objects the round removed: its proposals in target order, then the linked group members they took along.</summary>
    public ImmutableArray<TargetId> RemovedIn(Round round) => _store.RemovedIn[VisibleIndexOf(round)];

    /// <summary>The objects the round held, in target order.</summary>
    public ImmutableArray<TargetId> HeldIn(Round round) => _store.HeldIn[VisibleIndexOf(round)];

    /// <summary>Every decided object, in target order.</summary>
    public IEnumerable<(TargetId Target, Verdict Verdict)> All()
    {
        for (var index = 0; index < _store.Verdicts.Length; index++)
        {
            var id = new TargetId(index);
            if (Of(id) is { } verdict) yield return (id, verdict);
        }
    }

    /// <summary>
    /// Opens the next round in two phases. Direct: proposals in target order (for a target proposed
    /// twice the first proposal counts) decide each undecided target: a protected one is held,
    /// any other removed. Spread: the undecided members of the linked group of each target removed
    /// directly are removed as linked to the group's lowest-id direct removal. A protected group
    /// never has a removed member, since protection covers whole groups.
    /// </summary>
    /// <returns>The view that includes the new round.</returns>
    public Ledger Apply(RoundKind kind, IReadOnlyList<Proposal> proposals)
    {
        if (_roundCount != _store.Rounds.Count) throw new InvalidOperationException("Only the newest ledger view can apply a round.");

        var round = new Round(_roundCount + 1, kind);
        _store.Rounds.Add(round);
        var next = new Ledger(_store, round.Number);
        var (removed, held) = next.DecideDirectly(round, proposals);
        _store.RemovedIn.Add([.. removed, .. next.SpreadToLinkedMembers(round, removed)]);
        _store.HeldIn.Add([.. held]);
        return next;
    }

    private (List<TargetId> Removed, List<TargetId> Held) DecideDirectly(Round round, IReadOnlyList<Proposal> proposals)
    {
        var removed = new List<TargetId>();
        var held = new List<TargetId>();
        foreach (var proposal in proposals.OrderBy(proposal => proposal.Target))
        {
            var target = proposal.Target;
            if (IsDecided(target)) continue;
            if (_store.Protection.TryGetKeepReason(target.Index, out var reason))
            {
                _store.Verdicts[target.Index] = new Verdict.Held(round, proposal.Cause, reason);
                held.Add(target);
            }
            else
            {
                _store.Verdicts[target.Index] = new Verdict.Removed(round, proposal.Cause);
                removed.Add(target);
            }
        }
        return (removed, held);
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
                _store.Verdicts[member] = new Verdict.Removed(round, new Cause.Linked(removed));
                linked.Add(memberId);
            }
        }
        return linked;
    }

    private int VisibleIndexOf(Round round) =>
        round.Number <= _roundCount ? round.Number - 1 : throw new ArgumentOutOfRangeException(nameof(round), round, "The round is not part of this ledger view.");
}
