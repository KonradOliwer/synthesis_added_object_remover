using AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport;

/// <summary>
/// The other mods' objects judged again once the restore step removed the leftovers in the areas it restores, as the step judges
/// them after the also-remove rounds. An object already taken away is judged the same again, since nothing it rested on came back,
/// and a target object already decided is not proposed again, so only what the leftovers held up is new.
/// </summary>
internal sealed class SettlingAfterLeftovers(OtherModsObjects.SettlingInput input) : IOtherModsObjectsAfterLeftovers
{
    public SettledAfterLeftovers Settle(OtherModsObjectsResult earlier, IRemovalDecisions decisions)
    {
        var again = OtherModsObjects.SettleLeftWithoutSupport(input, decisions);
        return new SettledAfterLeftovers(Joined(earlier, again), [.. again.ProposedRounds.Select(proposals => new ProposedRound(AlsoRemoveRounds.Opening, proposals))]);
    }

    /// <summary>The objects as judged again with the rounds of both settlings; a settling again that did not finish leaves the earlier objects, with its outcome.</summary>
    private static OtherModsObjectsResult Joined(OtherModsObjectsResult earlier, OtherModsObjectsResult again) =>
        again.Outcome == StepOutcome.Performed
            ? again with { Rounds = [.. earlier.Rounds, .. again.Rounds], ProposedRounds = [.. earlier.ProposedRounds, .. again.ProposedRounds] }
            : earlier with { Outcome = again.Outcome, Failure = again.Failure };
}
