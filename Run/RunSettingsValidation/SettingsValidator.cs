using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FitClashingObjects.Contracts;
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
    private const int MaxOtherMastersLimit = 100;
    private const float MinPercent = 1f;
    private const int MinShrinkLimitPercent = 1;

    /// <summary>The names the settings screen shows, group first, so a warning leads to the setting.</summary>
    private const string CleanedModName = "Mod to clean up";
    private const string NpcsName = "Remove objects too close to other mods' objects > Other mods' NPCs and creatures cause removal";
    private const string TerrainCheckedObjectsName = "Remove objects on terrain changed by other mods > Which of the cleaned mod's objects are checked";
    private const string HeightChangeToleranceName = "Remove objects on terrain changed by other mods > Count only height changes larger than";
    private const string WaterHeightChangeToleranceName = "Remove objects on terrain changed by other mods > Count only water height changes larger than";
    private const string ShrinkOrTurnGroup = "Shrink or turn objects to make them fit > ";
    private const string MovedDistanceName = ShrinkOrTurnGroup + "Distance that counts as moved";
    private const string ShrinkLimitName = ShrinkOrTurnGroup + "Shrink down to";
    private const string TurnLimitName = ShrinkOrTurnGroup + "Turn by at most";
    private const string WhichObjectsName = ShrinkOrTurnGroup + "Which objects may be shrunk or turned";
    private const string FollowUpModeName = "Also remove what rests on removed objects > When an object is removed, also remove";
    private const string TouchDistanceName = "Also remove what rests on removed objects > Gap that still counts as touching";
    private const string AnchoringThresholdName = "Also remove what rests on removed objects > OnlyWhatLosesItsSupport: remove when this much support is gone";
    private const string SurroundingObjectsGroup = "Invisible objects left behind > When the cleaned mod's visible objects around it were removed > ";
    private const string SearchRadiusName = SurroundingObjectsGroup + "Look this far around it";
    private const string DirectionThresholdName = SurroundingObjectsGroup + "A direction is cleared when this much of it was removed";
    private const string RemovedDirectionsName = SurroundingObjectsGroup + "Remove when this share of those directions is cleared";
    private const string OccupiedDirectionsName = SurroundingObjectsGroup + "Decide only if this share of the 8 directions has objects";
    private const string TooCloseSizeMarginName = "Remove objects too close to other mods' objects > Margin by object size > Margin on every side";
    private const string TooCloseDistanceMarginName = "Remove objects too close to other mods' objects > Margin by fixed distance > Margin on every side";
    private const string TerrainSizeMarginName = "Remove objects on terrain changed by other mods > Margin by object size > Margin on every side";
    private const string TerrainDistanceMarginName = "Remove objects on terrain changed by other mods > Margin by fixed distance > Margin on every side";
    private const string TerrainTypeChangePercentName = "Remove objects on terrain changed by other mods > Remove only when this share of the ground points changed type";
    private const string WaterCoverChangePercentName = "Remove objects on terrain changed by other mods > Remove only when this share of the ground got covered/uncovered";
    private const string TrustedModsName = "Which other mods can cause removals > Trusted mods, and their masters";
    private const string OwnChangesModsName = "Which other mods can cause removals > Mods trusted for their own changes only";
    private const string NeverTrustedModsName = "Which other mods can cause removals > Never trusted mods";
    private const string MaxOtherMastersName = "Which other mods can cause removals > ...only if it has at most this many other masters";

    /// <summary>Validation runs in a fixed order because the warnings are reported in the order found.</summary>
    /// <param name="detailedLogAndReports">The one setting for every run that turns on both the detailed log and the report files.</param>
    /// <param name="onlyWarnWhenRequiringModLoadsAfterPatch">
    /// The one setting for every run that turns the stop for a mod that requires the cleaned mod and loads after the patch into a warning per mod.
    /// </param>
    /// <param name="reportsFolder">Required only when <paramref name="detailedLogAndReports"/> is on.</param>
    public static RunRequest Build(
        ConfiguredSettings config,
        IPluginRecords records,
        LoadOrderPlugins mods,
        bool detailedLogAndReports,
        bool onlyWarnWhenRequiringModLoadsAfterPatch,
        string? reportsFolder,
        int workers)
    {
        var settings = config.Settings;
        var warnings = new List<SettingWarning>();
        WarnOfMissingPlugins(mods, warnings);
        if (FindTarget(records, mods, settings.CleanedMod, out var targetAsTyped, out var targetListing) is { } stop)
        {
            return new RunRequest.Stop(stop, config.Source, [.. warnings]);
        }

        var requiringModsAfterPatch = FindRequiringModsAfterPatch(mods, targetListing.Mod);
        if (requiringModsAfterPatch.Length > 0 && !onlyWarnWhenRequiringModLoadsAfterPatch)
        {
            return new RunRequest.Stop(
                new StopReason(StopKind.RequiringModLoadsAfterPatch, targetAsTyped.ToString(), requiringModsAfterPatch[^1]), config.Source, [.. warnings]);
        }

        WarnOfRequiringModsAfterPatch(requiringModsAfterPatch, targetAsTyped.ToString(), warnings);
        WarnIfMastersUnreadable(targetListing, warnings);
        var modTrust = CreateModTrustSettings(records, mods, targetListing.Mod, settings.Trust, warnings);
        var tooClose = CreateTooCloseSettings(settings.TooClose, warnings);
        var changedTerrain = CreateChangedTerrainSettings(settings.TerrainChange, warnings);
        var movedDistance = NotNegative(settings.ShrinkOrTurn.MovedDistanceInUnits, SettingDefaults.MovedDistanceInUnits, MovedDistanceName, warnings);
        var fit = CreateFitSettings(settings.ShrinkOrTurn, warnings);
        var alsoRemove = CreateAlsoRemoveSettings(settings.FollowUpRemoval, touchChainsShown: detailedLogAndReports, warnings);
        var leftoverSettings = settings.LeftoverInvisibleObjects;
        var leftBehind = CreateLeftBehindSettings(leftoverSettings, warnings);
        var markerMoves = CreateMarkerMoveSettings(leftoverSettings);
        var reports = detailedLogAndReports ? CreateReportFileSettings(reportsFolder, config.Source) : null;

        var runSettings = new RunSettings(
            targetListing.Mod,
            targetAsTyped,
            modTrust,
            CreateWhatToCollect(alsoRemove.Mode, markerMoves, changedTerrain, detailedLogAndReports, movedDistance),
            new SelectionSettings(tooClose.Npcs),
            changedTerrain,
            tooClose,
            fit,
            alsoRemove,
            leftoverSettings.RemoveLeftoverInvisibleObjects ? leftBehind : null,
            markerMoves,
            reports,
            onlyWarnWhenRequiringModLoadsAfterPatch,
            new Execution(workers));
        return new RunRequest.Ready(runSettings, config.Source, [.. warnings]);
    }

    /// <summary>
    /// Builds every config's request in order. A config file that sets no target, a target that is not in the plugin list, is disabled or loads after the patch, or is the patch
    /// plugin, a mod that requires the target and loads after the patch (unless the switch only to warn is on), or two configs with the same target, fail the whole batch;
    /// a Standard request that stops stays a stop request.
    /// </summary>
    public static BatchRequest BuildAll(
        ConfigsRead read,
        IPluginRecords records,
        LoadOrderPlugins mods,
        bool detailedLogAndReports,
        bool onlyWarnWhenRequiringModLoadsAfterPatch,
        string? reportsFolder,
        int workers) =>
        read switch
        {
            ConfigsRead.Failed failed => new BatchRequest.Failed(failed.Message),
            ConfigsRead.Ready ready => BuildEach(
                ready.Configs, records, mods, detailedLogAndReports, onlyWarnWhenRequiringModLoadsAfterPatch, reportsFolder, workers),
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
                PluginListProblem.PluginListedAfterPatch after =>
                    $"{after.Plugin} is loaded by Synthesis above the patch {after.Patch} but is listed after it in the plugin list file {after.Path}.",
                _ => throw new UnreachableException(),
            })
            + ConfigsRead.NothingWasRun);

    private static BatchRequest BuildEach(
        ImmutableArray<ConfiguredSettings> configs,
        IPluginRecords records,
        LoadOrderPlugins mods,
        bool detailedLogAndReports,
        bool onlyWarnWhenRequiringModLoadsAfterPatch,
        string? reportsFolder,
        int workers)
    {
        var requests = ImmutableArray.CreateBuilder<RunRequest>(configs.Length);
        foreach (var config in configs)
        {
            var request = Build(config, records, mods, detailedLogAndReports, onlyWarnWhenRequiringModLoadsAfterPatch, reportsFolder, workers);
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
            StopKind.CleanedModNotInLoadOrder => $"Config file {file.FilePath}: {stop.Reason.Name} is not in your plugin list.{ConfigsRead.NothingWasRun}",
            StopKind.CleanedModDisabled => $"Config file {file.FilePath}: {stop.Reason.Name} is disabled.{ConfigsRead.NothingWasRun}",
            StopKind.CleanedModNotLoadedBySynthesis => $"Config file {file.FilePath}: {stop.Reason.Name} is enabled in your plugin list but Synthesis did not load it for this group.{ConfigsRead.NothingWasRun}",
            StopKind.CleanedModLoadsAfterPatch when stop.Reason.Placement is { } placement =>
                $"Config file {file.FilePath}: {stop.Reason.Name} loads after the patch {placement.Patch} (position {placement.ModPosition} > {placement.PatchPosition}): move {placement.Patch} below it.{ConfigsRead.NothingWasRun}",
            StopKind.RequiringModLoadsAfterPatch when stop.Reason.Placement is { } placement =>
                $"Config file {file.FilePath}: {placement.Mod} requires {stop.Reason.Name} and loads after the patch {placement.Patch} (position {placement.ModPosition} > {placement.PatchPosition}): move {placement.Patch} below it in your plugin list, or turn on \"{RunSettings.OnlyWarnSettingName}\".{ConfigsRead.NothingWasRun}",
            StopKind.TargetIsThePatch => $"Config file {file.FilePath}: {stop.Reason.Name} is the patch this patcher writes.{ConfigsRead.NothingWasRun}",
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

    /// <remarks>The limit is checked only when the rule for mods that require the cleaned mod is on; it is not used otherwise.</remarks>
    private static ModTrustSettings CreateModTrustSettings(
        IPluginRecords records, LoadOrderPlugins mods, PluginName target, ITrustSettingsValues trust, ICollection<SettingWarning> warnings)
    {
        var trusted = ParseListedPlugins(records, mods, trust.TrustedModsAndTheirMasters, TrustedModsName, warnings);
        var ownChangesOnly = ParseListedPlugins(records, mods, trust.ModsTrustedForOwnChangesOnly, OwnChangesModsName, warnings);
        var neverTrusted = ParseListedPlugins(records, mods, trust.NeverTrustedMods, NeverTrustedModsName, warnings);
        WarnOfUnlistableNeverTrusted(neverTrusted, target, mods.PatchMod, warnings);
        var requiringModsTrusted = trust.TrustModsRequiringCleanedModAndTheirMasters;
        return new ModTrustSettings(
            target,
            trusted,
            ownChangesOnly,
            neverTrusted,
            requiringModsTrusted,
            requiringModsTrusted
                ? Clamp(trust.MaxOtherMastersOfRequiringModToTrust, 0, MaxOtherMastersLimit, MaxOtherMastersName, warnings)
                : trust.MaxOtherMastersOfRequiringModToTrust,
            trust.TrustModsLoadingBeforeCleanedMod);
    }

    /// <summary>
    /// A listed plugin that is not in the plugin list changes nothing; it stays listed and is reported. The patch may have no entry
    /// in the plugin list, so it is never reported as missing.
    /// </summary>
    private static ImmutableArray<PluginName> ParseListedPlugins(
        IPluginRecords records, LoadOrderPlugins mods, IReadOnlyList<string> names, string listName, ICollection<SettingWarning> warnings)
    {
        ImmutableArray<PluginName> listed = [.. names.Select(name => ParsePluginName(records, name))];
        foreach (var plugin in listed.Where(plugin => !mods.Positions.Contains(plugin) && plugin != mods.PatchMod))
        {
            warnings.Add(new SettingWarning(listName, $"Warning: {listName}: {plugin} is not in your plugin list."));
        }
        return listed;
    }

    /// <summary>Such entries have no effect: the cleaned mod is always trusted, and the patch is never classified.</summary>
    private static void WarnOfUnlistableNeverTrusted(
        ImmutableArray<PluginName> neverTrusted, PluginName target, PluginName patch, ICollection<SettingWarning> warnings)
    {
        foreach (var plugin in neverTrusted)
        {
            if (plugin == target)
            {
                warnings.Add(new SettingWarning(
                    NeverTrustedModsName,
                    $"Warning: {NeverTrustedModsName}: {plugin} is the mod to clean up, which cannot be listed here; the entry has no effect."));
            }
            else if (plugin == patch)
            {
                warnings.Add(new SettingWarning(
                    NeverTrustedModsName,
                    $"Warning: {NeverTrustedModsName}: {plugin} is the patch this patcher writes, which cannot be listed here; the entry has no effect."));
            }
        }
    }

    /// <summary>With neither margin on, the step switches itself off and removes nothing, so a warning says so.</summary>
    private static TooCloseSettings CreateTooCloseSettings(ITooCloseSettingsValues values, ICollection<SettingWarning> warnings)
    {
        var npcs = ValidateDefined(values.Npcs, SettingDefaults.Npcs, NpcsName, warnings);
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
    /// no height, ground type or water changes counted, or with neither terrain area on, it is passed as off, with a warning,
    /// because it could remove nothing.
    /// </summary>
    private static ChangedTerrainSettings CreateChangedTerrainSettings(ITerrainChangeSettingsValues terrain, ICollection<SettingWarning> warnings)
    {
        var objects = ValidateDefined(terrain.TerrainCheckedObjects, SettingDefaults.TerrainChecked, TerrainCheckedObjectsName, warnings);
        var tolerance = Clamp(
            terrain.HeightChangeTolerance, 0, MaxHeightChangeTolerance, SettingDefaults.HeightChangeTolerance, HeightChangeToleranceName, warnings);
        var sizeMultiplier = ClampSizeMultiplier(terrain.AreaSizeMultiplier, TerrainSizeMarginName, warnings);
        var distanceInUnits = ClampMarginInUnits(terrain.AreaDistanceInUnits, SettingDefaults.TerrainAreaDistanceInUnits, TerrainDistanceMarginName, warnings);
        var typeChangePercent = Clamp(terrain.MinTerrainTypeChangePercent, 0, Percent.PerWhole, TerrainTypeChangePercentName, warnings);
        var waterTolerance = Clamp(
            terrain.WaterHeightChangeTolerance, 0, MaxHeightChangeTolerance, SettingDefaults.WaterHeightChangeTolerance, WaterHeightChangeToleranceName, warnings);
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
                "Warning: changed terrain is on, but no height, ground type or water changes are counted; no object is removed for changed terrain."));
        }
        if (settings.On && !hasAnArea)
        {
            warnings.Add(new SettingWarning(
                "changed terrain",
                "Warning: changed terrain is on, but neither terrain margin is on; no object is removed for changed terrain."));
        }
        return settings with { On = settings.On && countsAChange && hasAnArea };
    }

    /// <summary>A shrink limit of 100 or more and a turn limit of 180 or more are valid: they mean never shrunk and any heading.</summary>
    private static FitSettings CreateFitSettings(IShrinkOrTurnSettingsValues values, ICollection<SettingWarning> warnings) =>
        new(
            AtLeast(values.ShrinkLimitPercent, MinShrinkLimitPercent, ShrinkLimitName, warnings),
            AtLeast(values.TurnLimitDegrees, 0, TurnLimitName, warnings),
            ValidateDefined(values.WhichObjects, SettingDefaults.ShrinkOrTurn, WhichObjectsName, warnings));

    /// <param name="touchChainsShown">Whether the detailed log and the report files, which show the touch chains, are on.</param>
    private static AlsoRemoveSettings CreateAlsoRemoveSettings(
        IFollowUpSettingsValues followUp, bool touchChainsShown, ICollection<SettingWarning> warnings)
    {
        var mode = ValidateDefined(followUp.Mode, SettingDefaults.FollowUpMode, FollowUpModeName, warnings);
        var touchDistance = Clamp(followUp.TouchDistance, 0, MaxTouchDistance, SettingDefaults.TouchDistance, TouchDistanceName, warnings);
        var supportPercent = Clamp(
            followUp.AnchoringThresholdPercent, MinPercent, Percent.PerWhole,
            SettingDefaults.AnchoringThresholdPercent, AnchoringThresholdName, warnings);
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
    /// <param name="movedDistance">The validated distance that counts as moved.</param>
    private static WhatToCollect CreateWhatToCollect(
        FollowUpRemovalMode mode, MarkerMoveSettings? markerMoves, ChangedTerrainSettings changedTerrain, bool detailedLog, float movedDistance)
    {
        var needsSurroundings = mode == FollowUpRemovalMode.OnlyWhatLosesItsSupport || markerMoves != null;
        return new WhatToCollect(
            needsSurroundings,
            needsSurroundings,
            markerMoves != null,
            detailedLog,
            changedTerrain.On,
            changedTerrain.On && changedTerrain.CountTerrainTypeChanges,
            changedTerrain.On && changedTerrain.CountsWaterChanges,
            movedDistance);
    }

    /// <summary>Built (and its settings validated) even when the step is off, so invalid values are always reported.</summary>
    private static LeftBehindSettings CreateLeftBehindSettings(ILeftoverSettingsValues leftovers, ICollection<SettingWarning> warnings)
    {
        return new LeftBehindSettings(
            LookAround: Clamp(leftovers.SearchRadius, MinSearchRadius, MaxSearchRadius, SettingDefaults.SearchRadius, SearchRadiusName, warnings),
            DirectionClearedPercent: WholeTens(leftovers.DirectionThresholdPercent, DirectionThresholdName, warnings),
            ClearedDirectionsPercent: WholeTens(leftovers.RemovedDirectionsPercent, RemovedDirectionsName, warnings),
            OccupiedDirectionsPercent: WholeTens(leftovers.OccupiedDirectionsPercent, OccupiedDirectionsName, warnings),
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
            ? new StopReason(StopKind.CleanedModLoadsAfterPatch, name, new PatchPlacement(mods.PatchMod.ToString(), name, position, mods.PatchPosition))
            : null;
    }

    /// <summary>
    /// The enabled mods that require the cleaned mod and load below the patch, in plugin list order. Overriding a record of the cleaned mod
    /// needs it as a master, so the masters tell every mod that can beat the patch for the cleaned mod's records.
    /// </summary>
    private static ImmutableArray<PatchPlacement> FindRequiringModsAfterPatch(LoadOrderPlugins mods, PluginName cleanedMod) =>
        [.. mods.ListingsRequiring(cleanedMod)
            .Select(listing => new PatchPlacement(mods.PatchMod.ToString(), listing.Mod.ToString(), mods.Positions.Of(listing.Mod), mods.PatchPosition))
            .Where(placement => placement.ModPosition > placement.PatchPosition)
            .OrderBy(placement => placement.ModPosition)];

    private static void WarnOfRequiringModsAfterPatch(ImmutableArray<PatchPlacement> placements, string cleanedMod, ICollection<SettingWarning> warnings)
    {
        foreach (var placement in placements)
        {
            warnings.Add(new SettingWarning(
                RunSettings.OnlyWarnSettingName,
                $"Warning: {placement.Mod} requires {cleanedMod} and loads after the patch {placement.Patch} (position {placement.ModPosition} > {placement.PatchPosition}); the objects of {cleanedMod} that it overrides are left unchanged."));
        }
    }

    /// <summary>Plugin settings are load-order picker values, which the settings reader only creates from valid plugin file names.</summary>
    private static PluginName ParsePluginName(IPluginRecords records, string name) =>
        records.TryParsePluginName(name, out var parsed) ? parsed : throw new InvalidOperationException($"The plugin setting {name} is not a plugin file name.");

    /// <summary>A plugin list entry without its file is a gap in the user's setup: the run goes on without that plugin.</summary>
    private static void WarnOfMissingPlugins(LoadOrderPlugins mods, ICollection<SettingWarning> warnings)
    {
        foreach (var missing in mods.MissingPlugins)
        {
            warnings.Add(new SettingWarning(
                "plugin list",
                $"Warning: {missing} is enabled in your plugin list but its file was not found; it was skipped."));
        }
    }

    private static void WarnIfMastersUnreadable(PluginListing targetListing, ICollection<SettingWarning> warnings)
    {
        if (targetListing.Loaded) return;
        warnings.Add(new SettingWarning(
            CleanedModName,
            $"Warning: could not read {targetListing.Mod} to find its masters."));
    }
}
