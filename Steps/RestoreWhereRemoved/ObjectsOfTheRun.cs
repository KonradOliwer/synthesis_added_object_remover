using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>Which objects the run removes and which stay, as the restore reads them.</summary>
internal static class ObjectsOfTheRun
{
    /// <summary>The placed records a round or the other mods' objects step remove.</summary>
    public static HashSet<RecordKey> RemovedByRun(CollectedObjects collected, IRemovalDecisions final, OtherModsObjectsResult otherModsObjects) =>
        collected.Targets.Where(target => final.IsRemoved(target.Id)).Select(target => target.Key)
            .Concat(otherModsObjects.Objects.Where(outcome => outcome.IsRemoved).Select(outcome => outcome.Object.Key))
            .ToHashSet();

    /// <summary>Each target that is not removed, where it stands last, but the placed records whose areas let them go back.</summary>
    /// <param name="cleared">The placed records whose areas let them go back.</param>
    public static List<TargetObject> TargetsThatMayStay(CollectedObjects collected, IRemovalDecisions final, List<RecordChangedByCleanedMod> cleared)
    {
        var goingBack = cleared.Select(change => change.Key).ToHashSet();
        return [.. AfterRemovalWorld.TargetsWhereTheyStandLast(collected.Targets, final).Where(target => !goingBack.Contains(target.Key))];
    }
}
