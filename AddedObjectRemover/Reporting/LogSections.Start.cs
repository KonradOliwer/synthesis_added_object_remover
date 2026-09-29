using System.Collections.Immutable;
using System.Diagnostics;

namespace AddedObjectRemover;

internal static partial class LogSections
{
    public static LogSection Stop(StopReason reason) => new("stop", [StopMessage(reason)]);

    /// <summary>The warnings the settings check found, in the order found.</summary>
    public static LogSection Warnings(ImmutableArray<SettingWarning> warnings) =>
        new("warnings", [.. warnings.Select(warning => warning.Message)]);

    public static LogSection ReportFolderWarning(string message) => new("reportFolderWarning", [message]);

    public static LogSection Config(RunOptions options, ModFacts mods, ModStanding standing) =>
        new("config",
        [
            $"Target plugin: {options.TargetKey}",
            $"Size multiplier: {options.Clash.Multiplier}",
            $"Removal zone: {options.Clash.Zone}",
            $"Excluded plugins: {JoinOrNone(options.ExcludedNames)}",
            options.Standing.IgnoreTargetMasters
                ? $"Ignored masters of target: {JoinOrNone(standing.IgnoredMasters.Select(mod => mods.Mods.KeyOf(mod).ToString()))}"
                : "Masters of target are not ignored.",
            .. CompatibilityPatches(standing.Patches, mods.Mods),
            $"NPCs and creatures: {options.Clash.Npcs}",
            DescribeFollowUpRemoval(options.FollowUp),
            DescribeLeftoverRemoval(options),
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

    private static string JoinOrNone(IEnumerable<string> names)
    {
        var list = names.ToList();
        return list.Count == 0 ? "(none)" : string.Join(", ", list);
    }

    private static ImmutableArray<string> CompatibilityPatches(PatchReport patches, ModTable mods)
    {
        if (!patches.DetectionOn) return ["Compatibility patches: detection off."];
        var lines = ImmutableArray.CreateBuilder<string>();
        lines.Add($"Compatibility patch detection: {patches.PluginsMasteringTarget:N0} plugins master the target.");
        lines.AddRange(patches.SkippedTooManyMasters.Select(skipped =>
            $"  Plugin {mods.KeyOf(skipped.Patch)} skipped: too many masters (likely generated or merged); it masters the target and {skipped.OtherMasters.Length:N0} other mods."));
        if (patches.Detected.Length == 0)
        {
            lines.Add("Compatibility patches: none detected.");
            return lines.ToImmutable();
        }
        lines.AddRange(patches.Detected.Select(patch =>
            $"  Compatibility patch {mods.KeyOf(patch.Patch)}: links target with {string.Join(", ", patch.OtherMasters.Select(mods.KeyOf))}."));
        var ignoredMods = patches.IgnoredMods.Select(mods.KeyOf).OrderBy(mod => mod.ToString(), StringComparer.Ordinal);
        lines.Add($"Ignored because of compatibility patches: {string.Join(", ", ignoredMods)}.");
        return lines.ToImmutable();
    }

    private static string DescribeFollowUpRemoval(FollowUpOptions options) => options.Mode switch
    {
        FollowUpRemovalMode.Nothing => "Also remove: nothing.",
        FollowUpRemovalMode.EverythingTouching => $"Also remove: everything touching, touch distance {options.TouchGap}.",
        FollowUpRemovalMode.ObjectsSupportedByIt =>
            $"Also remove: objects supported by it, touch distance {options.TouchGap}, threshold {options.SupportLostFraction:P0}.",
        _ => throw new UnreachableException($"Unknown follow-up removal mode {options.Mode}."),
    };

    private static string DescribeLeftoverRemoval(RunOptions options)
    {
        if (options.Leftovers is not { } leftovers) return "Leftover invisible objects: kept.";
        var protectedKinds = leftovers.NeverRemove.Count == 0 ? "none" : string.Join(", ", leftovers.NeverRemove.Order());
        return $"Leftover invisible objects: removed, search radius {leftovers.LookAround}, "
            + $"removed area per direction {leftovers.DirectionClearedPercent}%, removed directions required {leftovers.ClearedDirectionsPercent}%, "
            + $"occupied directions required {leftovers.OccupiedDirectionsPercent}%, protected types {leftovers.Preset} ({protectedKinds}), "
            + $"kept markers inside other mods' objects {(options.Relocation != null ? "moved" : "left in place")}.";
    }
}
