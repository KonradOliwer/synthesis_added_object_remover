using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using Mutagen.Bethesda.Synthesis.Settings;

namespace AddedObjectRemover;

public class FollowUpRemovalSettings : IFollowUpSettingsValues
{
    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.FollowUpMode)]
    [SynthesisTooltip(
        "Nothing: of the cleaned mod's objects, only those the removal steps remove (too close, changed terrain, objects left behind).\n"
        + "EverythingTouchingIt: everything connected to it through touching objects, spreading one object at a time; this can reach a whole room.\n"
        + "OnlyWhatLosesItsSupport: a touching object only when enough of what it rests on was removed.")]
    public FollowUpRemovalMode Mode { get; set; } = SettingDefaults.FollowUpMode;

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.TouchDistance + SettingNames.GameUnitsSuffix)]
    [SynthesisTooltip(
        "Two objects this close or closer count as touching. 0-64.\n"
        + "Used by EverythingTouchingIt and OnlyWhatLosesItsSupport, and for other mods' objects (the last setting of this group).")]
    public float TouchDistance { get; set; } = SettingDefaults.TouchDistance;

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.AnchoringThreshold + SettingNames.PercentSuffix)]
    [SynthesisTooltip(
        "The ground and every plugin's objects count as support. 1-100.\n"
        + "Also decides when another mod's object rests on an object or is left without support (the last setting of this group),\n"
        + "and, unless \"Nothing\" is chosen above, when an object of the cleaned mod rests on another that is then never shrunk or turned.")]
    public float AnchoringThresholdPercent { get; set; } = SettingDefaults.AnchoringThresholdPercent;

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.OtherModsObjects)]
    [SynthesisTooltip(
        "What happens to another mod's object resting on an object of the cleaned mod that is removed. Only objects of the base game, the cleaned mod's masters and trusted mods count.\n"
        + "KeepSupportingObject: the cleaned mod's supporting object is kept. RemoveFloatingObject: the other mod's object is removed too. "
        + "PullObjectToTheGround: it is moved straight down onto what is below it, or removed with nothing below within 2048 units.\n"
        + "Objects something depends on are never removed. Details: see the README.")]
    public OtherModsObjectsHandling OtherModsObjectsLeftWithoutSupport { get; set; } = SettingDefaults.OtherModsObjects;
}
