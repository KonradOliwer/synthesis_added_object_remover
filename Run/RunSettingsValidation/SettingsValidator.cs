using System.Collections.Immutable;
using System.Diagnostics;
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
    private const string TerrainTypeChangePercentName = "Remove objects on terrain changed by other mods > Remove only when this share of the ground points changed type";
    private const string WaterCoverChangePercentName = "Remove objects on terrain changed by other mods > Remove only when this share of the ground got covered/uncovered";

    /// <summary>Validation runs in a fixed order because the warnings are reported in the order found.</summary>
    /// <param name="detailedLogAndReports">The one setting for every run that turns on both the detailed log and the report files.</param>
    /// <param name="reportsFolder">Required only when <paramref name="detailedLogAndReports"/> is on.</param>
    public static RunRequest Build(
        ConfiguredSettings config, IPluginRecords records, LoadOrderPlugins mods, bool detailedLogAndReports, string? reportsFolder, int workers)
    {
        var settings = config.Settings;
        var warnings = new List<SettingWarning>();
        if (FindTarget(records, mods, settings.CleanedMod, out var targetAsTyped, out var targetListing) is { } stop)
        {
            return new RunRequest.Stop(stop, config.Source, [.. warnings]);
        }

        var ignore = settings.WhatToIgnore;
        var excluded = ignore.ExcludedPlugins.Select(name => ParsePluginName(records, name)).ToList();
        WarnIfMastersUnreadable(targetListing, warnings);
        var modIdentification = CreateModIdentificationSettings(mods, targetListing, excluded, ignore, warnings);
        var tooClose = CreateTooCloseSettings(settings.TooClose, warnings);
        var changedTerrain = CreateChangedTerrainSettings(settings.TerrainChange, warnings);
        var alsoRemove = CreateAlsoRemoveSettings(settings.FollowUpRemoval, touchChainsShown: detailedLogAndReports, warnings);
        var leftoverSettings = settings.LeftoverInvisibleObjects;
        var leftBehind = CreateLeftBehindSettings(leftoverSettings, warnings);
        var markerMoves = CreateMarkerMoveSettings(leftoverSettings);
        var reports = detailedLogAndReports ? CreateReportFileSettings(reportsFolder, config.Source) : null;

        var runSettings = new RunSettings(
            targetListing.Mod,
            targetAsTyped,
            [.. excluded.Select(name => name.ToString())],
            modIdentification,
            CreateWhatToCollect(alsoRemove.Mode, markerMoves, changedTerrain, detailedLogAndReports),
            new SelectionSettings(tooClose.Npcs),
            changedTerrain,
            tooClose,
            alsoRemove,
            leftoverSettings.RemoveLeftoverInvisibleObjects ? leftBehind : null,
            markerMoves,
            reports,
            new Execution(workers));
        return new RunRequest.Ready(runSettings, config.Source, [.. warnings]);
    }

    /// <summary>
    /// Builds every config's request in order. A config file that sets no target, a target that is not in the plugin list, is disabled or loads after the patch, or is the patch
    /// plugin, or two configs with the same target, fail the whole batch; a Standard request that stops stays a stop request.
    /// </summary>
    public static BatchRequest BuildAll(
        ConfigsRead read, IPluginRecords records, LoadOrderPlugins mods, bool detailedLogAndReports, string? reportsFolder, int workers) =>
        read switch
        {
            ConfigsRead.Failed failed => new BatchRequest.Failed(failed.Message),
            ConfigsRead.Ready ready => BuildEach(ready.Configs, records, mods, detailedLogAndReports, reportsFolder, workers),
            _ => throw new UnreachableException(),
        };

    /// <summary>The plugin list is read before any settings are checked, so its problem stops every run of the batch.</summary>
    public static BatchRequest.Failed StopOnUnusablePluginList(PluginListProblem problem) =>
        new(
            (problem switch
            {
                PluginListProblem.PathUnknown => "Could not find the game's plugin list file (Plugins.txt in the game's folder under your local application data).",
                PluginListProblem.FileMissing missing => $"The plugin list file {missing.Path} does not exist.",
                PluginListProblem.MalformedLine malformed => $"The plugin list file {malformed.Path} has a malformed entry at line {malformed.Number}. Correct or remove that line.",
                PluginListProblem.PluginNotListed unlisted => $"{unlisted.Plugin} is loaded by Synthesis but is not in the plugin list file {unlisted.Path}.",
                _ => throw new UnreachableException(),
            })
            + ConfigsRead.NothingWasRun);

    private static BatchRequest BuildEach(
        ImmutableArray<ConfiguredSettings> configs, IPluginRecords records, LoadOrderPlugins mods, bool detailedLogAndReports, string? reportsFolder, int workers)
    {
        var requests = ImmutableArray.CreateBuilder<RunRequest>(configs.Length);
        foreach (var config in configs)
        {
            var request = Build(config, records, mods, detailedLogAndReports, reportsFolder, workers);
            if (DescribeConfigFileStop(request) is { } stopped) return new BatchRequest.Failed(stopped);
            requests.Add(request);
        }

        return FindSharedTarget(requests) is { } shared ? new BatchRequest.Failed(shared) : new BatchRequest.Ready(requests.MoveToImmutable());
    }

    private static string? DescribeConfigFileStop(RunRequest request)
    {
        if (request is not RunRequest.Stop { Source: SettingsSource.ConfigFile file } stop) return null;
        return stop.Reason.Kind switch
        {
            StopKind.NoTargetSet => $"Config file {file.FilePath} sets no mod to clean up (CleanedMod).{ConfigsRead.NothingWasRun}",
            StopKind.CleanedModNotInLoadOrder => $"Config file {file.FilePath}: the mod to clean up, {stop.Reason.Name}, is not in your plugin list.{ConfigsRead.NothingWasRun}",
            StopKind.CleanedModDisabled => $"Config file {file.FilePath}: the mod to clean up, {stop.Reason.Name}, is disabled.{ConfigsRead.NothingWasRun}",
            StopKind.CleanedModNotLoadedBySynthesis => $"Config file {file.FilePath}: the mod to clean up, {stop.Reason.Name}, is enabled in your plugin list but Synthesis did not load it for this group.{ConfigsRead.NothingWasRun}",
            StopKind.CleanedModLoadsAfterPatch when stop.Reason.Placement is { } placement =>
                $"Config file {file.FilePath}: the mod to clean up, {stop.Reason.Name}, loads after the patch {placement.Patch} (position {placement.CleanedModPosition} > {placement.PatchPosition}): move {placement.Patch} below it.{ConfigsRead.NothingWasRun}",
            StopKind.TargetIsThePatch => $"Config file {file.FilePath}: the mod to clean up, {stop.Reason.Name}, is the patch this patcher writes.{ConfigsRead.NothingWasRun}",
            _ => throw new UnreachableException(),
        };
    }

    private static string? FindSharedTarget(IEnumerable<RunRequest> requests)
    {
        var filePathByTarget = new Dictionary<PluginName, string>();
        foreach (var ready in requests.OfType<RunRequest.Ready>())
        {
            if (ready.Source is not SettingsSource.ConfigFile file) continue;
            if (!filePathByTarget.TryAdd(ready.Settings.Target, file.FilePath))
            {
                return $"Config files {filePathByTarget[ready.Settings.Target]} and {file.FilePath} both clean up {ready.Settings.Target}; each mod may be cleaned up by one config only.{ConfigsRead.NothingWasRun}";
            }
        }

        return null;
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
    /// no height, terrain type or water changes counted, or with neither terrain area on, it is passed as off, with a warning,
    /// because it could remove nothing.
    /// </summary>
    private static ChangedTerrainSettings CreateChangedTerrainSettings(ITerrainChangeSettingsValues terrain, ICollection<SettingWarning> warnings)
    {
        var objects = ValidateDefined(terrain.TerrainCheckedObjects, SettingDefaults.TerrainChecked, "objects checked for changed terrain", warnings);
        var tolerance = Clamp(
            terrain.HeightChangeTolerance, 0, MaxHeightChangeTolerance, SettingDefaults.HeightChangeTolerance, "height change tolerance", warnings);
        var sizeMultiplier = ClampSizeMultiplier(terrain.AreaSizeMultiplier, TerrainSizeMarginName, warnings);
        var distanceInUnits = ClampMarginInUnits(terrain.AreaDistanceInUnits, SettingDefaults.TerrainAreaDistanceInUnits, TerrainDistanceMarginName, warnings);
        var typeChangePercent = Clamp(terrain.MinTerrainTypeChangePercent, 0, Percent.PerWhole, TerrainTypeChangePercentName, warnings);
        var waterTolerance = Clamp(
            terrain.WaterHeightChangeTolerance, 0, MaxHeightChangeTolerance, SettingDefaults.WaterHeightChangeTolerance, "water height change tolerance", warnings);
        var waterCoverPercent = Clamp(terrain.MinWaterCoverChangePercent, 0, Percent.PerWhole, WaterCoverChangePercentName, warnings);
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
        var countsAChange = settings.CountHeightChanges || settings.CountTerrainTypeChanges || settings.CountsWaterChanges;
        var hasAnArea = settings.AreaBySize || settings.AreaByDistance;
        if (settings.On && !countsAChange)
        {
            warnings.Add(new SettingWarning(
                "changed terrain",
                "Warning: changed terrain is on, but no height, terrain type or water changes are counted; no object is removed for changed terrain."));
        }
        if (settings.On && !hasAnArea)
        {
            warnings.Add(new SettingWarning(
                "changed terrain",
                "Warning: changed terrain is on, but neither terrain margin is on; no object is removed for changed terrain."));
        }
        return settings with { On = settings.On && countsAChange && hasAnArea };
    }

    /// <param name="touchChainsShown">Whether the detailed log and the report files, which show the touch chains, are on.</param>
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

    /// <summary>A config file's reports go to a subfolder named after the config, so the configs of a batch do not overwrite each other's.</summary>
    private static ReportFileSettings CreateReportFileSettings(string? reportsFolder, SettingsSource source) =>
        new(ResolveReportFolder(reportsFolder ?? throw new ArgumentNullException(nameof(reportsFolder)), source));

    internal static string ResolveReportFolder(string reportsFolder, SettingsSource source) =>
        source is SettingsSource.ConfigFile file ? Path.Combine(reportsFolder, file.Name) : reportsFolder;

    /// <summary>
    /// The surroundings are read only when follow-up removal or relocation needs them; the Editor ID lists only when the detailed
    /// log prints them; every plugin's terrain only when the changed-terrain step runs, its texture layers only when it counts terrain
    /// types, and its water only when it counts a water change.
    /// </summary>
    private static WhatToCollect CreateWhatToCollect(FollowUpRemovalMode mode, MarkerMoveSettings? markerMoves, ChangedTerrainSettings changedTerrain, bool detailedLog)
    {
        var needsSurroundings = mode == FollowUpRemovalMode.OnlyWhatLosesItsSupport || markerMoves != null;
        return new WhatToCollect(
            needsSurroundings,
            needsSurroundings,
            markerMoves != null,
            detailedLog,
            changedTerrain.On,
            changedTerrain.On && changedTerrain.CountTerrainTypeChanges,
            changedTerrain.On && changedTerrain.CountsWaterChanges);
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

    private static StopReason? FindTarget(IPluginRecords records, LoadOrderPlugins mods, string? targetPlugin, out PluginName target, out PluginListing targetListing)
    {
        target = default;
        targetListing = null!;
        if (targetPlugin == null) return new StopReason(StopKind.NoTargetSet, null);
        target = ParsePluginName(records, targetPlugin);
        if (target == mods.PatchMod) return new StopReason(StopKind.TargetIsThePatch, target.ToString());
        if (FindUnusablePlacement(mods, target) is { } unusable) return unusable;
        var listing = mods.Find(target);
        if (listing == null) return new StopReason(StopKind.CleanedModNotLoadedBySynthesis, target.ToString());
        targetListing = listing;
        return null;
    }

    /// <summary>
    /// Synthesis loads only the enabled plugins above the patch, so the plugin list alone tells why the cleaned mod is missing:
    /// not listed, disabled, or below the patch. The positions are the plugin list's, counted from 0.
    /// </summary>
    private static StopReason? FindUnusablePlacement(LoadOrderPlugins mods, PluginName target)
    {
        var name = target.ToString();
        if (!mods.Positions.Contains(target)) return new StopReason(StopKind.CleanedModNotInLoadOrder, name);
        if (mods.DisabledPlugins.Contains(target)) return new StopReason(StopKind.CleanedModDisabled, name);
        var position = mods.Positions.Of(target);
        return position > mods.PatchPosition
            ? new StopReason(StopKind.CleanedModLoadsAfterPatch, name, new PatchPlacement(mods.PatchMod.ToString(), position, mods.PatchPosition))
            : null;
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
