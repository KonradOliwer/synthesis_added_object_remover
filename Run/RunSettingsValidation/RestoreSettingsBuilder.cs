using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;
using static AddedObjectRemover.SettingCorrections;

namespace AddedObjectRemover;

/// <summary>Builds the settings of the restore of what the cleaned mod changed where it was removed.</summary>
internal static class RestoreSettingsBuilder
{
    internal const float MinClearedAreaRadius = 1024f;
    internal const float MaxClearedAreaRadius = 16384f;
    internal const float MaxEdgeBlendDistance = 1024f;

    private const string ClearedAreaRadiusName = SettingPaths.ClearedAreaGroup + SettingNames.ClearedAreaRadius;
    private const string MaxStandingSizePercentName = SettingPaths.ClearedAreaGroup + SettingNames.MaxStandingSizePercent;
    private const string MaxStandingCountPercentName = SettingPaths.ClearedAreaGroup + SettingNames.MaxStandingCountPercent;
    private const string SharesThatMustPassName = SettingPaths.ClearedAreaGroup + SettingNames.SharesThatMustPass;
    private const string EdgeBlendDistanceName = SettingPaths.RestoreWhereRemovedGroup + SettingNames.EdgeBlendDistance;

    /// <summary>
    /// Built (and its settings validated) even when the group is off, so invalid values are always reported.
    /// The percents are the decimals the user typed: the float setting 0.7 would otherwise widen to 0.69999998.
    /// </summary>
    /// <param name="cleanedModCollision">The collision settings of the cleaned mod's own objects, whose terrain tolerances the restore reuses.</param>
    /// <param name="changedCollision">The collision settings of the objects the cleaned mod changed, which judge a restored object against what stays.</param>
    public static WhereRemovedRestoreSettings Create(
        IRestoreWhereRemovedSettingsValues values,
        CollisionSettings cleanedModCollision,
        ChangedObjectCollision changedCollision,
        ICollection<SettingWarning> warnings)
    {
        var area = values.ClearedArea;
        var searchRadius = Clamp(
            area.SearchRadius, MinClearedAreaRadius, MaxClearedAreaRadius, SettingDefaults.ClearedAreaSearchRadius, ClearedAreaRadiusName, warnings);
        var maxStandingSizePercent = Clamp(
            area.MaxStandingSizePercent, 0, Percent.PerWhole, SettingDefaults.ClearedAreaMaxStandingSizePercent, MaxStandingSizePercentName, warnings);
        var maxStandingCountPercent = Clamp(
            area.MaxStandingCountPercent, 0, Percent.PerWhole, SettingDefaults.ClearedAreaMaxStandingCountPercent, MaxStandingCountPercentName, warnings);
        var sharesThatMustPass = ValidateDefined(area.SharesThatMustPass, SettingDefaults.SharesThatMustPass, SharesThatMustPassName, warnings);
        var edgeBlendDistance = Clamp(
            values.TerrainEdgeBlendDistanceInUnits, 0, MaxEdgeBlendDistance, SettingDefaults.TerrainEdgeBlendDistanceInUnits, EdgeBlendDistanceName, warnings);
        return new WhereRemovedRestoreSettings(
            values.RestoreOtherModsObjects,
            values.RestoreTerrain,
            values.RestoreCells,
            values.RestoreNavmeshes,
            new ClearedArea(searchRadius, (double)(decimal)maxStandingSizePercent, (double)(decimal)maxStandingCountPercent, sharesThatMustPass),
            edgeBlendDistance,
            cleanedModCollision.ChangedTerrain.HeightTolerance,
            cleanedModCollision.ChangedTerrain.WaterHeightTolerance,
            changedCollision.TooClose,
            changedCollision.ChangedTerrain);
    }
}
