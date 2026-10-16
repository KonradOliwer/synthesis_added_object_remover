using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;
using AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;
using static AddedObjectRemover.SettingCorrections;

namespace AddedObjectRemover;

/// <summary>Validates the settings against the load order; an invalid value is replaced and reported as a warning, in the order found.</summary>
public static class SettingsValidator
{
    private const float MaxSizeMultiplier = 5f;
    private const float MaxMarginInUnits = 1024f;
    private const float MaxHeightChangeTolerance = 256f;
    private const float MaxTouchDistance = 64f;
    private const float MinSearchRadius = 64f;
    private const float MaxSearchRadius = 8192f;
    private const int MinOtherMastersForPatch = 1;
    private const int MaxOtherMastersForPatch = 100;
    private const float MinPercent = 1f;

    /// <summary>The names the settings screen shows, group first, so a warning leads to the setting.</summary>
    private const string TooCloseSizeMarginName = "Remove objects too close to other mods' objects > Margin by object size > Margin on every side";
    private const string TooCloseDistanceMarginName = "Remove objects too close to other mods' objects > Margin by fixed distance > Margin on every side";
    private const string TerrainSizeMarginName = "Remove objects on terrain changed by other mods > Margin by object size > Margin on every side";
    private const string TerrainDistanceMarginName = "Remove objects on terrain changed by other mods > Margin by fixed distance > Margin on every side";

    /// <summary>Validation runs in a fixed order because the warnings are reported in the order found.</summary>
    public static RunRequest Build(ISettingsValues settings, IPluginRecords records, LoadOrderPlugins mods, string outputPath, int workers)
    {
        var warnings = new List<SettingWarning>();
        if (FindTarget(records, mods, settings.TargetPlugin, out var targetAsTyped, out var targetListing) is { } stop)
        {
            return new RunRequest.Stop(stop, [.. warnings]);
        }

        var ignore = settings.WhatToIgnore;
        var excluded = ignore.ExcludedPlugins.Select(name => ParsePluginName(records, name)).ToList();
        WarnIfMastersUnreadable(targetListing, warnings);
        var modIdentification = CreateModIdentificationSettings(mods, targetListing, excluded, ignore, warnings);
        var tooClose = CreateTooCloseSettings(settings.TooClose, warnings);
        var changedTerrain = CreateChangedTerrainSettings(settings.TerrainChange, warnings);
        var detailedLog = settings.Diagnostics.DetailedLog;
        var writeReportFiles = settings.Diagnostics.WriteReportFiles;
        var alsoRemove = CreateAlsoRemoveSettings(settings.FollowUpRemoval, touchChainsShown: detailedLog || writeReportFiles, warnings);
        var leftoverSettings = settings.LeftoverInvisibleObjects;
        var leftBehind = CreateLeftBehindSettings(leftoverSettings, warnings);
        var markerMoves = CreateMarkerMoveSettings(leftoverSettings);
        var reports = new ReportFileSettings(writeReportFiles, ResolveReportFolder(outputPath, settings, warnings));

        var runSettings = new RunSettings(
            targetListing.Mod,
            targetAsTyped,
            [.. excluded.Select(name => name.ToString())],
            modIdentification,
            CreateWhatToCollect(alsoRemove.Mode, markerMoves, changedTerrain, detailedLog),
            new SelectionSettings(tooClose.Npcs),
            changedTerrain,
            tooClose,
            alsoRemove,
            leftoverSettings.RemoveLeftoverInvisibleObjects ? leftBehind : null,
            markerMoves,
            reports,
            detailedLog,
            new Execution(workers));
        return new RunRequest.Ready(runSettings,[.. warnings]);
    }

    private static ModIdentificationSettings CreateModIdentificationSettings(
        LoadOrderPlugins mods, PluginListing targetListing, IReadOnlyList<PluginName> excluded, IIgnoreSettingsValues ignore, ICollection<SettingWarning> warnings) =>
        new(
            targetListing.Mod,
            [.. excluded.Where(mods.Mods.Knows).Select(mods.Mods.WithKnownSpelling)],
            ignore.IgnoreTargetMasters,
            ignore.IgnoreModsPatchedWithTarget,
            ignore.IgnoreModsPatchedWithTarget
                ? Clamp(ignore.MaxOtherMastersForPatch, MinOtherMastersForPatch, MaxOtherMastersForPatch, "maximum other masters for a patch", warnings)
                : ignore.MaxOtherMastersForPatch);

