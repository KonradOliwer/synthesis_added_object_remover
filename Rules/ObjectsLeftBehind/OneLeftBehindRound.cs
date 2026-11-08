using System.Collections.Immutable;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Rules.ObjectsLeftBehind;

/// <summary>The one round of a left-behind step that judges every object once, as <see cref="ILeftBehindRoundJoin.DecideOneRound{TCheck}"/> says.</summary>
internal static class OneLeftBehindRound
{
    public static OneRoundDecided<TCheck> Decide<TCheck>(
        IRemovalDecisions decisions, RoundOpening opening, IReadOnlyList<TCheck> checks, Func<TCheck, RemovalReason> reasonOf, IAlsoRemoveFollowUp followUp)
        where TCheck : LeftBehindVerdict
    {
        var proposals = LeftBehindVerdicts.ProposalsOf(checks, reasonOf);
        if (proposals.Count == 0) return new OneRoundDecided<TCheck>(checks, [], followUp.Summarize(decisions));

        var copy = decisions.WorkingCopy();
        var decided = copy.DecideRound(opening, proposals);
        var joined = JoinVerdicts(checks, decided, opening.Kind);
        var followUps = FollowUp(decided, followUp);
        foreach (var round in followUps) decided = copy.DecideRound(round.Opening, round.Proposals);
        return new OneRoundDecided<TCheck>(joined, [new ProposedRound(opening, proposals), .. followUps], followUp.Summarize(decided));
    }

    /// <param name="decided">The decisions with the step's round, which decided every proposal and the linked group members it took along.</param>
    private static TCheck[] JoinVerdicts<TCheck>(IReadOnlyList<TCheck> checks, IRemovalDecisions decided, RoundKind kind)
        where TCheck : LeftBehindVerdict =>
        [.. checks.Select(check => LeftBehindVerdicts.JoinRoundDecision(check, RoundDecisionOf(new TargetId(check.TargetIndex), decided, kind), decided.Of))];

    /// <remarks>A candidate the round did not decide keeps the decision it had, such as its fit.</remarks>
    private static Decision? RoundDecisionOf(TargetId target, IRemovalDecisions decided, RoundKind kind) =>
        decided.Of(target) is { } decision && decision.Round.Kind == kind ? decision : null;

    /// <summary>The also-remove rounds that follow up what the step's round took from its place; none when it took nothing.</summary>
    private static ImmutableArray<ProposedRound> FollowUp(IRemovalDecisions decided, IAlsoRemoveFollowUp followUp)
    {
        var left = decided.LeftInNewestRound();
        if (left.IsEmpty) return [];
        return [.. followUp.FollowUp(decided, left).AsProposedRounds()];
    }
}
