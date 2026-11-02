using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.CollectPlacedObjects;

/// <summary>What the scan has made of the placed records so far, in the order the records came.</summary>
internal sealed class GatheredObjects
{
    public List<TargetObject> Targets { get; } = [];

    public List<OtherObject> OtherModObjects { get; } = [];

    public List<OtherObject> SupportOnlyObjects { get; } = [];

    public List<OverriddenOtherRecord> OverriddenOthers { get; } = [];

    public List<RecordKey> RemovedRecords { get; } = [];
}
