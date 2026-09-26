using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;

namespace AddedObjectRemover;

/// <summary>Validated settings of one run.</summary>
/// <param name="AnchoringThreshold">Fraction (0-1) of an object's support that must come from removed objects for Anchoring to remove it.</param>
/// <param name="OrphanCheckRadius">Horizontal radius around an invisible target object in which the target's visible objects are counted.</param>
/// <param name="OrphanRemovedShare">Fraction (0-1) of those visible objects that must be removed for the invisible object to be removed.</param>
/// <param name="DiagnosticsFolder">Empty when no diagnostics files are written.</param>
internal sealed record RunConfig(
    ModKey Target,
    ISkyrimModGetter? TargetMod,
    HashSet<ModKey> IgnoredOrigins,
    IReadOnlyList<ModKey> ExcludedPlugins,
    IReadOnlyList<ModKey> TargetMasters,
    bool IgnoreTargetMasters,
    float SizeMultiplier,
    FollowUpRemovalMode FollowUpMode,
    float TouchDistance,
    float AnchoringThreshold,
    bool RemoveOrphanedInvisibleObjects,
    float OrphanCheckRadius,
    float OrphanRemovedShare,
    bool DetailedLog,
    string DiagnosticsFolder)
{
    public bool WritesDiagnostics => DiagnosticsFolder.Length > 0;
}

internal static class RunConfigFactory
{
    /// <summary>Base game plugins never count as "other mods". Creation Club plugins deliberately are not listed.</summary>
    private static readonly ModKey[] BaseGamePlugins =
    [
        ModKey.FromNameAndExtension("Skyrim.esm"),
        ModKey.FromNameAndExtension("Update.esm"),
        ModKey.FromNameAndExtension("Dawnguard.esm"),
        ModKey.FromNameAndExtension("HearthFires.esm"),
        ModKey.FromNameAndExtension("Dragonborn.esm"),
    ];

    private const float MinThresholdPercent = 1f;
    private const float MaxThresholdPercent = 99f;
    private const float MinOrphanSharePercent = 1f;
    private const float MaxOrphanSharePercent = 100f;
    private const float PercentPerWhole = 100f;

    /// <summary>Null (after logging why) when the run must make no changes.</summary>
    public static RunConfig? Create(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, Settings settings)
    {
        if (!TryFindTarget(state, settings.WhatToCheck.TargetPlugin, out var target, out var targetMod)) return null;

        var ignore = settings.WhatToIgnore;
        var excluded = ParseExcludedPlugins(ignore.ExcludedPlugins);
        var masters = ignore.IgnoreTargetMasters ? GetTargetMasters(target, targetMod) : [];
        var ignored = new HashSet<ModKey>(BaseGamePlugins) { target, state.PatchMod.ModKey };
        ignored.UnionWith(excluded);
        ignored.UnionWith(masters);

        var followUp = settings.FollowUpRemoval;
        return new RunConfig(
            Target: target,
            TargetMod: targetMod,
            IgnoredOrigins: ignored,
            ExcludedPlugins: excluded,
            TargetMasters: masters,
            IgnoreTargetMasters: ignore.IgnoreTargetMasters,
            SizeMultiplier: AtLeast(settings.WhatToCheck.SizeMultiplier, 0, "size multiplier"),
            FollowUpMode: ValidateMode(followUp.Mode),
            TouchDistance: AtLeast(followUp.TouchDistance, 0, "touch distance"),
            AnchoringThreshold: ClampPercent(
                followUp.AnchoringThresholdPercent, MinThresholdPercent, MaxThresholdPercent,
                FollowUpRemovalSettings.DefaultAnchoringThresholdPercent, "anchoring threshold") / PercentPerWhole,
            RemoveOrphanedInvisibleObjects: followUp.RemoveOrphanedInvisibleObjects,
            OrphanCheckRadius: Positive(followUp.OrphanCheckRadius, FollowUpRemovalSettings.DefaultOrphanCheckRadius, "orphan check radius"),
            OrphanRemovedShare: ClampPercent(
                followUp.OrphanRemovedSharePercent, MinOrphanSharePercent, MaxOrphanSharePercent,
                FollowUpRemovalSettings.DefaultOrphanRemovedSharePercent, "orphan removed share") / PercentPerWhole,
            DetailedLog: settings.Diagnostics.DetailedLog,
            DiagnosticsFolder: settings.Diagnostics.DiagnosticsFolder.Trim());
    }

    private static bool TryFindTarget(
        IPatcherState<ISkyrimMod, ISkyrimModGetter> state,
        string targetPlugin,
        out ModKey target,
        out ISkyrimModGetter? targetMod)
    {
        targetMod = null;
        if (string.IsNullOrWhiteSpace(targetPlugin))
        {
            target = default;
            Console.WriteLine("No target plugin set. No changes made.");
            return false;
        }
        if (!ModKey.TryFromNameAndExtension(targetPlugin.Trim(), out target))
        {
            Console.WriteLine($"Target plugin '{targetPlugin}' is not a valid plugin file name. No changes made.");
            return false;
        }
        var key = target;
        var listing = state.LoadOrder.ListedOrder.FirstOrDefault(listing => listing.ModKey == key);
        if (listing == null)
        {
            Console.WriteLine($"Target plugin {target} is not in the load order. No changes made.");
            return false;
        }
        targetMod = listing.Mod;
        return true;
    }

    private static List<ModKey> ParseExcludedPlugins(IEnumerable<string> names)
    {
        var excluded = new List<ModKey>();
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (ModKey.TryFromNameAndExtension(name.Trim(), out var key))
            {
                excluded.Add(key);
            }
            else
            {
                Console.WriteLine($"Warning: excluded plugin '{name}' is not a valid plugin file name and was ignored.");
            }
        }
        return excluded;
    }

    private static List<ModKey> GetTargetMasters(ModKey target, ISkyrimModGetter? targetMod)
    {
        if (targetMod == null)
        {
            Console.WriteLine($"Warning: could not read {target} to find its masters.");
            return [];
        }
        return targetMod.MasterReferences.Select(master => master.Master).ToList();
    }

    private static FollowUpRemovalMode ValidateMode(FollowUpRemovalMode mode)
    {
        if (Enum.IsDefined(mode)) return mode;
        Console.WriteLine($"Warning: follow-up removal mode {mode} is invalid; using {FollowUpRemovalMode.AnyTouch}.");
        return FollowUpRemovalMode.AnyTouch;
    }

    private static float AtLeast(float value, float minimum, string name)
    {
        if (float.IsFinite(value) && value >= minimum) return value;
        Console.WriteLine($"Warning: {name} {value} is invalid; using {minimum}.");
        return minimum;
    }

    /// <summary>A value of 0 or less has no nearest valid value, so it falls back to the default.</summary>
    private static float Positive(float value, float defaultValue, string name)
    {
        if (float.IsFinite(value) && value > 0) return value;
        Console.WriteLine($"Warning: {name} {value} is not more than 0; using {defaultValue}.");
        return defaultValue;
    }

    private static float ClampPercent(float value, float minimum, float maximum, float defaultValue, string name)
    {
        if (value >= minimum && value <= maximum) return value;
        var clamped = float.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : defaultValue;
        Console.WriteLine($"Warning: {name} {value} is outside {minimum}-{maximum}; using {clamped}.");
        return clamped;
    }
}
