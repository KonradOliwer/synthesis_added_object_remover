using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport.Contracts;

namespace AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport;

/// <summary>
/// KeepSupportingObject: the target objects other mods' objects rest on, directly or through other target objects, each where
/// it stands before any decision (see <see cref="HeldUpTargetsFinder"/>); switched off otherwise.
/// </summary>
internal sealed class HeldUpTargetsSearch(OtherModsObjects.SupportJudging judging) : IHeldUpTargetsSearch
{
    public HeldUpTargets Find()
    {
        if (judging.Settings.Handling != OtherModsObjectsHandling.KeepSupportingObject) return HeldUpTargets.SwitchedOff;

        return Failures.Attempt(
            () => new HeldUpTargets(StepOutcome.Performed, new HeldUpTargetsFinder(judging.Targets, judging.CreateHoldingBeforeAnyDecision(), judging.Execution).Find(), Failure: null),
            failure => new HeldUpTargets(
                StepOutcome.StoppedByUnexpectedError, new Dictionary<TargetId, RestingObject>(), PartFailure.OfWholePart(OtherModsObjectsParts.HeldUpTargets, failure)));
    }
}
