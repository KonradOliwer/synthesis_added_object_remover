using System.Diagnostics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

namespace AddedObjectRemover.Steps.CollectPlacedObjects;

internal enum RecordRole { Target, TargetOverriddenLater, Moved, Replaced, OverriddenByTarget, OtherUnchanged, OtherChanged }

/// <param name="Change">Only for <see cref="RecordRole.OtherChanged"/>.</param>
/// <param name="EarlierVersion">Only for <see cref="RecordRole.Moved"/> and <see cref="RecordRole.Replaced"/>.</param>
internal readonly record struct Classification(RecordRole Role, ObjectChange? Change, PlacedVersionFact? EarlierVersion)
{
    /// <summary>Null for a record that is not a target object.</summary>
    public TargetOrigin? Origin => Role switch
    {
        RecordRole.Target => TargetOrigin.CleanedMod,
        RecordRole.Moved => TargetOrigin.Moved,
        RecordRole.Replaced => TargetOrigin.Replaced,
        RecordRole.TargetOverriddenLater or RecordRole.OverriddenByTarget or RecordRole.OtherUnchanged or RecordRole.OtherChanged => null,
        _ => throw new UnreachableException($"Unknown record role {Role}."),
    };

    public bool IsJudged => Origin is not null;
}

/// <summary>Gives a placed record its role in the run.</summary>
internal sealed class RecordClassifier(ModTrustTable trust, AcceptedWorld world, WhatToCollect plan, PlacedRecordFacts facts)
{
    /// <summary>Creator first: the target's own records, then the records the target overrides, then the rule decides once.</summary>
    public Classification Classify(PlacedRecordFact record)
    {
        if (IsCreatedByTarget(record)) return new(WinsInTargetOrPatch(record) ? RecordRole.Target : RecordRole.TargetOverriddenLater, null, null);
        if (facts.OverriddenRecords.Contains(record.Key)) return ClassifyOverriddenRecord(record);
        var change = OtherRecordChange.Decide(record, world);
        return new(change is null ? RecordRole.OtherUnchanged : RecordRole.OtherChanged, change, null);
    }

    public bool IsCreatedByTarget(PlacedRecordFact record) => record.Key.Plugin == trust.CleanedMod;

    private Classification ClassifyOverriddenRecord(PlacedRecordFact record)
    {
        var changed = ChangedObjectKind.Of(record, trust.CleanedMod, world, plan.MovedDistance);
        return changed switch
        {
            { Origin: TargetOrigin.Moved } => new(RecordRole.Moved, null, changed.Value.EarlierVersion),
            { Origin: TargetOrigin.Replaced } when plan.ProcessReplaced => new(RecordRole.Replaced, null, changed.Value.EarlierVersion),
            _ => new(RecordRole.OverriddenByTarget, null, null),
        };
    }

    /// <remarks>The patch's version holds what earlier patchers in this group wrote, so a target record it wins is judged as the patch left it.</remarks>
    private bool WinsInTargetOrPatch(PlacedRecordFact record) => record.WinningMod == trust.CleanedMod || record.WinningMod == trust.Patch;
}
