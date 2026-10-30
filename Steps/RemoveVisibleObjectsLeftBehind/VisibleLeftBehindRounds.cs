using System.Collections.Immutable;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind;

/// <summary>
/// The step's rounds, decided on a working copy of the decisions. Each round judges the candidates whose surroundings changed
/// since the round before (the first round: by the earlier steps), against the view before the round, and its proposals are decided
/// together, so objects the same round removes never count towards each other and the order of the objects never matters. The
/// also-remove follow-up of each round that took something from its place is decided on the same copy before the next round, so
/// what the follow-up removes counts for the next round. The rounds repeat until a round takes nothing from its place or no
/// undecided candidate's surroundings changed; a round runs only when it has a candidate to judge.
/// </summary>
/// <remarks>
/// Every visible round but the last takes at least one object from its place, and an object leaves its place at most once, so there
/// are at most as many visible rounds as targets plus one. A candidate whose surroundings did not change since it was last judged needs no new
/// judgement: that judgement kept it, because a removal verdict would have decided it, and the same surroundings give the same
/// verdict. A candidate no round judged has every neighbour standing and so no cleared direction, because a direction is cleared
/// only when at least the setting's percentage of it, never below ten, has left.
/// </remarks>
internal sealed class VisibleLeftBehindRounds
{
    private readonly VisibleLeftBehindRule _rule;
    private readonly VisibleObjectSurroundings _surroundings;
    private readonly IAlsoRemoveFollowUp _followUp;
    private readonly Func<TimeSpan> _timeSinceStart;
    private readonly IRemovalDecisionsCopy _copy;

    /// <summary>Target indexes, ascending.</summary>
    private readonly int[] _candidates;

    /// <summary>By position in <see cref="_candidates"/>.</summary>
    private readonly VisibleLeftBehindCheck[] _latestChecks;

    private readonly ImmutableArray<ProposedRound>.Builder _proposedRounds = ImmutableArray.CreateBuilder<ProposedRound>();

    private VisibleLeftBehindRounds(
        VisibleLeftBehindRule rule,
        VisibleObjectSurroundings surroundings,
        IAlsoRemoveFollowUp followUp,
        Func<TimeSpan> timeSinceStart,
        IRemovalDecisions decisions)
    {
        _rule = rule;
        _surroundings = surroundings;
        _followUp = followUp;
        _timeSinceStart = timeSinceStart;
        _copy = decisions.WorkingCopy();
        _candidates = [.. surroundings.Initial.Select(measured => measured.TargetIndex)];
        _latestChecks = [.. surroundings.Initial.Select(rule.Judge)];
        Decided = decisions;
    }

    /// <summary>The copy's view after the newest round.</summary>
    public IRemovalDecisions Decided { get; private set; }

    /// <summary>Each candidate's check, in target order: with every neighbour standing, or as the newest round that judged it found it.</summary>
    public IReadOnlyList<VisibleLeftBehindCheck> LatestChecks => _latestChecks;

    /// <summary>The visible rounds and their follow-up rounds, in the order they were decided.</summary>
    public ImmutableArray<ProposedRound> ProposedRounds => _proposedRounds.ToImmutable();

    /// <summary>The time of the visible rounds alone, the follow-ups between them left out.</summary>
    public TimeSpan RoundsTime { get; private set; }

    /// <param name="timeSinceStart">The run's clock, which measures the visible rounds.</param>
    /// <param name="decisions">The decisions of the earlier steps; the copy starts from them.</param>
    public static VisibleLeftBehindRounds Decide(
        VisibleLeftBehindRule rule,
        VisibleObjectSurroundings surroundings,
        IAlsoRemoveFollowUp followUp,
        Func<TimeSpan> timeSinceStart,
        IRemovalDecisions decisions)
    {
        var rounds = new VisibleLeftBehindRounds(rule, surroundings, followUp, timeSinceStart, decisions);
        while (rounds.DecideNextVisibleRound()) rounds.FollowUpNewestRound();
        return rounds;
    }

    /// <returns>Whether the round took something from its place.</returns>
    private bool DecideNextVisibleRound()
    {
        var start = _timeSinceStart();
        var tookSomething = JudgeAndDecideRound();
        RoundsTime += _timeSinceStart() - start;
        return tookSomething;
    }

    /// <returns>Whether the round took something from its place; false without a candidate to judge.</returns>
    private bool JudgeAndDecideRound()
    {
        var checks = _surroundings.Update(Decided).Select(_rule.Judge).ToArray();
        if (checks.Length == 0) return false;

        RecordLatestChecks(checks);
        DecideRound(new ProposedRound(RoundKind.VisibleLeftBehind, VisibleLeftBehindProposals.ProposalsOf(checks)));
        return !LeftInNewestRound().IsEmpty;
    }

    /// <remarks>A failed follow-up proposes nothing, so the visible rounds go on alone.</remarks>
    private void FollowUpNewestRound()
    {
        var pass = _followUp.FollowUp(Decided, LeftInNewestRound());
        foreach (var proposals in pass.ProposedRounds) DecideRound(new ProposedRound(RoundKind.AlsoRemove, proposals));
    }

    private void RecordLatestChecks(IEnumerable<VisibleLeftBehindCheck> checks)
    {
        foreach (var check in checks) _latestChecks[Array.BinarySearch(_candidates, check.TargetIndex)] = check;
    }

    private void DecideRound(ProposedRound round)
    {
        _proposedRounds.Add(round);
        Decided = _copy.DecideRound(round.Kind, round.Proposals);
    }

    private ImmutableArray<TargetId> LeftInNewestRound() => Decided.LeftIn(Decided.Rounds[^1]);
}