    /// <summary>With neither margin on, the step switches itself off and removes nothing, so a warning says so.</summary>
    private static TooCloseSettings CreateTooCloseSettings(ITooCloseSettingsValues values, ICollection<SettingWarning> warnings)
    {
        var npcs = ValidateDefined(values.Npcs, SettingDefaults.Npcs, "NPCs and creatures setting", warnings);
        var sizeMultiplier = ClampSizeMultiplier(values.SizeMultiplier, TooCloseSizeMarginName, warnings);
        var distanceInUnits = ClampMarginInUnits(values.DistanceInUnits, SettingDefaults.RemovalDistanceInUnits, TooCloseDistanceMarginName, warnings);
        var zoneShape = values.UseRoughBoxes ? ZoneShape.BoundingBox : ZoneShape.ObjectShape;
        if (!values.BySize && !values.ByDistance)
        {
            warnings.Add(new SettingWarning(
                "too close",
                "Warning: neither too-close margin is on; no object is removed for being too close."));
        }
        return new TooCloseSettings(values.BySize, sizeMultiplier, values.ByDistance, distanceInUnits, zoneShape, npcs);
    }

    private static float ClampSizeMultiplier(float multiplier, string name, ICollection<SettingWarning> warnings) =>
        Clamp(multiplier, 0, MaxSizeMultiplier, SettingDefaults.SizeMultiplier, name, warnings);

    private static float ClampMarginInUnits(float units, float defaultUnits, string name, ICollection<SettingWarning> warnings) =>
        Clamp(units, 0, MaxMarginInUnits, defaultUnits, name, warnings);

    /// <summary>
    /// Built (and its settings validated) even when the step is off, so invalid values are always reported. Switched on with
    /// neither height nor terrain type changes counted, or with neither terrain area on, it is passed as off, with a warning,
    /// because it could remove nothing.
    /// </summary>
    private static ChangedTerrainSettings CreateChangedTerrainSettings(ITerrainChangeSettingsValues terrain, ICollection<SettingWarning> warnings)
    {
        var objects = ValidateDefined(terrain.TerrainCheckedObjects, SettingDefaults.TerrainChecked, "objects checked for changed terrain", warnings);
        var tolerance = Clamp(
            terrain.HeightChangeTolerance, 0, MaxHeightChangeTolerance, SettingDefaults.HeightChangeTolerance, "height change tolerance", warnings);
        var sizeMultiplier = ClampSizeMultiplier(terrain.AreaSizeMultiplier, TerrainSizeMarginName, warnings);
        var distanceInUnits = ClampMarginInUnits(terrain.AreaDistanceInUnits, SettingDefaults.TerrainAreaDistanceInUnits, TerrainDistanceMarginName, warnings);
        var countsAChange = terrain.CountHeightChanges || terrain.CountTerrainTypeChanges;
        var hasAnArea = terrain.AreaBySize || terrain.AreaByDistance;
        if (terrain.RemoveOnChangedTerrain && !countsAChange)
        {
            warnings.Add(new SettingWarning(
                "changed terrain",
                "Warning: changed terrain is on, but neither height nor terrain type changes are counted; no object is removed for changed terrain."));
        }
        if (terrain.RemoveOnChangedTerrain && !hasAnArea)
        {
            warnings.Add(new SettingWarning(
                "changed terrain",
                "Warning: changed terrain is on, but neither terrain margin is on; no object is removed for changed terrain."));
        }
        return new ChangedTerrainSettings(
            terrain.RemoveOnChangedTerrain && countsAChange && hasAnArea,
            objects,
            terrain.CountHeightChanges,
            tolerance,
            terrain.CountTerrainTypeChanges,
            terrain.AreaBySize,
            sizeMultiplier,
            terrain.AreaByDistance,
            distanceInUnits);
    }

    /// <param name="touchChainsShown">Whether the Detailed log or the report files show the touch chains.</param>
    private static AlsoRemoveSettings CreateAlsoRemoveSettings(
        IFollowUpSettingsValues followUp, bool touchChainsShown, ICollection<SettingWarning> warnings)
    {
        var mode = ValidateDefined(followUp.Mode, SettingDefaults.FollowUpMode, "follow-up removal mode", warnings);
        var touchDistance = Clamp(followUp.TouchDistance, 0, MaxTouchDistance, SettingDefaults.TouchDistance, "touch distance", warnings);
        var supportPercent = Clamp(
            followUp.AnchoringThresholdPercent, MinPercent, Percent.PerWhole,
            SettingDefaults.AnchoringThresholdPercent, "anchoring threshold", warnings);
        return new AlsoRemoveSettings(mode, touchDistance, supportPercent /Percent.PerWhole, touchChainsShown);
    }

