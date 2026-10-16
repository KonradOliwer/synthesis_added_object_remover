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
    public static LogSection BuildStopSection(StopReason reason) => new("stop", [StopMessage(reason)]);

    /// <summary>The warnings the settings check found, in the order found.</summary>
    public static LogSection BuildWarningsSection(ImmutableArray<SettingWarning> warnings) =>
        new("warnings", [.. warnings.Select(warning => warning.Message)]);

    public static LogSection BuildReportFolderWarningSection(ReportFileProblem problem) =>
        new("reportFolderWarning", [BuildReportFileProblemLine(problem)]);

    public static LogSection BuildConfigSection(RunSettings settings, IdentifiedMods standing) =>
        new("config",
        [
            $"Target plugin: {settings.TargetAsTyped}",
            settings.TooClose.BySize ? $"Size multiplier: {settings.TooClose.Multiplier}" : "Size multiplier: off",
            .. DescribeDistanceZone(settings.TooClose),
            $"Removal zone: {settings.TooClose.Zone}",
            .. DescribeChangedTerrain(settings.ChangedTerrain),
            $"Excluded plugins: {JoinOrNone(settings.ExcludedNames)}",
            settings.ModIdentification.IgnoreTargetMasters
                ? $"Ignored masters of target: {JoinOrNone(standing.IgnoredMasters.Select(mod => mod.ToString()))}"
                : "Masters of target are not ignored.",
            .. CompatibilityPatches(standing.Patches),
            $"NPCs and creatures: {settings.TooClose.Npcs}",
            DescribeAlsoRemove(settings.AlsoRemove),
            DescribeLeftBehind(settings),
            $"Detailed log: {settings.DetailedLog}",
            $"Write report files: {settings.Reports.WriteFiles}",
            $"Report folder: {settings.Reports.Folder}",
        ]);

    private static ImmutableArray<string> DescribeDistanceZone(TooCloseSettings tooClose) =>
        tooClose.ByDistance ? [$"Removal distance (game units): {tooClose.DistanceInUnits}"] : [];

    /// <summary>The changed-terrain settings in one line; the parts of a check that is switched off are left out.</summary>
    private static ImmutableArray<string> DescribeChangedTerrain(ChangedTerrainSettings terrain)
    {
        if (!terrain.On) return [];

        List<string> parts =
        [
            "Changed terrain: removed",
            $"objects checked: {CheckedObjectsText(terrain.Objects)}",
            $"distance {string.Join(" and ", TerrainDistances(terrain))}",
        ];
        if (terrain.CountHeightChanges) parts.Add($"height changes over {terrain.HeightTolerance} units");
        if (terrain.CountTerrainTypeChanges) parts.Add("terrain type changes counted");
        return [$"{string.Join("; ", parts)}."];
    }

    private static IEnumerable<string> TerrainDistances(ChangedTerrainSettings terrain)
    {
        if (terrain.AreaBySize) yield return $"{terrain.AreaMultiplier} times the object size";
        if (terrain.AreaByDistance) yield return $"{terrain.AreaDistanceInUnits} units";
    }

    private static string CheckedObjectsText(TerrainCheckedObjects objects) => objects switch
    {
        TerrainCheckedObjects.VisibleObjectsOnTheGround => "visible objects on the ground",
        TerrainCheckedObjects.VisibleObjectsAtAnyHeight => "all visible objects",
        TerrainCheckedObjects.AllObjectsIncludingInvisibleOnes => "all objects",
        _ => throw new UnreachableException($"Unknown checked objects {objects}."),
    };

    private static string StopMessage(StopReason reason) => reason.Kind switch
    {
        StopKind.NoTargetSet => "No target plugin set. No changes made.",
        StopKind.TargetNotInLoadOrder => $"Target plugin {reason.Name} is not in the load order. No changes made.",
        StopKind.NoTargetObjects => "Nothing to check. No changes made.",
        _ => throw new UnreachableException($"Unknown stop kind {reason.Kind}."),
    };

    private static string JoinOrNone(IEnumerable<string> names) => TextLists.JoinOr(names, Describe.ListSeparator, "(none)");

    private static ImmutableArray<string> CompatibilityPatches(DetectedPatches patches)
    {
        if (!patches.DetectionOn) return ["Compatibility patches: detection off."];
        var lines = ImmutableArray.CreateBuilder<string>();
        lines.Add($"Compatibility patch detection: {TextFormat.Count(patches.PluginsMasteringTarget)} plugins master the target.");
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
        lines.Add($"Ignored because of compatibility patches: {string.Join(", ", ignoredMods)}.");
        return lines.ToImmutable();
    }

    private static string DescribeAlsoRemove(AlsoRemoveSettings settings) => settings.Mode switch
    {
        FollowUpRemovalMode.Nothing => "Also remove: nothing.",
        FollowUpRemovalMode.EverythingTouchingIt => $"Also remove: everything touching, touch distance {settings.TouchGap}.",
        FollowUpRemovalMode.OnlyWhatLosesItsSupport =>
            $"Also remove: objects supported by it, touch distance {settings.TouchGap}, threshold {TextFormat.Percent(settings.SupportLostFraction)}.",
        _ => throw new UnreachableException($"Unknown follow-up removal mode {settings.Mode}."),
    };

    private static string DescribeLeftBehind(RunSettings settings)
    {
        if (settings.LeftBehind is not { } leftovers) return "Leftover invisible objects: kept.";
        var protectedKinds = TextLists.JoinOr(leftovers.NeverRemove.Order().Select(kind => kind.ToString()), Describe.ListSeparator, Describe.NoEntries);
        return $"Leftover invisible objects: removed, search radius {leftovers.LookAround}, "
            + $"removed area per direction {leftovers.DirectionClearedPercent}%, removed directions required {leftovers.ClearedDirectionsPercent}%, "
            + $"occupied directions required {leftovers.OccupiedDirectionsPercent}%, never removed ({protectedKinds}), "
            + $"kept markers inside other mods' objects {(settings.MarkerMoves != null ? "moved" : "left in place")}.";
    }
}
