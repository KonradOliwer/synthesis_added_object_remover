using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.JudgeChangedObjects.Contracts;
using static AddedObjectRemover.SettingCorrections;

namespace AddedObjectRemover;

/// <summary>Builds the settings of the other mods' objects the cleaned mod changed.</summary>
internal static class ChangedObjectSettingsBuilder
{
    private const string MovedDistanceName = SettingPaths.MovedObjectsGroup + SettingNames.MovedDistance;
    private const string MovedWhenRemovedName = SettingPaths.MovedObjectsGroup + SettingNames.WhenRemoved;
    private const string ReplacedWhenRemovedName = SettingPaths.ReplacedObjectsGroup + SettingNames.WhenRemoved;

    public static float ValidateMovedDistance(IChangedObjectsSettingsValues values, ICollection<SettingWarning> warnings) =>
        NotNegative(values.Moved.MovedDistanceInUnits, SettingDefaults.MovedDistanceInUnits, MovedDistanceName, warnings);

    public static ChangedObjectSettings Create(IChangedObjectsSettingsValues values, ICollection<SettingWarning> warnings) =>
        new(
            ValidateDefined(values.Moved.WhenRemoved, SettingDefaults.MovedObjectsWhenRemoved, MovedWhenRemovedName, warnings),
            ValidateDefined(values.Replaced.WhenRemoved, SettingDefaults.ReplacedObjectsWhenRemoved, ReplacedWhenRemovedName, warnings));

    /// <summary>Validated even when no object is checked with it, so invalid values are always reported.</summary>
    public static ChangedObjectCollision CreateCollision(IChangedObjectsSettingsValues values, ICollection<SettingWarning> warnings)
    {
        var collision = CollisionSettingsBuilder.CreateForChangedObjects(values.Collision, warnings);
        return new ChangedObjectCollision(
            collision.TooClose, collision.ChangedTerrain, collision.Fit, values.Moved.AttemptResizingBeforeRemoval, values.Replaced.AttemptResizingBeforeRemoval);
    }
}
