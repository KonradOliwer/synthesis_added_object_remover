using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.FitClashingObjects.Contracts;
using AddedObjectRemover.Steps.JudgeChangedObjects.Contracts;
using AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsInClearedAreas.Contracts;
using AddedObjectRemover.Rules.TooCloseToOtherModsObjects.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

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
            .. DescribeChangedObjects(settings),
            .. settings.DetailedLog ? [DescribePluginList(plugins)] : ImmutableArray<string>.Empty,
            DescribeTrustSettings(settings.ModTrust),
            .. DescribeTrustReasons(settings.TargetAsTyped, settings.ModTrust.MaxOtherMastersOfRequiringMod, trust, plugins),
            .. DescribeCollision(settings.Collision),
            DescribeAlsoRemove(settings.AlsoRemove, settings.OtherModsObjects),
            DescribeVisibleLeftBehind(settings),
            DescribeObjectsInClearedAreas(settings.ObjectsInClearedAreas, settings.ObjectsInClearedAreasOff),
            DescribeLeftBehind(settings),
            DescribeRestoreWhereRemoved(settings.RestoreWhereRemoved),
            DescribeDetailedLogAndReports(settings.Reports),
            DescribeOnlyWarnWhenRequiringModLoadsAfterPatch(settings),
        ]);

    private static ImmutableArray<string> DescribeChangedObjects(RunSettings settings)
    {
        const string collisionLabel = "  Collision handling: ";
        var target = settings.TargetAsTyped;
        var collision = settings.ChangedObjectCollision;
        var replaced = settings.Read.ProcessReplaced
            ? DescribeWhenRemoved(settings.ChangedObjects.Replaced, collision.ResizeReplaced, collision.ResizeLimits)
            : "not processed";
        return
        [
            $"Other mods' objects {target} changed: moved farther than {settings.Read.MovedDistance} units: "
                + $"{DescribeWhenRemoved(settings.ChangedObjects.Moved, collision.ResizeMoved, collision.ResizeLimits)}; replaced: {replaced}.",
            .. collision.TooClose == settings.Collision.TooClose && collision.ChangedTerrain.AsRun == settings.Collision.ChangedTerrain.AsRun
                ? [$"{collisionLabel}as for {target}'s objects."]
                : DescribeTooCloseAndChangedTerrain(collision.TooClose, collision.ChangedTerrain, collisionLabel),
        ];
    }

    /// <summary>The limits are shown only for a kind that tries resizing, which is the only kind that uses them.</summary>
    private static string DescribeWhenRemoved(RestoreMode mode, bool triesResizing, FitSettings limits) =>
        triesResizing
            ? $"{mode}, resizing tried first (down to {limits.ShrinkLimitPercent}%, at most {limits.TurnLimitDegrees} degrees)"
            : $"{mode}, no resizing";

    private static string DescribePluginList(LoadOrderPlugins plugins) =>
        $"Plugin list read from {plugins.PluginsFilePath}: {TextFormat.Count(plugins.Positions.Count)} listings.";

    private static string OnOff(bool on) => on ? "on" : "off";

    /// <summary>"off", or "on" with the detail that only matters when it is on.</summary>
    private static string OnWith(bool on, string detail) => on ? $"on, {detail}" : "off";

    private static ImmutableArray<string> DescribeCollision(CollisionSettings collision) =>
    [
        .. DescribeTooCloseAndChangedTerrain(collision.TooClose, collision.ChangedTerrain, string.Empty),
        DescribeShrinkOrTurn(collision.Fit),
    ];

    /// <param name="label">Starts each of the two lines.</param>
    private static ImmutableArray<string> DescribeTooCloseAndChangedTerrain(TooCloseSettings tooClose, ChangedTerrainSettings terrain, string label) =>
    [
        label + DescribeTooClose(tooClose),
        label + DescribeChangedTerrain(terrain),
    ];

    private static string DescribeTooClose(TooCloseSettings tooClose) =>
        "Remove objects too close to other mods' objects: "
        + $"Margin by object size: {OnWith(tooClose.BySize, $"{tooClose.Multiplier} x object size")}; "
        + $"Margin by fixed distance: {OnWith(tooClose.ByDistance, $"{tooClose.DistanceInUnits} units")}; "
        + $"Use rough boxes instead of exact shapes (faster): {OnOff(tooClose.Zone == ZoneShape.BoundingBox)}; "
        + $"Other mods' NPCs and creatures cause removal: {tooClose.Npcs}.";

    private static string DescribeChangedTerrain(ChangedTerrainSettings terrain)
    {
        const string group = "Remove objects on terrain changed by other mods";
        if (!terrain.Runs) return $"{group}: off.";

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

    private static string DescribeShrinkOrTurn(FitSettings fit)
    {
        const string group = "Shrink or turn objects to make them fit";
        return fit.ShrinksOrTurns
            ? $"{group}: Shrink down to: {fit.ShrinkLimitPercent}% of its size; Turn by at most: {fit.TurnLimitDegrees} degrees either way."
            : $"{group}: off.";
    }

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

    /// <remarks>The gap and the share are printed in every mode, since other mods' objects are judged by them whatever the mode.</remarks>
    private static string DescribeAlsoRemove(AlsoRemoveSettings settings, OtherModsObjectsSettings otherModsObjects) =>
        $"Also remove what rests on removed objects: {settings.Mode}; Gap that still counts as touching: {settings.TouchGap} units; "
        + $"Remove when this much support is gone: {TextFormat.Percent(settings.SupportLostFraction)}; "
        + $"Other mods' objects left without support: {otherModsObjects.Handling}.";

    private const string DirectionMethodLineName = "Objects left behind, direction method";

    private static string DescribeVisibleLeftBehind(RunSettings settings) =>
        settings.VisibleLeftBehind is not { } visible
            ? $"{DirectionMethodLineName}: off."
            : $"{DescribeLeftBehindStep(DirectionMethodLineName, visible.Surroundings, "objects", settings.NeverRemovedVisibleKinds.Kinds.Order().Select(VisibleObjectKindText.KindName))}; "
                + $"{KeptWithTooFewObjectsAround(visible.MinObjectsAround)}.";

    private const string AreaMethodLineName = "Objects left behind, area method";

    /// <param name="off">Why the step is off; null when it is on.</param>
    private static string DescribeObjectsInClearedAreas(ObjectsInClearedAreasSettings? settings, ObjectsInClearedAreasOff? off) =>
        settings is { ClearedArea: var area }
            ? $"{AreaMethodLineName}: radius {area.SearchRadius} units; "
                + $"cleared when at most {area.MaxStandingSizePercent}% by size and {area.MaxStandingCountPercent}% by number still stand; "
                + $"{KeptWithTooFewObjectsAround(settings.MinObjectsAround)}."
            : $"{AreaMethodLineName}: {OffText(off ?? throw new UnreachableException("The step for objects in cleared areas has no settings and no reason it is off."))}";

    private static string KeptWithTooFewObjectsAround(int minObjectsAround) => $"kept with {LeftBehindOutcomeText.TooFewObjectsAround(minObjectsAround)}";

    private static string OffText(ObjectsInClearedAreasOff off) => off switch
    {
        ObjectsInClearedAreasOff.Switch => "off.",
        ObjectsInClearedAreasOff.SizeLimit => "off (the size limit is 100% or more).",
        ObjectsInClearedAreasOff.CountLimit => "off (the count limit is 100% or more).",
        ObjectsInClearedAreasOff.BothLimits => "off (the size and count limits are 100% or more).",
        _ => throw new UnreachableException($"Unknown reason {off} the step for objects in cleared areas is off."),
    };

    /// <summary>The settings line of a left-behind step that is on, up to its own settings.</summary>
    /// <param name="objectsKind">"invisible objects" or "objects", as the never-remove setting names the objects.</param>
    private static string DescribeLeftBehindStep(string stepName, LeftBehindSurroundings surroundings, string objectsKind, IEnumerable<string> protectedKindNames) =>
        $"{stepName}: on; "
        + $"Look this far around it: {surroundings.LookAround} units; "
        + $"Decide only if at least this many of the 8 directions have objects: {surroundings.MinOccupiedDirections}; "
        + $"A direction is cleared when this much of it was removed: {surroundings.DirectionClearedPercent}%; "
        + $"Remove when this share of the directions with objects is cleared: {surroundings.ClearedDirectionsPercent}%; "
        + $"Never remove these as {objectsKind} left behind: {TextLists.JoinOr(protectedKindNames, Describe.ListSeparator, Describe.NoEntries)}";

    private static string DescribeRestoreWhereRemoved(WhereRemovedRestoreSettings restore)
    {
        const string group = "Restore where the cleaned mod was removed";
        if (!restore.Runs) return $"{group}: off.";

        var area = restore.ClearedArea;
        var terrainEdges = restore.Terrain ? $"; terrain edges blended over {restore.TerrainEdgeBlendDistance} units" : string.Empty;
        return $"{group}: {string.Join(Describe.ListSeparator, RestoredKindNames(restore))}; "
            + $"an area {area.SearchRadius * 2} units wide counts as cleared when at most {area.MaxStandingSizePercent}% of the cleaned mod's objects there still stand by size "
            + $"and at most {area.MaxStandingCountPercent}% by number{terrainEdges}.";
    }

    private static IEnumerable<string> RestoredKindNames(WhereRemovedRestoreSettings restore)
    {
        if (restore.OtherModsObjects) yield return "other mods' objects";
        if (restore.Terrain) yield return "terrain";
        if (restore.Cells) yield return "cell settings";
        if (restore.Navmeshes) yield return "navmeshes";
    }

    private static string DescribeLeftBehind(RunSettings settings) =>
        settings.InvisibleLeftBehind is not { } leftovers
            ? $"{InvisibleLeftBehindStepName}: off."
            : $"{DescribeLeftBehindStep(InvisibleLeftBehindStepName, leftovers.Surroundings, "invisible objects", settings.NeverRemovedInvisibleKinds.Kinds.Order().Select(ObjectVisibilityText.KindName))}; "
                + $"Move kept markers out of other mods' objects: {OnOff(settings.MarkerMoves.Runs)}.";
}
