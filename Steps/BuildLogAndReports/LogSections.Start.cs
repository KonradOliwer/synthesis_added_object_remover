using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.FitClashingObjects.Contracts;
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

    public static LogSection BuildConfigSection(RunSettings settings, ModTrustTable trust, LoadOrderPlugins plugins) =>
        new("config",
        [
            $"Mod to clean up: {settings.TargetAsTyped}",
            .. settings.DetailedLog ? [DescribePluginList(plugins)] : ImmutableArray<string>.Empty,
            DescribeTrustSettings(settings.ModTrust),
            .. DescribeTrustReasons(settings.TargetAsTyped, settings.ModTrust.MaxOtherMastersOfRequiringMod, trust, plugins),
            DescribeTooClose(settings.TooClose),
            DescribeChangedTerrain(settings.ChangedTerrain),
            DescribeShrinkOrTurn(settings.Read.MovedDistance, settings.Fit),
            DescribeAlsoRemove(settings.AlsoRemove),
            DescribeLeftBehind(settings),
            DescribeDetailedLogAndReports(settings.Reports),
            DescribeOnlyWarnWhenRequiringModLoadsAfterPatch(settings),
        ]);

    private static string DescribePluginList(LoadOrderPlugins plugins) =>
        $"Plugin list read from {plugins.PluginsFilePath}: {TextFormat.Count(plugins.Positions.Count)} listings.";

    private static string OnOff(bool on) => on ? "on" : "off";

    /// <summary>"off", or "on" with the detail that only matters when it is on.</summary>
    private static string OnWith(bool on, string detail) => on ? $"on, {detail}" : "off";

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
            + $"Count ground type changes: {OnWith(terrain.CountTerrainTypeChanges, $"remove only when this share of the ground points changed type: {terrain.MinTerrainTypeChangePercent}%")}; "
            + $"{DescribeWaterChecks(terrain)}.";
    }

    /// <summary>The share is left out when neither water change is counted, since it is then not used.</summary>
    private static string DescribeWaterChecks(ChangedTerrainSettings terrain)
    {
        var checks = $"Count water height changes: {OnWith(terrain.CountWaterHeightChanges, $"ignoring changes up to {terrain.WaterHeightTolerance} units")}; "
            + $"Count water added/removed changes: {OnOff(terrain.CountWaterAddedOrRemoved)}";
        return terrain.CountsWaterChanges
            ? $"{checks}; remove only when this share of the ground got covered/uncovered: {terrain.MinWaterCoverChangePercent}%"
            : checks;
    }

    private static string DescribeShrinkOrTurn(float movedDistance, FitSettings fit) =>
        "Shrink or turn objects to make them fit: "
        + $"Distance that counts as moved: {movedDistance} units; "
        + $"Shrink down to: {fit.ShrinkLimitPercent}% of its size; "
        + $"Turn by at most: {fit.TurnLimitDegrees} degrees either way.";

    private static string DescribeDetailedLogAndReports(ReportFileSettings? reports) =>
        $"Detailed log and reports: {(reports is null ? "off" : $"on, report files go to {reports.Folder}")}.";

    private static string DescribeOnlyWarnWhenRequiringModLoadsAfterPatch(RunSettings settings) =>
        $"{RunSettings.OnlyWarnSettingName}: {OnOff(settings.OnlyWarnWhenRequiringModLoadsAfterPatch)}.";

    private static string StopMessage(StopReason reason, SettingsSource source) => reason.Kind switch
    {
        StopKind.NoTargetSet => "No mod to clean up is set (CleanedMod). No changes made.",
        StopKind.CleanedModNotInLoadOrder => $"{reason.Name} is not in your plugin list. No changes made.",
        StopKind.CleanedModDisabled => $"{reason.Name} is disabled. No changes made.",
        StopKind.CleanedModLoadsAfterPatch when reason.Placement is { } placement =>
            $"{reason.Name} loads after the patch {placement.Patch} (position {placement.ModPosition} > {placement.PatchPosition}): move {placement.Patch} below it. No changes made.",
        StopKind.RequiringModLoadsAfterPatch when reason.Placement is { } placement =>
            $"{placement.Mod} requires {reason.Name} and loads after the patch {placement.Patch} (position {placement.ModPosition} > {placement.PatchPosition}): move {placement.Patch} below it in your plugin list, or turn on \"{RunSettings.OnlyWarnSettingName}\". No changes made.",
        StopKind.CleanedModNotLoadedBySynthesis => $"{reason.Name} is enabled in your plugin list but Synthesis did not load it for this group. No changes made.",
        StopKind.TargetIsThePatch => $"{reason.Name} is the patch this patcher writes. No changes made.",
        StopKind.NoTargetObjects when source is SettingsSource.ConfigFile => "Nothing to check for this config. No changes made for it; the batch goes on.",
        StopKind.NoTargetObjects => "Nothing to check. No changes made.",
        _ => throw new UnreachableException($"Unknown stop kind {reason.Kind}."),
    };

    private static string JoinOrNone(IEnumerable<string> names) => TextLists.JoinOr(names, Describe.ListSeparator, "(none)");

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
        var protectedKinds = TextLists.JoinOr(leftovers.NeverRemove.Order().Select(ObjectVisibilityText.KindName), Describe.ListSeparator, Describe.NoEntries);
        return $"{group}: on; Look this far around it: {leftovers.LookAround} units; "
            + $"Decide only if this share of the 8 directions has objects: {leftovers.OccupiedDirectionsPercent}%; "
            + $"A direction is cleared when this much of it was removed: {leftovers.DirectionClearedPercent}%; "
            + $"Remove when this share of those directions is cleared: {leftovers.ClearedDirectionsPercent}%; "
            + $"Never remove these as left behind: {protectedKinds}; "
            + $"Move kept markers out of other mods' objects: {OnOff(settings.MarkerMoves != null)}.";
    }
}
