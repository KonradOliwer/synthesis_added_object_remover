using AddedObjectRemover.Run.RunSettingsValidation.Contracts;

namespace AddedObjectRemover;

/// <summary>The names the settings screen shows, group first, so a warning leads to the setting. A group path ends in the separator.</summary>
internal static class SettingPaths
{
    public const string StandardGroupPath = "";
    public const string TooCloseGroup = SettingNames.TooClose + SettingNames.PathSeparator;
    public const string TerrainGroup = SettingNames.ChangedTerrain + SettingNames.PathSeparator;
    public const string ShrinkOrTurnGroup = SettingNames.ShrinkOrTurn + SettingNames.PathSeparator;
    public const string SizeMargin = SettingNames.MarginBySize + SettingNames.PathSeparator + SettingNames.MarginOnEverySide;
    public const string DistanceMargin = SettingNames.MarginByDistance + SettingNames.PathSeparator + SettingNames.MarginOnEverySide;
    public const string ChangedObjectsGroup = SettingNames.ChangedObjects + SettingNames.PathSeparator;
    public const string MovedObjectsGroup = ChangedObjectsGroup + SettingNames.MovedObjects + SettingNames.PathSeparator;
    public const string ReplacedObjectsGroup = ChangedObjectsGroup + SettingNames.ReplacedObjects + SettingNames.PathSeparator;
    public const string ChangedCollisionGroup = ChangedObjectsGroup + SettingNames.CollisionHandling + SettingNames.PathSeparator;
    public const string TrustGroup = SettingNames.Trust + SettingNames.PathSeparator;
    public const string AlsoRemoveGroup = SettingNames.AlsoRemove + SettingNames.PathSeparator;
    public const string InvisibleSurroundingObjectsGroup =
        SettingNames.InvisibleLeftBehind + SettingNames.PathSeparator + SettingNames.SurroundingObjects + SettingNames.PathSeparator;
    public const string VisibleSurroundingObjectsGroup =
        SettingNames.VisibleLeftBehind + SettingNames.PathSeparator + SettingNames.SurroundingObjects + SettingNames.PathSeparator;
    public const string RestoreWhereRemovedGroup = SettingNames.RestoreWhereRemoved + SettingNames.PathSeparator;
    public const string ClearedAreaGroup = RestoreWhereRemovedGroup + SettingNames.ClearedArea + SettingNames.PathSeparator;
}
