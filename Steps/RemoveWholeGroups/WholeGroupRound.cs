using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveWholeGroups.Contracts;

namespace AddedObjectRemover.Steps.RemoveWholeGroups;

/// <summary>
/// The step's one round: the groups seeded by the decisions of the earlier steps, every standing candidate in them proposed together,
/// then the also-remove follow-up of what the round took from its place, decided on a working copy. Nothing is grouped again after the
/// round.
/// </summary>
internal static class WholeGroupRound
{
    /// <summary>An object kept shrunk or turned still stands where it was fitted, so the round decides it too.</summary>
    private static readonly RoundOpening Opening = new(RoundKind.WholeGroup, MayReplaceFits: true);

    public static WholeGroupsResult Decide(WholeGroupRule rule, WholeGroups.Input input)
    {
        var decisions = input.Decisions;
        var groups = rule.FindSeededGroups(decisions);
        var round = input.RoundJoin.DecideOneRound(
            decisions, Opening, rule.Judge(groups, decisions), check => new InGroupWithClash(groups[check.GroupIndex].Seed, groups[check.GroupIndex].Members.Length), input.FollowUp);
        return new WholeGroupsResult(StepOutcome.Performed, groups, round.Checks, round.ProposedRounds, round.FollowUp, Failure: null);
    }
}
