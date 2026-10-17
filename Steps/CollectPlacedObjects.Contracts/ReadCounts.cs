namespace AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

/// <param name="OtherInvalidPlacements">Other mods' objects in target spaces that the game shows but whose position or rotation is out of range or not a number.</param>
/// <param name="RemovedByEarlierRuns">Records in target spaces that an earlier run of the batch removed, left out of this run.</param>
/// <param name="MovedByEarlierRuns">Records in target spaces that an earlier run of the batch moved, read at their new position.</param>
public sealed record ReadCounts(
    int RecordsScanned,
    int TargetsOverriddenLater,
    int TargetsHiddenOrWithoutPlacement,
    int OthersOverriddenByTarget,
    int OtherInvalidPlacements,
    int NavmeshCount,
    int RemovedByEarlierRuns,
    int MovedByEarlierRuns);
