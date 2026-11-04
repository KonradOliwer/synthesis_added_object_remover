using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;

/// <summary>
/// RemoveFloatingObject and PullObjectToTheGround: what becomes of the other mods' objects left without support once the
/// also-remove rounds are decided, and the also-remove rounds for the target objects resting on or touching those taken away;
/// switched off with KeepSupportingObject.
/// </summary>
public interface ILeftWithoutSupportRounds
{
    /// <summary>The rounds are decided on a working copy of <paramref name="decisions"/>, which never changes, and proposed.</summary>
    /// <param name="afterFits">The decisions with every fit: the rounds measure an object shrunk or turned and removed later at its new placement.</param>
    /// <param name="decisions">The decisions of every removal round so far, the also-remove rounds included.</param>
    OtherModsObjectsResult Settle(IRemovalDecisions afterFits, IRemovalDecisions decisions);
}
