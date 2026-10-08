namespace AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

/// <param name="OtherInvalidPlacements">Other mods' objects in target spaces that the game shows but whose position or rotation is out of range or not a number.</param>
public sealed record ReadCounts(
    int RecordsScanned,
    int TargetsOverriddenLater,
    int TargetsHiddenOrWithoutPlacement,
    int OthersOverriddenByTarget,
    int OtherInvalidPlacements,
    int NavmeshCount);
