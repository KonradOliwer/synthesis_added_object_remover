using System.Collections.Immutable;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveWholeGroups.Contracts;

namespace AddedObjectRemover.Steps.RemoveWholeGroups;

/// <summary>The step's one round: the groups seeded by the decisions of the earlier steps, every standing candidate in them proposed together. Nothing is grouped again after the round.</summary>
internal static class WholeGroupRound
{
    private static readonly RoundOpening Opening = RoundOpening.ReplacingFits(RoundKind.WholeGroup);

    public static WholeGroupsResult Decide(WholeGroupRule rule, WholeGroups.Input input)
    {
        var decisions = input.Decisions;
        var groups = rule.FindSeededGroups(decisions);
        var round = input.RoundJoin.DecideOneRound(decisions, Opening, rule.Judge(groups, decisions), check => ReasonOf(check, groups), input.FollowUp);
        return new WholeGroupsResult(StepOutcome.Performed, groups, round.Checks, round.ProposedRounds, round.FollowUp, Failure: null);
    }

    private static InGroupWithClash ReasonOf(WholeGroupCheck check, ImmutableArray<SeededGroup> groups)
    {
        var group = groups[check.GroupIndex];
        return new InGroupWithClash(group.Seed, group.Members.Length);
    }
}
