using System.Diagnostics;
using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover;

/// <summary>Validates the settings against the load order; an invalid value is replaced and reported as a warning, in the order found.</summary>
public static class SettingsValidator
{
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
        var warnings = new List<SettingWarning>();
        LoadOrderChecks.WarnOfMissingPlugins(mods, warnings);
        return LoadOrderChecks.FindTarget(records, mods, config.Settings.CleanedMod) switch
        {
            CleanedModInLoadOrder.Stopped stopped => new RunRequest.Stop(stopped.Reason, config.Source, [.. warnings]),
            CleanedModInLoadOrder.Found found => BuildForTarget(
                config, records, mods, found, detailedLogAndReports, onlyWarnWhenRequiringModLoadsAfterPatch, reportsFolder, workers, warnings),
            _ => throw new UnreachableException(),
        };
    }

    private static RunRequest BuildForTarget(
        ConfiguredSettings config,
        IPluginRecords records,
        LoadOrderPlugins mods,
        CleanedModInLoadOrder.Found target,
        bool detailedLogAndReports,
        bool onlyWarnWhenRequiringModLoadsAfterPatch,
        string? reportsFolder,
        int workers,
        List<SettingWarning> warnings)
    {
        var settings = config.Settings;
        var (targetAsTyped, targetListing) = target;
        var requiringModsAfterPatch = LoadOrderChecks.FindRequiringModsAfterPatch(mods, targetListing.Mod);
        if (requiringModsAfterPatch.Length > 0 && !onlyWarnWhenRequiringModLoadsAfterPatch)
        {
            return new RunRequest.Stop(
                new StopReason(StopKind.RequiringModLoadsAfterPatch, targetAsTyped.ToString(), requiringModsAfterPatch[^1]), config.Source, [.. warnings]);
        }

        LoadOrderChecks.WarnOfRequiringModsAfterPatch(requiringModsAfterPatch, targetAsTyped.ToString(), warnings);
        LoadOrderChecks.WarnIfMastersUnreadable(targetListing, warnings);
        var changed = settings.ChangedObjects;
        var movedDistance = ChangedObjectSettingsBuilder.ValidateMovedDistance(changed, warnings);
        var changedObjects = ChangedObjectSettingsBuilder.Create(changed, warnings);
        var changedCollision = ChangedObjectSettingsBuilder.CreateCollision(changed, warnings);
        var modTrust = TrustSettingsBuilder.Create(records, mods, targetListing.Mod, settings.Trust, warnings);
        var collision = CollisionSettingsBuilder.CreateForCleanedMod(settings.Collision, warnings);
        var alsoRemove = AlsoRemoveSettingsBuilder.Create(settings.FollowUpRemoval, touchChainsShown: detailedLogAndReports, warnings);
        var otherModsObjects = AlsoRemoveSettingsBuilder.CreateForOtherModsObjects(settings.FollowUpRemoval, alsoRemove, warnings);
        var neverRemovedVisible = LeftBehindSettingsBuilder.CreateNeverRemovedVisible(settings.LeftoverObjects);
        var neverRemovedInvisible = LeftBehindSettingsBuilder.CreateNeverRemovedInvisible(settings.LeftoverInvisibleObjects);
        var minObjectsAround = LeftBehindSettingsBuilder.ValidateMinObjectsAround(settings.LeftoverObjects, warnings);
        var visibleLeftBehind = LeftBehindSettingsBuilder.CreateVisible(settings.LeftoverObjects, minObjectsAround, warnings);
        var objectsInClearedAreas = ClearedAreasSettingsBuilder.Create(settings.LeftoverObjects, minObjectsAround, warnings);
        var leftBehind = LeftBehindSettingsBuilder.CreateInvisible(settings.LeftoverInvisibleObjects, warnings);
        var markerMoves = LeftBehindSettingsBuilder.CreateMarkerMoves(settings.LeftoverInvisibleObjects);
        var restoreWhereRemoved = RestoreSettingsBuilder.Create(settings.RestoreWhereRemoved, collision, changedCollision, warnings);
        var reports = detailedLogAndReports ? ReportFileSettingsBuilder.Create(reportsFolder, config.Source) : null;

        var runSettings = new RunSettings(
            targetListing.Mod,
            targetAsTyped,
            modTrust,
            WhatToCollectBuilder.Create(
                markerMoves, reports, collision.ChangedTerrain, changedCollision.ChangedTerrain, changedObjects, changed.Replaced.Process, movedDistance, restoreWhereRemoved),
            new SelectionSettings(collision.TooClose.Npcs),
            collision,
            changedObjects,
            changedCollision,
            alsoRemove,
            otherModsObjects,
            visibleLeftBehind,
            objectsInClearedAreas.Settings,
            objectsInClearedAreas.Off,
            leftBehind,
            neverRemovedVisible,
            neverRemovedInvisible,
            restoreWhereRemoved,
            markerMoves,
            reports,
            onlyWarnWhenRequiringModLoadsAfterPatch,
            new Execution(workers));
        return new RunRequest.Ready(runSettings, config.Source, [.. warnings]);
    }
}
