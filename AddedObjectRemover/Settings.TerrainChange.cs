using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;
using Mutagen.Bethesda.Synthesis.Settings;

namespace AddedObjectRemover;

public class TerrainChangeSettings : ITerrainChangeSettingsValues
{
    [SynthesisOrder]
    [SynthesisSettingName("Remove objects on changed terrain")]
    [SynthesisTooltip("Switches this removal on.")]
    public bool RemoveOnChangedTerrain { get; set; } = SettingDefaults.RemoveOnChangedTerrain;

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.TerrainCheckedObjects)]
    [SynthesisTooltip(
        "VisibleObjectsOnTheGround: visible objects that reach the ground (objects on roofs, bridges or high shelves are skipped).\n"
        + "VisibleObjectsAtAnyHeight: every visible object.\n"
        + "AllObjectsIncludingInvisibleOnes: also markers, lights and sounds.")]
    public TerrainCheckedObjects TerrainCheckedObjects { get; set; } = SettingDefaults.TerrainChecked;

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.MarginBySize)]
    [SynthesisTooltip(
        "The terrain under the object plus this margin around it is checked for height, ground type and water changes.\n"
        + "Bigger objects get a bigger margin. The terrain points right around the object are always checked.")]
    public TerrainBySizeSettings BySize { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.MarginByDistance)]
    [SynthesisTooltip(
        "The terrain under the object plus this margin around it is checked for height, ground type and water changes.\n"
        + "Every object gets the same margin. The terrain points right around the object are always checked.")]
    public TerrainByDistanceSettings ByDistance { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName("Count height changes")]
    [SynthesisTooltip("Whether a change of the ground's height counts.")]
    public bool CountHeightChanges { get; set; } = SettingDefaults.CountHeightChanges;

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.HeightChangeTolerance + SettingNames.GameUnitsSuffix)]
    [SynthesisTooltip(
        "Small smoothing edits are common and rarely visible under an object. 0 counts every change. 0-256.\n"
        + "Counting uses it only when \"Count height changes\" is on.\n"
        + "With that switch on or off, it also decides which height changes a mod trusted for its own changes owns: only those larger than this.")]
    public float HeightChangeTolerance { get; set; } = SettingDefaults.HeightChangeTolerance;

    [SynthesisOrder]
    [SynthesisSettingName("Count ground type changes (for example grass to road)")]
    [SynthesisTooltip("The kind of ground: grass, dirt, stone, snow and so on. One grass texture replaced by another is not a change.")]
    public bool CountTerrainTypeChanges { get; set; } = SettingDefaults.CountTerrainTypeChanges;

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.TerrainTypeChangePercent + SettingNames.PercentSuffix)]
    [SynthesisTooltip(
        "A ground type change removes an object only when at least this share of the terrain points checked around it changed type.\n"
        + "Small touch-ups at the edges of painted patches then don't remove objects. 0 removes on any one changed point. 0-100.\n"
        + "Height changes are not affected. Only used when \"Count ground type changes\" is on.")]
    public int MinTerrainTypeChangePercent { get; set; } = SettingDefaults.MinTerrainTypeChangePercent;

    [SynthesisOrder]
    [SynthesisSettingName("Count water height changes")]
    [SynthesisTooltip(
        "Whether another mod raising or lowering a cell's water counts, when that puts ground around the object under water\n"
        + "or out of it. Water over ground that stays covered is not seen.")]
    public bool CountWaterHeightChanges { get; set; } = SettingDefaults.CountWaterHeightChanges;

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.WaterHeightChangeTolerance + SettingNames.GameUnitsSuffix)]
    [SynthesisTooltip(
        "Small water level edits rarely matter. 0 counts every change. 0-256.\n"
        + "Counting uses it only when \"Count water height changes\" is on.\n"
        + "With that switch on or off, it also decides which water height changes a mod trusted for its own changes owns: only those larger than this.")]
    public float WaterHeightChangeTolerance { get; set; } = SettingDefaults.WaterHeightChangeTolerance;

    [SynthesisOrder]
    [SynthesisSettingName("Count water added/removed changes")]
    [SynthesisTooltip(
        "Whether another mod adding water to a cell or taking its water away counts, when that puts ground around the object\n"
        + "under water or out of it. Water over ground that stays covered is not seen.\n"
        + "A world that stores no default water height has no default water: adding or dropping that default adds or takes away\n"
        + "the water of the cells that use it.")]
    public bool CountWaterAddedOrRemoved { get; set; } = SettingDefaults.CountWaterAddedOrRemoved;

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.WaterCoverChangePercent + SettingNames.PercentSuffix)]
    [SynthesisTooltip(
        "A water change removes an object only when at least this share of the terrain points checked around it went under water\n"
        + "or came out of it. A point that changed only because the ground itself was raised or lowered is left to the height check.\n"
        + "0 removes on any one such point. 0-100. Only used when one of the two water switches above is on.")]
    public int MinWaterCoverChangePercent { get; set; } = SettingDefaults.MinWaterCoverChangePercent;

    bool ITerrainChangeSettingsValues.AreaBySize => (BySize ?? new()).Use;
    float ITerrainChangeSettingsValues.AreaSizeMultiplier => (BySize ?? new()).SizeMultiplier;
    bool ITerrainChangeSettingsValues.AreaByDistance => (ByDistance ?? new()).Use;
    float ITerrainChangeSettingsValues.AreaDistanceInUnits => (ByDistance ?? new()).DistanceInUnits;
}
