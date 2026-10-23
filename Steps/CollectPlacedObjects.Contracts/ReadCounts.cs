namespace AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

/// <param name="CleanedModObjects">Every placed record the target plugin created, whatever its final version.</param>
/// <param name="TargetsOverriddenLater">Target records whose final version, not removed, comes from a plugin other than the target and the patch.</param>
/// <param name="TargetsHiddenOrWithoutPlacement">Winning target records the game does not show: removed by the target itself, initially disabled, without a usable placement, or moved by the patch into a space that holds no target record.</param>
/// <param name="RemovedByEarlierPatchers">Target records whose final version is the patch's and removed: an earlier patcher in this group removed them.</param>
/// <param name="RemovedByOtherPlugins">Target records whose final version comes from a plugin other than the target and the patch and is removed.</param>
/// <param name="OthersUnchanged">
/// Other mods' objects in target spaces that the game shows with their accepted values; the changed ones are the other-mod objects.
/// </param>
/// <param name="OtherInvalidPlacements">Other mods' objects in target spaces that the game shows but whose position or rotation is out of range or not a number.</param>
public sealed record ReadCounts(
    int RecordsScanned,
    int CleanedModObjects,
    int TargetsOverriddenLater,
    int TargetsHiddenOrWithoutPlacement,
    int RemovedByEarlierPatchers,
    int RemovedByOtherPlugins,
    int OthersOverriddenByTarget,
    int OthersUnchanged,
    int OtherInvalidPlacements,
    int NavmeshCount);
