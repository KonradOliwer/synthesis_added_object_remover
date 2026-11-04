using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsInClearedAreas.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;
using static AddedObjectRemover.SettingCorrections;

namespace AddedObjectRemover;

/// <summary>Builds the settings of the restore of what the cleaned mod changed where it was removed.</summary>
internal static class RestoreSettingsBuilder
{
    internal const float MaxEdgeBlendDistance = 1024f;

    private const string EdgeBlendDistanceName = SettingPaths.RestoreWhereRemovedGroup + SettingNames.EdgeBlendDistance;

    private static readonly ClearedAreaSettingsReader.Names ClearedAreaNames = new(
        SettingPaths.ClearedAreaGroup + SettingNames.ClearedAreaRadius,
        SettingPaths.ClearedAreaGroup + SettingNames.MaxStandingSizePercent,
        SettingPaths.ClearedAreaGroup + SettingNames.MaxStandingCountPercent);

    /// <summary>Built (and its settings validated) even when the group is off, so invalid values are always reported.</summary>
    /// <param name="cleanedModCollision">The collision settings of the cleaned mod's own objects, whose terrain tolerances the restore reuses.</param>
    /// <param name="changedCollision">The collision settings of the objects the cleaned mod changed, which judge a restored object against what stays.</param>
    public static WhereRemovedRestoreSettings Create(
        IRestoreWhereRemovedSettingsValues values,
        CollisionSettings cleanedModCollision,
        ChangedObjectCollision changedCollision,
        ICollection<SettingWarning> warnings)
    {
        var area = ClearedAreaSettingsReader.Read(
            values.ClearedArea, ClearedAreaNames, (percent, defaultValue, name) => Clamp(percent, 0, Percent.PerWhole, defaultValue, name, warnings), warnings);
        var edgeBlendDistance = Clamp(
            values.TerrainEdgeBlendDistanceInUnits, 0, MaxEdgeBlendDistance, SettingDefaults.TerrainEdgeBlendDistanceInUnits, EdgeBlendDistanceName, warnings);
        return new WhereRemovedRestoreSettings(
            values.RestoreOtherModsObjects,
            values.RestoreTerrain,
            values.RestoreCells,
            values.RestoreNavmeshes,
            area.ToClearedArea(),
            edgeBlendDistance,
            cleanedModCollision.ChangedTerrain.HeightTolerance,
            cleanedModCollision.ChangedTerrain.WaterHeightTolerance,
            changedCollision.TooClose);
    }
}
