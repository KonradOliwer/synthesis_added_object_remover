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
    [SynthesisSettingName(SettingNames.MaxStandingSizePercent + SettingNames.PercentSuffix)]
    [SynthesisTooltip(
        "The share of the cleaned mod's objects in the area that may still stand, weighed by size: each object by the volume of its box.\n"
        + "Effect-only objects such as fog add no size. 0 means none may stand. Decimals are allowed. 0-100.")]
    public float MaxStandingSizePercent { get; set; } = SettingDefaults.ClearedAreaMaxStandingSizePercent;

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.MaxStandingCountPercent + SettingNames.PercentSuffix)]
    [SynthesisTooltip(
        "The share of the cleaned mod's objects in the area that may still stand, counted by number: each object once, effect-only ones included.\n"
        + "0 means none may stand. Decimals are allowed. 0-100.")]
    public float MaxStandingCountPercent { get; set; } = SettingDefaults.ClearedAreaMaxStandingCountPercent;

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.SharesThatMustPass)]
    [SynthesisTooltip(
        "Both: the area counts as cleared only when the share by size and the share by number are each at most their limit.\n"
        + "Either: one of them is enough. Either clears more areas: a large removed piece can then clear an area where many small pieces still stand,\n"
        + "and those pieces are removed as leftovers.")]
    public ClearedAreaShares SharesThatMustPass { get; set; } = SettingDefaults.SharesThatMustPass;
}
