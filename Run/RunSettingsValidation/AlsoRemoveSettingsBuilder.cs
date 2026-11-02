using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using static AddedObjectRemover.SettingCorrections;

namespace AddedObjectRemover;

/// <summary>Builds the settings of the objects removed because they rested on removed ones, the cleaned mod's and other mods'.</summary>
internal static class AlsoRemoveSettingsBuilder
{
    internal const float MaxTouchDistance = 64f;
    internal const float MinPercent = 1f;

    private const string FollowUpModeName = SettingPaths.AlsoRemoveGroup + SettingNames.FollowUpMode;
    private const string TouchDistanceName = SettingPaths.AlsoRemoveGroup + SettingNames.TouchDistance;
    private const string AnchoringThresholdName = SettingPaths.AlsoRemoveGroup + SettingNames.AnchoringThreshold;
    private const string OtherModsObjectsName = SettingPaths.AlsoRemoveGroup + SettingNames.OtherModsObjects;

    /// <param name="touchChainsShown">Whether the detailed log and the report files, which show the touch chains, are on.</param>
    public static AlsoRemoveSettings Create(IFollowUpSettingsValues followUp, bool touchChainsShown, ICollection<SettingWarning> warnings)
    {
        var mode = ValidateDefined(followUp.Mode, SettingDefaults.FollowUpMode, FollowUpModeName, warnings);
        var touchDistance = Clamp(followUp.TouchDistance, 0, MaxTouchDistance, SettingDefaults.TouchDistance, TouchDistanceName, warnings);
        var supportPercent = Clamp(
            followUp.AnchoringThresholdPercent, MinPercent, Percent.PerWhole,
            SettingDefaults.AnchoringThresholdPercent, AnchoringThresholdName, warnings);
        return new AlsoRemoveSettings(mode, touchDistance, supportPercent / Percent.PerWhole, touchChainsShown);
    }

    /// <param name="alsoRemove">The validated group, whose gap and threshold other mods' objects are judged by too.</param>
    public static OtherModsObjectsSettings CreateForOtherModsObjects(
        IFollowUpSettingsValues followUp, AlsoRemoveSettings alsoRemove, ICollection<SettingWarning> warnings) =>
        new(
            ValidateDefined(followUp.OtherModsObjectsLeftWithoutSupport, SettingDefaults.OtherModsObjects, OtherModsObjectsName, warnings),
            alsoRemove.TouchGap,
            alsoRemove.SupportLostFraction);
}
