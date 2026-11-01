using AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using Mutagen.Bethesda.Synthesis.Settings;

namespace AddedObjectRemover;

public class FollowUpRemovalSettings : IFollowUpSettingsValues
{
    [SynthesisOrder]
    [SynthesisSettingName("When an object is removed, also remove")]
    [SynthesisTooltip(
        "Nothing: of the cleaned mod's objects, only those the removal steps remove (too close, changed terrain, visible objects left behind).\n"
        + "EverythingTouchingIt: everything connected to it through touching objects, spreading one object at a time; this can reach a whole room.\n"
        + "OnlyWhatLosesItsSupport: a touching object only when enough of what it rests on was removed.")]
    public FollowUpRemovalMode Mode { get; set; } = SettingDefaults.FollowUpMode;

    [SynthesisOrder]
    [SynthesisSettingName("Gap that still counts as touching (game units)")]
    [SynthesisTooltip(
        "Two objects this close or closer count as touching. 0-64.\n"
        + "Used by EverythingTouchingIt and OnlyWhatLosesItsSupport, and for other mods' objects (the last setting of this group).")]
    public float TouchDistance { get; set; } = SettingDefaults.TouchDistance;

    [SynthesisOrder]
    [SynthesisSettingName("OnlyWhatLosesItsSupport: remove when this much support is gone (%)")]
    [SynthesisTooltip(
        "The ground and every plugin's objects count as support. 1-100.\n"
        + "Also decides when another mod's object rests on an object or is left without support (the last setting of this group),\n"
        + "and, unless \"Nothing\" is chosen above, when an object of the cleaned mod rests on another that is then never shrunk or turned.")]
    public float AnchoringThresholdPercent { get; set; } = SettingDefaults.AnchoringThresholdPercent;

    [SynthesisOrder]
    [SynthesisSettingName("Other mods' objects left without support")]
    [SynthesisTooltip(
        "What happens to another mod's object resting on an object of the cleaned mod that is removed.\n"
        + "Only objects of the base game, of the cleaned mod's masters and of mods trusted in the settings above (listed, requiring the cleaned mod, or loading before it) count; "
        + "objects of untrusted mods, of never trusted mods and of mods trusted only as masters of other mods are ignored.\n"
        + "Whatever is chosen here, an object of the cleaned mod that such an object rests on is never shrunk or turned.\n"
        + "KeepSupportingObject: the cleaned mod's object it rests on is kept as it is when, without it, the other mod's object would be left without support, judged as the other two choices judge it.\n"
        + "When only taking several of them away together would do that, all of them are kept.\n"
        + "RemoveFloatingObject: the other mod's object is removed too.\n"
        + "PullObjectToTheGround: the other mod's object is moved straight down onto what is below it, keeping its rotation;\n"
        + "with nothing below it within 2048 units, it is removed.\n"
        + "Other mods' objects that something depends on (persistent ones, ones another object links to, ...) are never removed.\n"
        + "With the last two, an object of the cleaned mod that rests on an object of another mod that is removed or pulled down is removed or kept as \"When an object is removed, also remove\" says.")]
    public OtherModsObjectsHandling OtherModsObjectsLeftWithoutSupport { get; set; } = SettingDefaults.OtherModsObjects;
}
