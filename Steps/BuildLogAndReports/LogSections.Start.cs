using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    public static LogSection BuildStopSection(StopReason reason, SettingsSource source) => new("stop", [StopMessage(reason, source)]);

    /// <summary>The warnings the settings check found, in the order found.</summary>
    public static LogSection BuildWarningsSection(ImmutableArray<SettingWarning> warnings) =>
        new("warnings", [.. warnings.Select(warning => warning.Message)]);

    public static LogSection BuildReportFolderWarningSection(ReportFileProblem problem) =>
        new("reportFolderWarning", [BuildReportFileProblemLine(problem)]);

    public static LogSection BuildConfigSection(RunSettings settings, IdentifiedMods standing) =>
        new("config",
        [
            $"Mod to clean up: {settings.TargetAsTyped}",
            DescribeOtherModsThatNeverCauseRemovals(settings, standing),
            .. CompatibilityPatches(standing.Patches),
            DescribeTooClose(settings.TooClose),
            DescribeChangedTerrain(settings.ChangedTerrain),
            DescribeAlsoRemove(settings.AlsoRemove),
            DescribeLeftBehind(settings),
            DescribeDetailedLogAndReports(settings.Reports),
        ]);

    private static string OnOff(bool on) => on ? "on" : "off";

    /// <summary>"off", or "on" with the detail that only matters when it is on.</summary>
    private static string OnWith(bool on, string detail) => on ? $"on, {detail}" : "off";

    private static string DescribeOtherModsThatNeverCauseRemovals(RunSettings settings, IdentifiedMods standing)
    {
        var identification = settings.ModIdentification;
        var requiredMods = identification.IgnoreTargetMasters
            ? JoinOrNone(standing.IgnoredMasters.Select(mod => mod.ToString()))
            : "off";
        var patchedMods = identification.IgnorePatched ? $"on, a patch requires at most {identification.PatchMasterLimit} other mods" : "off";
        return $"Other mods that never cause removals: Never count these mods: {JoinOrNone(settings.ExcludedNames)}; "
            + $"Never count the mods the cleaned mod requires: {requiredMods}; "
            + $"Never count mods that have a compatibility patch with it: {patchedMods}.";
    }

    private static string DescribeTooClose(TooCloseSettings tooClose) =>
        "Remove objects too close to other mods' objects: "
        + $"Margin by object size: {OnWith(tooClose.BySize, $"{tooClose.Multiplier} x object size")}; "
        + $"Margin by fixed distance: {OnWith(tooClose.ByDistance, $"{tooClose.DistanceInUnits} units")}; "
        + $"Use rough boxes instead of exact shapes (faster): {OnOff(tooClose.Zone == ZoneShape.BoundingBox)}; "
        + $"Other mods' NPCs and creatures cause removal: {tooClose.Npcs}.";

    private static string DescribeChangedTerrain(ChangedTerrainSettings terrain)
    {
        const string group = "Remove objects on terrain changed by other mods";
        if (!terrain.On) return $"{group}: off.";

        return $"{group}: on; Which of the cleaned mod's objects are checked: {terrain.Objects}; "
            + $"Margin by object size: {OnWith(terrain.AreaBySize, $"{terrain.AreaMultiplier} x object size")}; "
            + $"Margin by fixed distance: {OnWith(terrain.AreaByDistance, $"{terrain.AreaDistanceInUnits} units")}; "
            + $"Count height changes: {OnWith(terrain.CountHeightChanges, $"ignoring changes up to {terrain.HeightTolerance} units")}; "
            + $"Count ground type changes: {OnWith(terrain.CountTerrainTypeChanges, $"remove only when this share of the ground points changed type: {terrain.MinTerrainTypeChangePercent}%")}.";
    }

    private static string DescribeDetailedLogAndReports(ReportFileSettings? reports) =>
        $"Detailed log and reports: {(reports is null ? "off" : $"on, report files go to {reports.Folder}")}.";

    private static string StopMessage(StopReason reason, SettingsSource source) => reason.Kind switch
    {
        StopKind.NoTargetSet => "No target plugin set. No changes made.",
        StopKind.TargetNotInLoadOrder => $"Target plugin {reason.Name} is not in the load order. No changes made.",
        StopKind.TargetIsThePatch => $"Target plugin {reason.Name} is the patch this patcher writes. No changes made.",
        StopKind.NoTargetObjects when source is SettingsSource.ConfigFile => "Nothing to check for this config. No changes made for it; the batch goes on.",
        StopKind.NoTargetObjects => "Nothing to check. No changes made.",
        _ => throw new UnreachableException($"Unknown stop kind {reason.Kind}."),
    };

    private static string JoinOrNone(IEnumerable<string> names) => TextLists.JoinOr(names, Describe.ListSeparator, "(none)");

    private static ImmutableArray<string> CompatibilityPatches(DetectedPatches patches)
    {
        if (!patches.DetectionOn) return [];
        var lines = ImmutableArray.CreateBuilder<string>();
        lines.Add($"Compatibility patch detection: {TextFormat.Count(patches.PluginsMasteringTarget)} plugins master the target, {TextFormat.Count(patches.Detected.Length)} of them compatibility patches.");
        lines.AddRange(patches.SkippedTooManyMasters.Select(skipped =>
            $"  Plugin {skipped.Patch} skipped: too many masters (likely generated or merged); it masters the target and {TextFormat.Count(skipped.OtherMasters.Length)} other mods."));
        if (patches.Detected.Length == 0)
        {
            lines.Add("Compatibility patches: none detected.");
            return lines.ToImmutable();
        }
        lines.AddRange(patches.Detected.Select(patch =>
            $"  Compatibility patch {patch.Patch}: links target with {string.Join(", ", patch.OtherMasters)}."));
        var ignoredMods = patches.IgnoredMods.OrderBy(mod => mod.ToString(), StringComparer.Ordinal);
        lines.Add($"Ignored because of compatibility patches (the patches and the other mods they link to the target): {string.Join(", ", ignoredMods)}.");
        return lines.ToImmutable();
    }

    private static string DescribeAlsoRemove(AlsoRemoveSettings settings)
    {
        const string group = "Also remove what rests on removed objects";
        var touchGap = $"Gap that still counts as touching: {settings.TouchGap} units";
        return settings.Mode switch
        {
            FollowUpRemovalMode.Nothing => $"{group}: {settings.Mode}.",
            FollowUpRemovalMode.EverythingTouchingIt => $"{group}: {settings.Mode}; {touchGap}.",
            FollowUpRemovalMode.OnlyWhatLosesItsSupport =>
                $"{group}: {settings.Mode}; {touchGap}; Remove when this much support is gone: {TextFormat.Percent(settings.SupportLostFraction)}.",
            _ => throw new UnreachableException($"Unknown follow-up removal mode {settings.Mode}."),
        };
    }

    private static string DescribeLeftBehind(RunSettings settings)
    {
        const string group = "Invisible objects left behind";
        if (settings.LeftBehind is not { } leftovers) return $"{group}: off.";
        var protectedKinds = TextLists.JoinOr(leftovers.NeverRemove.Order().Select(kind => kind.ToString()), Describe.ListSeparator, Describe.NoEntries);
        return $"{group}: on; Look this far around it: {leftovers.LookAround} units; "
            + $"Decide only if this share of the 8 directions has objects: {leftovers.OccupiedDirectionsPercent}%; "
            + $"A direction is cleared when this much of it was removed: {leftovers.DirectionClearedPercent}%; "
            + $"Remove when this share of those directions is cleared: {leftovers.ClearedDirectionsPercent}%; "
            + $"Never remove these as left behind: {protectedKinds}; "
            + $"Move kept markers out of other mods' objects: {OnOff(settings.MarkerMoves != null)}.";
    }
}
