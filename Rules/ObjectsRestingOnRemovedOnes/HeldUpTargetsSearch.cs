using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes;

/// <summary>
/// KeepSupportingObject: the target objects other mods' objects rest on, directly or through other target objects, each where
/// it stands before any decision (see <see cref="HeldUpTargetsFinder"/>); switched off otherwise.
/// </summary>
internal sealed class HeldUpTargetsSearch(SupportJudging judging) : IHeldUpTargetsSearch
{
    public HeldUpTargets Find()
    {
        if (judging.Settings.Handling != OtherModsObjectsHandling.KeepSupportingObject) return HeldUpTargets.SwitchedOff;

        return Failures.Attempt(
            () => new HeldUpTargets(StepOutcome.Performed, new HeldUpTargetsFinder(judging.Targets, SupportJudgingParts.CreateHoldingBeforeAnyDecision(judging), judging.Execution).Find(), Failure: null),
            failure => new HeldUpTargets(
                StepOutcome.StoppedByUserDataFailure, new Dictionary<TargetId, RestingObject>(), PartFailure.OfWholePart(OtherModsObjectsParts.HeldUpTargets, failure)));
    }
}
