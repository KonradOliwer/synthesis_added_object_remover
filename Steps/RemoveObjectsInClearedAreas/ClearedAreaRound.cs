using System.Collections.Immutable;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsInClearedAreas.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsInClearedAreas;

/// <summary>
/// The step's one round: every candidate judged on the decisions of the earlier steps, all removals proposed together, then the
/// also-remove follow-up of what the round took from its place, decided on a working copy. Nothing is judged again after the round.
/// </summary>
internal static class ClearedAreaRound
{
    /// <summary>An object kept shrunk or turned still stands where it was fitted, so the round decides it too.</summary>
    private static readonly RoundOpening Opening = new(RoundKind.ClearedArea, MayReplaceFits: true);

    public static ObjectsInClearedAreasResult Decide(ClearedAreaRule rule, ObjectsInClearedAreas.Input input)
    {
        var decisions = input.Decisions;
        var (checks, judgeTime) = Timed.Run(input.TimeSinceStart, () => JudgeAll(rule, decisions, input));
        var proposals = input.RoundJoin.ProposalsOf(checks, check => new InClearedArea(check.Area));
        if (proposals.Count == 0) return Performed(checks, [], judgeTime, input.FollowUp.Summarize(decisions));

        var copy = decisions.WorkingCopy();
        var decided = copy.DecideRound(Opening, proposals);
        var joined = JoinVerdicts(checks, decided, input);
        var followUps = FollowUp(decided, input);
        foreach (var round in followUps) decided = copy.DecideRound(round.Opening, round.Proposals);
        return Performed(joined, [new ProposedRound(Opening, proposals), .. followUps], judgeTime, input.FollowUp.Summarize(decided));
    }

    private static ClearedAreaCheck[] JudgeAll(ClearedAreaRule rule, IRemovalDecisions decisions, ObjectsInClearedAreas.Input input) =>
        ParallelMap.RunOver(input.Execution, input.Order, rule.Candidates(decisions), rule.Judge, ParallelMap.AutomaticRangeSize);

    /// <param name="decided">The decisions with the step's round, which decided every proposal and the linked group members it took along.</param>
    private static ClearedAreaCheck[] JoinVerdicts(ClearedAreaCheck[] checks, IRemovalDecisions decided, ObjectsInClearedAreas.Input input) =>
        [.. checks.Select(check => input.RoundJoin.JoinRoundDecision(check, RoundDecisionOf(new TargetId(check.TargetIndex), decided), decided.Of))];

    /// <remarks>A candidate the round did not decide keeps the decision it had, such as its fit.</remarks>
    private static Decision? RoundDecisionOf(TargetId target, IRemovalDecisions decided) =>
        decided.Of(target) is { Round.Kind: RoundKind.ClearedArea } decision ? decision : null;

    /// <summary>The also-remove rounds that follow up what the step's round took from its place; none when it took nothing.</summary>
    private static ImmutableArray<ProposedRound> FollowUp(IRemovalDecisions decided, ObjectsInClearedAreas.Input input)
    {
        var left = decided.LeftIn(decided.Rounds[^1]);
        if (left.IsEmpty) return [];
        return [.. input.FollowUp.FollowUp(decided, left).ProposedRounds.Select(proposals => new ProposedRound(AlsoRemoveRounds.Opening, proposals))];
    }

    private static ObjectsInClearedAreasResult Performed(
        IReadOnlyList<ClearedAreaCheck> checks, ImmutableArray<ProposedRound> rounds, TimeSpan judgeTime, RestingObjectsResult followUp) =>
        new(StepOutcome.Performed, checks, rounds, judgeTime, followUp, Failure: null);
}