    private static MarkerMoveSettings? CreateMarkerMoveSettings(ILeftoverSettingsValues leftovers) =>
        leftovers.RemoveLeftoverInvisibleObjects && leftovers.MoveKeptMarkersOutOfOtherModsObjects ? new MarkerMoveSettings(MarkerMoveLimits.MaxDistance) : null;

    /// <summary>A rooted path is used as is; a relative one is resolved against the output plugin's folder.</summary>
    internal static string ResolveReportFolder(string outputPath, ISettingsValues settings, ICollection<SettingWarning> warnings)
    {
        var folder = ValidateFolder(settings.Diagnostics.ReportFolder?.Trim(), warnings);
        return FolderPaths.ResolveAgainst(Path.GetDirectoryName(outputPath) ?? string.Empty, folder);
    }

    /// <summary>
    /// The surroundings are read only when follow-up removal or relocation needs them; the Editor ID lists only when the detailed
    /// log prints them; every plugin's terrain only when the changed-terrain step runs, and its texture layers only when it counts terrain types.
    /// </summary>
    private static WhatToCollect CreateWhatToCollect(FollowUpRemovalMode mode, MarkerMoveSettings? markerMoves, ChangedTerrainSettings changedTerrain, bool detailedLog)
    {
        var needsSurroundings = mode == FollowUpRemovalMode.OnlyWhatLosesItsSupport || markerMoves != null;
        return new WhatToCollect(
            needsSurroundings,
            needsSurroundings,
            markerMoves != null,
            detailedLog,
            detailedLog,
            changedTerrain.On,
            changedTerrain.On && changedTerrain.CountTerrainTypeChanges);
    }

    /// <summary>Built (and its settings validated) even when the step is off, so invalid values are always reported.</summary>
    private static LeftBehindSettings CreateLeftBehindSettings(ILeftoverSettingsValues leftovers, ICollection<SettingWarning> warnings)
    {
        return new LeftBehindSettings(
            LookAround: Clamp(leftovers.SearchRadius, MinSearchRadius, MaxSearchRadius, SettingDefaults.SearchRadius, "search radius", warnings),
            DirectionClearedPercent: WholeTens(leftovers.DirectionThresholdPercent, "removed area per direction", warnings),
            ClearedDirectionsPercent: WholeTens(leftovers.RemovedDirectionsPercent, "removed directions required", warnings),
            OccupiedDirectionsPercent: WholeTens(leftovers.OccupiedDirectionsPercent, "occupied directions required", warnings),
            NeverRemove: leftovers.NeverRemove.ToHashSet());
    }

    /// <summary>A folder containing characters no path can contain (for example a NUL character) falls back to the default.</summary>
    private static string ValidateFolder(string? configured, ICollection<SettingWarning> warnings)
    {
        if (string.IsNullOrEmpty(configured)) return SettingDefaults.ReportFolder;
        if (configured.IndexOfAny(Path.GetInvalidPathChars()) < 0) return configured;
        warnings.Add(new SettingWarning(
            "report folder",
            $"Warning: report folder '{configured}' contains characters not allowed in a path; using '{SettingDefaults.ReportFolder}'."));
        return SettingDefaults.ReportFolder;
    }

    private static StopReason? FindTarget(IPluginRecords records, LoadOrderPlugins mods, string? targetPlugin, out PluginName target, out PluginListing targetListing)
    {
        target = default;
        targetListing = null!;
        if (targetPlugin == null) return new StopReason(StopKind.NoTargetSet, null);
        target = ParsePluginName(records, targetPlugin);
        var listing = mods.Find(target);
        if (listing == null) return new StopReason(StopKind.TargetNotInLoadOrder, target.ToString());
        targetListing = listing;
        return null;
    }

    /// <summary>Plugin settings are load-order picker values, which the settings reader only creates from valid plugin file names.</summary>
    private static PluginName ParsePluginName(IPluginRecords records, string name) =>
        records.TryParsePluginName(name, out var parsed) ? parsed : throw new InvalidOperationException($"The plugin setting {name} is not a plugin file name.");

    private static void WarnIfMastersUnreadable(PluginListing targetListing, ICollection<SettingWarning> warnings)
    {
        if (targetListing.Loaded) return;
        warnings.Add(new SettingWarning(
            "target plugin",
            $"Warning: could not read {targetListing.Mod} to find its masters."));
    }
}
