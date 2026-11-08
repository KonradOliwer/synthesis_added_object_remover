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
        var round = input.RoundJoin.DecideOneRound(decisions, Opening, checks, check => new InClearedArea(check.Area), input.FollowUp);
        return new ObjectsInClearedAreasResult(StepOutcome.Performed, round.Checks, round.ProposedRounds, judgeTime, round.FollowUp, Failure: null);
    }

    private static ClearedAreaCheck[] JudgeAll(ClearedAreaRule rule, IRemovalDecisions decisions, ObjectsInClearedAreas.Input input) =>
        ParallelMap.RunOver(input.Execution, input.Order, rule.Candidates(decisions), rule.Judge, ParallelMap.AutomaticRangeSize);
}
