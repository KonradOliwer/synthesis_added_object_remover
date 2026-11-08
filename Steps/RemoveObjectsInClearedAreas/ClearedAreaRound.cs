using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsInClearedAreas.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsInClearedAreas;

/// <summary>The step's one round: every candidate judged on the decisions of the earlier steps, all removals proposed together.</summary>
internal static class ClearedAreaRound
{
    private static readonly RoundOpening Opening = RoundOpening.ReplacingFits(RoundKind.ClearedArea);

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
