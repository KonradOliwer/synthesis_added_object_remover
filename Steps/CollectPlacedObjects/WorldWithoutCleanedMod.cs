using AddedObjectRemover.Rules.ObjectsThatCanCauseRemovals.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

namespace AddedObjectRemover.Steps.CollectPlacedObjects;

/// <summary>
/// Decides what the other plugins' objects would be, changed or not, in the accepted world without the cleaned mod, and in the
/// world before the cleaned mod.
/// </summary>
/// <param name="otherRecordChange">The rule for these worlds, which counts no part an earlier run gave back.</param>
internal sealed class WorldWithoutCleanedMod(
    ModTrustTable trust, WhatToCollect plan, PlacedRecordFacts facts, ChangedAreaRecordCollector changedAreaRecords, IOtherRecordChange otherRecordChange)
{
    public void DecideChangesInTheWorldsAsked(GatheredObjects gathered)
    {
        var withoutCleanedMod = DecidesWithoutCleanedMod(gathered);
        var beforeCleanedMod = plan.Needs.DecideEveryObjectBeforeCleanedMod && !facts.ChangedLands.IsEmpty;
        if (!withoutCleanedMod && !beforeCleanedMod) return;

        var recordOf = facts.Records.ToDictionary(record => record.Key);
        DecideChanges(gathered.OtherModObjects, recordOf, withoutCleanedMod, beforeCleanedMod);
        DecideChanges(gathered.SupportOnlyObjects, recordOf, withoutCleanedMod, beforeCleanedMod);
    }

    /// <remarks>
    /// Only a changed object has an old place to judge, so a run without one decides nothing in the world without the cleaned mod,
    /// unless the plan asks for every object and some placed record of another plugin the cleaned mod changed can go back.
    /// </remarks>
    private bool DecidesWithoutCleanedMod(GatheredObjects gathered) =>
        plan.Needs.JudgeOldPlaces
        && ((plan.Needs.DecideEveryObjectWithoutCleanedMod && changedAreaRecords.HasChangedRecords)
            || gathered.Targets.Any(target => target.Origin != TargetOrigin.CleanedMod));

    private void DecideChanges(
        List<OtherObject> objects, IReadOnlyDictionary<RecordKey, PlacedRecordFact> recordOf, bool withoutCleanedMod, bool beforeCleanedMod)
    {
        for (var index = 0; index < objects.Count; index++)
        {
            var record = recordOf[objects[index].Key];
            objects[index] = objects[index] with
            {
                ChangeWithoutCleanedMod = withoutCleanedMod ? otherRecordChange.Decide(record, trust.AcceptedWorldWithoutCleanedMod) : null,
                ChangeSinceBeforeCleanedMod = beforeCleanedMod ? otherRecordChange.Decide(record, trust.AcceptedWorldBeforeCleanedMod) : null,
            };
        }
    }
}
