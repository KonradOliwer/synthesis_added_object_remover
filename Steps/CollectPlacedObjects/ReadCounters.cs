using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

namespace AddedObjectRemover.Steps.CollectPlacedObjects;

/// <summary>Counts the placed records the scan met, by what became of each.</summary>
internal sealed class ReadCounters(ModTrustTable trust)
{
    private int _cleanedModObjects;
    private int _targetsOverriddenLater;
    private int _targetsHiddenOrWithoutPlacement;
    private int _removedByEarlierPatchers;
    private int _removedByOtherPlugins;
    private int _othersOverriddenByTarget;
    private int _othersUnchanged;
    private int _otherInvalidPlacements;
    private int _clonesOfCleanedMod;
    private int _clonesOfOtherCleanedMods;

    public void CountCleanedModObject() => _cleanedModObjects++;

    /// <param name="cleanedMod">The cleaned mod whose content the clone belongs to.</param>
    public void CountClone(PluginName cleanedMod)
    {
        if (cleanedMod == trust.CleanedMod) _clonesOfCleanedMod++;
        else _clonesOfOtherCleanedMods++;
    }

    public void CountTargetOverriddenLater() => _targetsOverriddenLater++;

    public void CountTargetHiddenOrWithoutPlacement() => _targetsHiddenOrWithoutPlacement++;

    public void CountOtherOverriddenByTarget() => _othersOverriddenByTarget++;

    public void CountOtherUnchanged() => _othersUnchanged++;

    public void CountOtherInvalidPlacement() => _otherInvalidPlacements++;

    /// <param name="winner">The plugin whose version removes the record; the target's own removal counts with its hidden records.</param>
    public void CountRemovedTargetRecord(PluginName winner)
    {
        if (winner == trust.Patch) _removedByEarlierPatchers++;
        else if (winner == trust.CleanedMod) CountTargetHiddenOrWithoutPlacement();
        else _removedByOtherPlugins++;
    }

    public ReadCounts ToReadCounts(PlacedRecordFacts facts) =>
        new(
            facts.RecordsScanned,
            _cleanedModObjects,
            _targetsOverriddenLater,
            _targetsHiddenOrWithoutPlacement,
            _removedByEarlierPatchers,
            _removedByOtherPlugins,
            _othersOverriddenByTarget,
            _othersUnchanged,
            _otherInvalidPlacements,
            facts.NavmeshCount)
        {
            ClonesOfCleanedMod = _clonesOfCleanedMod,
            ClonesOfOtherCleanedMods = _clonesOfOtherCleanedMods,
        };
}
