using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

namespace AddedObjectRemover.Steps.CollectPlacedObjects;

/// <summary>Decides what the other plugins' objects would be, changed or not, in the accepted world without the cleaned mod.</summary>
internal sealed class WorldWithoutCleanedMod(ModTrustTable trust, WhatToCollect plan, PlacedRecordFacts facts, ChangedAreaRecordCollector changedAreaRecords)
{
    /// <remarks>
    /// Only a changed object has an old place to judge, so a run without one decides nothing in the world without the cleaned mod,
    /// unless the plan asks for every object and some placed record of another plugin the cleaned mod changed can go back.
    /// </remarks>
    public void DecideChangesIfOldPlacesAreJudged(GatheredObjects gathered)
    {
        if (!plan.Needs.JudgeOldPlaces) return;
        var everyObject = plan.Needs.DecideEveryObjectWithoutCleanedMod && changedAreaRecords.HasChangedRecords;
        if (!everyObject && gathered.Targets.All(target => target.Origin == TargetOrigin.CleanedMod)) return;

        var recordOf = facts.Records.ToDictionary(record => record.Key);
        DecideChanges(gathered.OtherModObjects, recordOf);
        DecideChanges(gathered.SupportOnlyObjects, recordOf);
    }

    private void DecideChanges(List<OtherObject> objects, IReadOnlyDictionary<RecordKey, PlacedRecordFact> recordOf)
    {
        for (var index = 0; index < objects.Count; index++)
        {
            var change = OtherRecordChange.Decide(recordOf[objects[index].Key], trust.AcceptedWorldWithoutCleanedMod);
            objects[index] = objects[index] with { ChangeWithoutCleanedMod = change };
        }
    }
}
