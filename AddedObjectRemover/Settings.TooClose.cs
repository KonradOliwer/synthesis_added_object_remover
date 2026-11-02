using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;
using Mutagen.Bethesda.Synthesis.Settings;

namespace AddedObjectRemover;

public class TooCloseRemovalSettings : ITooCloseSettingsValues
{
    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.MarginBySize)]
    [SynthesisTooltip("Bigger objects get a bigger margin. An object is removed when another mod's object comes within this margin around it.")]
    public TooCloseBySizeSettings BySize { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.MarginByDistance)]
    [SynthesisTooltip("Every object gets the same margin. An object is removed when another mod's object comes within this margin around it.")]
    public TooCloseByDistanceSettings ByDistance { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName("Use rough boxes instead of exact shapes (faster)")]
    [SynthesisTooltip(
        "Off: the object's real shape, enlarged by the margin, is compared with the other object's real shape.\n"
        + "On: the object's box, enlarged by the margin, is compared with the other object's centre point. Faster, but two large\n"
        + "objects that overlap only at their edges can be missed.\n"
        + "Applies to both margins.")]
    public bool UseRoughBoxes { get; set; } = SettingDefaults.UseRoughBoxes;

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.Npcs)]
    [SynthesisTooltip(
        "AlwaysLikeAnyObject: they count like any other object.\n"
        + "OnlyWhenStuckInsideTheObject: only when their body sinks into the object or they stand inside it (a boulder, a room of a building);\n"
        + "standing on or leaning against it does not count.\n"
        + "OnlyWhenStuckInsideTheObjectWithCollision: the same, but only for objects you cannot walk through (a rock, a tree, a wall);\n"
        + "objects without collision (ferns, shrubs, thickets, kelp) are never removed because an NPC stands in them.\n"
        + "Never: they never cause removal.")]
    public NpcHandling NpcHandling { get; set; } = SettingDefaults.Npcs;

    bool ITooCloseSettingsValues.BySize => (BySize ?? new()).Use;
    float ITooCloseSettingsValues.SizeMultiplier => (BySize ?? new()).SizeMultiplier;
    bool ITooCloseSettingsValues.ByDistance => (ByDistance ?? new()).Use;
    float ITooCloseSettingsValues.DistanceInUnits => (ByDistance ?? new()).DistanceInUnits;
    NpcHandling ITooCloseSettingsValues.Npcs => NpcHandling;
}
