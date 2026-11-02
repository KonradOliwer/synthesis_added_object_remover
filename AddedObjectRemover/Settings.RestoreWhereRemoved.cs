using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;
using Mutagen.Bethesda.Synthesis.Settings;

namespace AddedObjectRemover;

public class RestoreWhereRemovedSettings : IRestoreWhereRemovedSettingsValues
{
    [SynthesisOrder]
    [SynthesisSettingName("Restore other mods' objects")]
    [SynthesisTooltip(
        "Puts back, in a cleared area, the other mods' objects the cleaned mod moved, turned, resized, hid, disabled, deleted or gave another model.\n"
        + "Objects that would be removed because they are in the way are handled under \"Other mods' objects the cleaned mod changed\" instead.")]
    public bool RestoreOtherModsObjects { get; set; } = SettingDefaults.RestoreOtherModsObjects;

    [SynthesisOrder]
    [SynthesisSettingName("Restore terrain")]
    [SynthesisTooltip(
        "Puts back the ground heights, ground colours and ground textures the cleaned mod changed in a cleared area.\n"
        + "Ground under objects that stay is kept as the cleaned mod made it.")]
    public bool RestoreTerrain { get; set; } = SettingDefaults.RestoreTerrain;

    [SynthesisOrder]
    [SynthesisSettingName("Restore cell settings")]
    [SynthesisTooltip(
        "Puts back the settings of the cells in a cleared area that the cleaned mod changed: lighting, image space, acoustic space, music, water, regions, name, owner, encounter zone, location and every other cell setting.")]
    public bool RestoreCells { get; set; } = SettingDefaults.RestoreCells;

    [SynthesisOrder]
    [SynthesisSettingName("Restore navmeshes")]
    [SynthesisTooltip(
        "Puts back the navmeshes the cleaned mod edited, deleted or replaced in a cleared area, and cuts off the ones it added, so characters walk the earlier paths again.\n"
        + "A navmesh goes back only when every navmesh and door it is linked with can go back too.")]
    public bool RestoreNavmeshes { get; set; } = SettingDefaults.RestoreNavmeshes;

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.ClearedArea)]
    [SynthesisTooltip(
        "Nothing is restored unless the cleaned mod's visible objects around the place have mostly been removed.\n"
        + "In an interior, the whole interior is one area. If the cleaned mod has no visible object in the area, nothing is restored there.")]
    public ClearedAreaSettings ClearedArea { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.EdgeBlendDistance + SettingNames.GameUnitsSuffix)]
    [SynthesisTooltip(
        "Where restored ground meets ground that stays as the cleaned mod changed it, the height is blended over this distance, so no step or crack shows.\n"
        + "0-1024; 0 means no blending.")]
    public float TerrainEdgeBlendDistanceInUnits { get; set; } = SettingDefaults.TerrainEdgeBlendDistanceInUnits;

    IClearedAreaSettingsValues IRestoreWhereRemovedSettingsValues.ClearedArea => ClearedArea ?? new();
}

public class ClearedAreaSettings : IClearedAreaSettingsValues
{
    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.ClearedAreaRadius + SettingNames.GameUnitsSuffix)]
    [SynthesisTooltip(
        "The area is everything this far from the place in every direction, so it is twice this wide. 1024-16384.")]
    public float SearchRadius { get; set; } = SettingDefaults.ClearedAreaSearchRadius;

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.MaxStandingPercent + SettingNames.PercentSuffix)]
    [SynthesisTooltip(
        "The area counts as cleared when at most this share of the cleaned mod's visible objects in it is still standing, measured as set below.\n"
        + "0.1 is a tenth of a percent; 0 means none may stand. Decimals are allowed. 0-100.")]
    public float MaxStandingPercent { get; set; } = SettingDefaults.ClearedAreaMaxStandingPercent;

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.Measure)]
    [SynthesisTooltip(
        "ObjectSize: each object counts by the volume of its box, so a house counts for more than a bucket.\n"
        + "ObjectCount: each object counts once.")]
    public ClearedAreaMeasure Measure { get; set; } = SettingDefaults.AreaMeasure;
}
