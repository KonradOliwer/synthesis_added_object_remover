using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.RemoveWholeGroups.Contracts;
using static AddedObjectRemover.SettingCorrections;

namespace AddedObjectRemover;

/// <summary>Builds the settings of the step that removes whole groups of the cleaned mod's objects holding a clash removal.</summary>
internal static class WholeGroupsSettingsBuilder
{
    internal const float MinGroupGap = 1f;
    internal const float MaxGroupGap = 4096f;

    /// <summary>Built (and its settings validated) even when the step is off, so invalid values are always reported.</summary>
    public static WholeGroupsSettings? Create(ILeftoverObjectsSettingsValues values, ICollection<SettingWarning> warnings)
    {
        var settings = new WholeGroupsSettings(
            Clamp(values.GroupGapInUnits, MinGroupGap, MaxGroupGap, SettingDefaults.GroupGapInUnits, SettingPaths.LeftoverObjectsGroup + SettingNames.GroupGap, warnings));
        return values.RemoveWholeGroups ? settings : null;
    }
}
