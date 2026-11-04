using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.FitClashingObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;
using AddedObjectRemover.Rules.TooCloseToOtherModsObjects.Contracts;
using AddedObjectRemover.Rules.ObjectsThatCanCauseRemovals.Contracts;
using static AddedObjectRemover.SettingCorrections;

namespace AddedObjectRemover;

/// <summary>Builds the collision settings of a set of objects: the too-close, changed-terrain and shrink-or-turn groups.</summary>
internal static class CollisionSettingsBuilder
{
    internal const float MaxSizeMultiplier = 5f;
    internal const float MaxMarginInUnits = 1024f;
    internal const float MaxHeightChangeTolerance = 256f;
    internal const int MinShrinkLimitPercent = 1;

    private const string NoClashFoundAtEitherPlace = ", neither where the cleaned mod put it nor at its old place";

    private static readonly CollisionGroupTexts CleanedModCollisionTexts = new(
        SettingPaths.StandardGroupPath,
        SettingPaths.ShrinkOrTurnGroup,
        "no object is removed for being too close",
        "no object is removed for changed terrain");

    private static readonly CollisionGroupTexts ChangedObjectsCollisionTexts = new(
        SettingPaths.ChangedCollisionGroup,
        SettingPaths.ChangedCollisionGroup,
        $"no changed object is found too close{NoClashFoundAtEitherPlace}",
        $"no changed object is found on changed terrain{NoClashFoundAtEitherPlace}");

    /// <param name="GroupPath">The names, ending in " > ", of the groups that hold the too-close and changed-terrain groups; empty at the top of the settings.</param>
    /// <param name="FitGroupPath">The names, ending in " > ", of the groups that hold the two limits for shrinking and turning.</param>
    /// <param name="TooCloseEffect">What a warning says no longer happens when neither too-close margin is on.</param>
    /// <param name="ChangedTerrainEffect">What a warning says no longer happens when the changed-terrain check can find nothing.</param>
    private sealed record CollisionGroupTexts(string GroupPath, string FitGroupPath, string TooCloseEffect, string ChangedTerrainEffect);

    public static CollisionSettings CreateForCleanedMod(ICollisionSettingsValues values, ICollection<SettingWarning> warnings) =>
        CreateCollisionSettings(values, CleanedModCollisionTexts, warnings);

    public static CollisionSettings CreateForChangedObjects(ICollisionSettingsValues values, ICollection<SettingWarning> warnings) =>
        CreateCollisionSettings(values, ChangedObjectsCollisionTexts, warnings);

    private static CollisionSettings CreateCollisionSettings(
        ICollisionSettingsValues values, CollisionGroupTexts texts, ICollection<SettingWarning> warnings) =>
        new(
            CreateTooCloseSettings(values.TooClose, texts, warnings),
            CreateChangedTerrainSettings(values.TerrainChange, texts, warnings),
            CreateFitSettings(values.ShrinkOrTurn, texts.FitGroupPath, warnings));

    /// <summary>With neither margin on, the step switches itself off and finds nothing, so a warning says so.</summary>
    private static TooCloseSettings CreateTooCloseSettings(ITooCloseSettingsValues values, CollisionGroupTexts texts, ICollection<SettingWarning> warnings)
    {
        var groupPath = texts.GroupPath;
        var group = groupPath + SettingPaths.TooCloseGroup;
        var npcs = ValidateDefined(values.Npcs, SettingDefaults.Npcs, group + SettingNames.Npcs, warnings);
        var sizeMultiplier = ClampSizeMultiplier(values.SizeMultiplier, group + SettingPaths.SizeMargin, warnings);
        var distanceInUnits = ClampMarginInUnits(values.DistanceInUnits, SettingDefaults.RemovalDistanceInUnits, group + SettingPaths.DistanceMargin, warnings);
        var zoneShape = values.UseRoughBoxes ? ZoneShape.BoundingBox : ZoneShape.ObjectShape;
        if (!values.BySize && !values.ByDistance)
        {
            warnings.Add(new SettingWarning(
                groupPath + "too close",
                $"Warning: {groupPath}neither too-close margin is on; {texts.TooCloseEffect}."));
        }
        return new TooCloseSettings(values.BySize, sizeMultiplier, values.ByDistance, distanceInUnits, zoneShape, npcs);
    }

