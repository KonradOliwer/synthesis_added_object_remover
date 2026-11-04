using AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;

/// <summary>What the changed-terrain step needs the scan to read.</summary>
public static class ChangedTerrainScanNeeds
{
    /// <summary>Every plugin's terrain is read only when the step runs, its texture layers only when it counts terrain types, and its water only when it counts a water change.</summary>
    public static ScanNeeds Of(ChangedTerrainSettings settings) =>
        new(TerrainVersions: settings.Runs, TerrainLayers: settings.Runs && settings.CountTerrainTypeChanges, Water: settings.Runs && settings.CountsWaterChanges);

    /// <summary>
    /// When only the changed objects' check is on, the scan reads terrain only if the cleaned mod overrides a record of another plugin;
    /// whether an override is a changed object is known only after the scan.
    /// </summary>
    /// <param name="cleanedMod">The settings of the cleaned mod's own objects.</param>
    /// <param name="changedObjects">The settings of the objects the cleaned mod changed.</param>
    public static ScanNeeds OfBoth(ChangedTerrainSettings cleanedMod, ChangedTerrainSettings changedObjects) =>
        ScanNeeds.UnionOf(Of(cleanedMod), Of(changedObjects)) with { TerrainForChangedObjectsOnly = !cleanedMod.Runs && changedObjects.Runs };
}
