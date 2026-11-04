using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport;

/// <summary>
/// The step for other mods' objects left without support: once the also-remove rounds are decided, what becomes of the other
/// mods' objects left without support, with the step's also-remove rounds added to the run's decisions.
/// </summary>
public static class OtherModsObjects
{
    /// <param name="Rounds">What becomes of the other mods' objects left without support.</param>
    /// <param name="AfterFits">The decisions with every fit: the rounds measure an object shrunk or turned and removed later at its new placement.</param>
    /// <param name="Decisions">The decisions of every removal round so far, the also-remove rounds included; the step's rounds are added to them.</param>
    public sealed record Input(ILeftWithoutSupportRounds Rounds, IRemovalDecisions AfterFits, IRunDecisions Decisions);

    /// <summary>
    /// RemoveFloatingObject and PullObjectToTheGround: what becomes of the other mods' objects left without support, with the
    /// step's also-remove rounds added to the run's decisions.
    /// </summary>
    public static Decided<OtherModsObjectsResult> Settle(Input input)
    {
        var result = input.Rounds.Settle(input.AfterFits, input.Decisions);
        return new Decided<OtherModsObjectsResult>(result, input.Decisions.ApplyRounds(AlsoRemoveRounds.Opening, result.ProposedRounds));
    }
}
