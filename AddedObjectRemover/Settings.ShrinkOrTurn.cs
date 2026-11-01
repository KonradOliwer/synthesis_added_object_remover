using AddedObjectRemover.Steps.FitClashingObjects.Contracts;
using Mutagen.Bethesda.Synthesis.Settings;

namespace AddedObjectRemover;

public class ShrinkOrTurnSettings : IShrinkOrTurnSettingsValues
{
    [SynthesisOrder]
    [SynthesisSettingName("Shrink or turn objects that are too close")]
    [SynthesisTooltip(
        "Off: an object too close to another mod's object is removed without first being tried shrunk or turned; the two settings below are then not used.")]
    public bool ShrinkOrTurnObjects { get; set; } = SettingDefaults.ShrinkOrTurnObjects;

    [SynthesisOrder]
    [SynthesisSettingName("Shrink down to (% of its size)")]
    [SynthesisTooltip(
        "An object that would be removed for being too close to another mod's object may be shrunk where it stands, down to this share of its size, to make it fit,\n"
        + "in steps of 5%; the largest size that fits is used. This applies to the cleaned mod's own objects.\n"
        + "100 or more: objects are never shrunk. At least 1 (a lower value becomes 1). Not used when the switch above is off.")]
    public int ShrinkLimitPercent { get; set; } = SettingDefaults.ShrinkLimitPercent;

    [SynthesisOrder]
    [SynthesisSettingName("Turn by at most (degrees either way)")]
    [SynthesisTooltip(
        "An object may be turned about its upright axis by up to this many degrees to the left or to the right to make it fit,\n"
        + "in steps of 15 degrees; the smallest turn that fits is used. It is never tilted.\n"
        + "0: objects are never turned. 180 or more: objects may face any way.\n"
        + "Not below 0 (a lower value becomes 0). Not used when the switch above is off.")]
    public int TurnLimitDegrees { get; set; } = SettingDefaults.TurnLimitDegrees;

    int IShrinkOrTurnSettingsValues.ShrinkLimitPercent => ShrinkOrTurnObjects ? ShrinkLimitPercent : FitSettings.Off.ShrinkLimitPercent;
    int IShrinkOrTurnSettingsValues.TurnLimitDegrees => ShrinkOrTurnObjects ? TurnLimitDegrees : FitSettings.Off.TurnLimitDegrees;
}