    private static float ClampSizeMultiplier(float multiplier, string name, ICollection<SettingWarning> warnings) =>
        Clamp(multiplier, 0, MaxSizeMultiplier, SettingDefaults.SizeMultiplier, name, warnings);

    private static float ClampMarginInUnits(float units, float defaultUnits, string name, ICollection<SettingWarning> warnings) =>
        Clamp(units, 0, MaxMarginInUnits, defaultUnits, name, warnings);

    /// <summary>
    /// Built (and its settings validated) even when the step is off, so invalid values are always reported. Switched on with
    /// no height, ground type or water changes counted, or with neither terrain area on, the step does not run, and a warning says so.
    /// </summary>
    private static ChangedTerrainSettings CreateChangedTerrainSettings(
        ITerrainChangeSettingsValues terrain, CollisionGroupTexts texts, ICollection<SettingWarning> warnings)
    {
        var groupPath = texts.GroupPath;
        var group = groupPath + SettingPaths.TerrainGroup;
        var objects = ValidateDefined(terrain.TerrainCheckedObjects, SettingDefaults.TerrainChecked, group + SettingNames.TerrainCheckedObjects, warnings);
        var tolerance = Clamp(
            terrain.HeightChangeTolerance, 0, MaxHeightChangeTolerance, SettingDefaults.HeightChangeTolerance, group + SettingNames.HeightChangeTolerance, warnings);
        var sizeMultiplier = ClampSizeMultiplier(terrain.AreaSizeMultiplier, group + SettingPaths.SizeMargin, warnings);
        var distanceInUnits = ClampMarginInUnits(terrain.AreaDistanceInUnits, SettingDefaults.TerrainAreaDistanceInUnits, group + SettingPaths.DistanceMargin, warnings);
        var typeChangePercent = Clamp(terrain.MinTerrainTypeChangePercent, 0, Percent.PerWhole, group + SettingNames.TerrainTypeChangePercent, warnings);
        var waterTolerance = Clamp(
            terrain.WaterHeightChangeTolerance, 0, MaxHeightChangeTolerance, SettingDefaults.WaterHeightChangeTolerance, group + SettingNames.WaterHeightChangeTolerance, warnings);
        var waterCoverPercent = Clamp(terrain.MinWaterCoverChangePercent, 0, Percent.PerWhole, group + SettingNames.WaterCoverChangePercent, warnings);
        var settings = new ChangedTerrainSettings(
            terrain.RemoveOnChangedTerrain,
            objects,
            terrain.CountHeightChanges,
            tolerance,
            terrain.CountTerrainTypeChanges,
            typeChangePercent,
            terrain.CountWaterHeightChanges,
            waterTolerance,
            terrain.CountWaterAddedOrRemoved,
            waterCoverPercent,
            terrain.AreaBySize,
            sizeMultiplier,
            terrain.AreaByDistance,
            distanceInUnits);
        if (settings.On && !settings.CountsAChange)
        {
            warnings.Add(new SettingWarning(
                groupPath + "changed terrain",
                $"Warning: {groupPath}changed terrain is on, but no height, ground type or water changes are counted; {texts.ChangedTerrainEffect}."));
        }
        if (settings.On && !settings.HasAnArea)
        {
            warnings.Add(new SettingWarning(
                groupPath + "changed terrain",
                $"Warning: {groupPath}changed terrain is on, but neither terrain margin is on; {texts.ChangedTerrainEffect}."));
        }
        return settings;
    }

    /// <summary>A shrink limit of 100 or more and a turn limit of 180 or more are valid: they mean never shrunk and any heading.</summary>
    private static FitSettings CreateFitSettings(IShrinkOrTurnSettingsValues values, string group, ICollection<SettingWarning> warnings) =>
        new(
            AtLeast(values.ShrinkLimitPercent, MinShrinkLimitPercent, group + SettingNames.ShrinkLimit, warnings),
            AtLeast(values.TurnLimitDegrees, 0, group + SettingNames.TurnLimit, warnings));
}
