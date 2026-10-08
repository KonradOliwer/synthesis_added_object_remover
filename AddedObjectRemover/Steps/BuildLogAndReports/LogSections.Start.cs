using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    public static LogSection Stop(StopReason reason) => new("stop", [StopMessage(reason)]);

    /// <summary>The warnings the settings check found, in the order found.</summary>
    public static LogSection Warnings(ImmutableArray<SettingWarning> warnings) =>
        new("warnings", [.. warnings.Select(warning => warning.Message)]);

    public static LogSection ReportFolderWarning(string message) => new("reportFolderWarning", [message]);

    public static LogSection Config(RunSettings options, IdentifiedMods standing) =>
        new("config",
        [
            $"Target plugin: {options.TargetAsTyped}",
            $"Size multiplier: {options.TooClose.Multiplier}",
            $"Removal zone: {options.TooClose.Zone}",
            $"Excluded plugins: {JoinOrNone(options.ExcludedNames)}",
            options.ModIdentification.IgnoreTargetMasters
                ? $"Ignored masters of target: {JoinOrNone(standing.IgnoredMasters.Select(mod => mod.ToString()))}"
                : "Masters of target are not ignored.",
            .. CompatibilityPatches(standing.Patches),
            $"NPCs and creatures: {options.TooClose.Npcs}",
            DescribeAlsoRemove(options.AlsoRemove),
            DescribeLeftBehind(options),
            $"Detailed log: {options.DetailedLog}",
            $"Write report files: {options.Reports.WriteFiles}",
            $"Report folder: {options.Reports.Folder}",
        ]);

    private static string StopMessage(StopReason reason) => reason.Kind switch
    {
        StopKind.NoTargetSet => "No target plugin set. No changes made.",
        StopKind.InvalidTargetName => $"Target plugin '{reason.Name}' is not a valid plugin file name. No changes made.",
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

    private static string DescribeAlsoRemove(AlsoRemoveSettings options) => options.Mode switch
    {
        FollowUpRemovalMode.Nothing => "Also remove: nothing.",
        FollowUpRemovalMode.EverythingTouching => $"Also remove: everything touching, touch distance {options.TouchGap}.",
        FollowUpRemovalMode.ObjectsSupportedByIt =>
            $"Also remove: objects supported by it, touch distance {options.TouchGap}, threshold {TextFormat.Percent(options.SupportLostFraction)}.",
        _ => throw new UnreachableException($"Unknown follow-up removal mode {options.Mode}."),
    };

    private static string DescribeLeftBehind(RunSettings options)
    {
        if (options.LeftBehind is not { } leftovers) return "Leftover invisible objects: kept.";
        var protectedKinds = TextLists.JoinOr(leftovers.NeverRemove.Order().Select(kind => kind.ToString()), Describe.ListSeparator, Describe.NoEntries);
        return $"Leftover invisible objects: removed, search radius {leftovers.LookAround}, "
            + $"removed area per direction {leftovers.DirectionClearedPercent}%, removed directions required {leftovers.ClearedDirectionsPercent}%, "
            + $"occupied directions required {leftovers.OccupiedDirectionsPercent}%, protected types {leftovers.Preset} ({protectedKinds}), "
            + $"kept markers inside other mods' objects {(options.MarkerMoves != null ? "moved" : "left in place")}.";
    }
}
