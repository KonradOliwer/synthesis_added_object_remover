using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.FitClashingObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

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
            $"Check other mods' objects the cleaned mod moved farther than: {settings.Read.MovedDistance} units.",
            .. settings.DetailedLog ? [DescribePluginList(plugins)] : ImmutableArray<string>.Empty,
            DescribeTrustSettings(settings.ModTrust),
            .. DescribeTrustReasons(settings.TargetAsTyped, settings.ModTrust.MaxOtherMastersOfRequiringMod, trust, plugins),
            DescribeTooClose(settings.TooClose),
            DescribeChangedTerrain(settings.ChangedTerrain),
            DescribeShrinkOrTurn(settings.Fit),
            DescribeAlsoRemove(settings.AlsoRemove, settings.OtherModsObjects),
            DescribeVisibleLeftBehind(settings.VisibleLeftBehind),
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

    private static string DescribeShrinkOrTurn(FitSettings fit) =>
        "Shrink or turn objects to make them fit: "
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

    /// <remarks>The gap and the share are printed in every mode, since other mods' objects are judged by them whatever the mode.</remarks>
    private static string DescribeAlsoRemove(AlsoRemoveSettings settings, OtherModsObjectsSettings otherModsObjects) =>
        $"Also remove what rests on removed objects: {settings.Mode}; Gap that still counts as touching: {settings.TouchGap} units; "
        + $"Remove when this much support is gone: {TextFormat.Percent(settings.SupportLostFraction)}; "
        + $"Other mods' objects left without support: {otherModsObjects.Handling}.";

    private static string DescribeVisibleLeftBehind(VisibleLeftBehindSettings? visible)
    {
        if (visible is null) return $"{VisibleLeftBehindStepName}: off.";
        var protectedKinds = TextLists.JoinOr(
            visible.NeverRemove.Order().Select(VisibleLeftBehindOutcomeText.KindName), Describe.ListSeparator, Describe.NoEntries);
        return $"{VisibleLeftBehindStepName}: on; "
            + $"{DescribeSurroundings(visible.LookAround, visible.MinOccupiedDirections, visible.DirectionClearedPercent, visible.ClearedDirectionsPercent)}; "
            + $"Never remove these as visible objects left behind: {protectedKinds}.";
    }

    private static string DescribeSurroundings(float lookAround, int minOccupiedDirections, int directionClearedPercent, int clearedDirectionsPercent) =>
        $"Look this far around it: {lookAround} units; "
        + $"Decide only if at least this many of the 8 directions have objects: {minOccupiedDirections}; "
        + $"A direction is cleared when this much of it was removed: {directionClearedPercent}%; "
        + $"Remove when this share of the directions with objects is cleared: {clearedDirectionsPercent}%";

    private static string DescribeLeftBehind(RunSettings settings)
    {
        if (settings.LeftBehind is not { } leftovers) return $"{LeftBehindStepName}: off.";
        var protectedKinds = TextLists.JoinOr(leftovers.NeverRemove.Order().Select(ObjectVisibilityText.KindName), Describe.ListSeparator, Describe.NoEntries);
        return $"{LeftBehindStepName}: on; "
            + $"{DescribeSurroundings(leftovers.LookAround, leftovers.MinOccupiedDirections, leftovers.DirectionClearedPercent, leftovers.ClearedDirectionsPercent)}; "
            + $"Never remove these as invisible objects left behind: {protectedKinds}; "
            + $"Move kept markers out of other mods' objects: {OnOff(settings.MarkerMoves != null)}.";
    }
}
