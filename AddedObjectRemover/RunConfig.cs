using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;

namespace AddedObjectRemover;

/// <summary>Validated settings of one run.</summary>
/// <param name="AnchoringThreshold">Fraction (0-1) of an object's support that must come from removed objects for Anchoring to remove it.</param>
/// <param name="LeftoverSearchRadius">Distance from an invisible target object to the target's visible objects that count as its surroundings.</param>
/// <param name="LeftoverRemovedShare">Fraction (0-1) of those surroundings that must be removed for the invisible object to be removed.</param>
/// <param name="ProtectedKinds">Invisible object kinds that are never removed as leftovers.</param>
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
    bool RemoveLeftoverInvisibleObjects,
    float LeftoverSearchRadius,
    float LeftoverRemovedShare,
    ProtectedInvisibleObjectsPreset ProtectedPreset,
    IReadOnlySet<InvisibleObjectKind> ProtectedKinds,
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

    private const float MaxSizeMultiplier = 5f;
    private const float MaxTouchDistance = 64f;
    private const float MinPercent = 1f;
    private const float MaxPercent = 100f;
    private const float PercentPerWhole = 100f;

    /// <summary>Null (after logging why) when the run must make no changes.</summary>
    public static RunConfig? Create(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, Settings settings)
    {
        var check = settings.WhatToCheck ?? new CheckSettings();
        if (!TryFindTarget(state, check.TargetPlugin, out var target, out var targetMod)) return null;

        var ignore = settings.WhatToIgnore ?? new IgnoreSettings();
        var excluded = ParseExcludedPlugins(ignore.ExcludedPlugins ?? []);
        var masters = ignore.IgnoreTargetMasters ? GetTargetMasters(target, targetMod) : [];
        var ignored = new HashSet<ModKey>(BaseGamePlugins) { target, state.PatchMod.ModKey };
        ignored.UnionWith(excluded);
        ignored.UnionWith(masters);

        var followUp = settings.FollowUpRemoval ?? new FollowUpRemovalSettings();
        var leftovers = settings.LeftoverInvisibleObjects ?? new LeftoverInvisibleObjectSettings();
        var protectedPreset = ValidatePreset(leftovers.ProtectedTypes);
        return new RunConfig(
            Target: target,
            TargetMod: targetMod,
            IgnoredOrigins: ignored,
            ExcludedPlugins: excluded,
            TargetMasters: masters,
            IgnoreTargetMasters: ignore.IgnoreTargetMasters,
            SizeMultiplier: Clamp(check.SizeMultiplier, 0, MaxSizeMultiplier, CheckSettings.DefaultSizeMultiplier, "size multiplier"),
            FollowUpMode: ValidateMode(followUp.Mode),
            TouchDistance: Clamp(followUp.TouchDistance, 0, MaxTouchDistance, FollowUpRemovalSettings.DefaultTouchDistance, "touch distance"),
            AnchoringThreshold: Clamp(
                followUp.AnchoringThresholdPercent, MinPercent, MaxPercent,
                FollowUpRemovalSettings.DefaultAnchoringThresholdPercent, "anchoring threshold") / PercentPerWhole,
            RemoveLeftoverInvisibleObjects: leftovers.RemoveLeftoverInvisibleObjects,
            LeftoverSearchRadius: Positive(leftovers.SearchRadius, LeftoverInvisibleObjectSettings.DefaultSearchRadius, "search radius"),
            LeftoverRemovedShare: Clamp(
                leftovers.RemovedSurroundingsPercent, MinPercent, MaxPercent,
                LeftoverInvisibleObjectSettings.DefaultRemovedSurroundingsPercent, "removed surroundings percentage") / PercentPerWhole,
            ProtectedPreset: protectedPreset,
            ProtectedKinds: ProtectedInvisibleObjects.Resolve(protectedPreset, ValidateKinds(leftovers.CustomProtectedTypes ?? [])),
            DetailedLog: settings.Diagnostics?.DetailedLog ?? false,
            DiagnosticsFolder: ReadDiagnosticsFolder(settings));
    }

    /// <summary>Empty when no diagnostics folder is set.</summary>
    public static string ReadDiagnosticsFolder(Settings settings) =>
        settings.Diagnostics?.DiagnosticsFolder?.Trim() ?? string.Empty;

    private static bool TryFindTarget(
        IPatcherState<ISkyrimMod, ISkyrimModGetter> state,
        string? targetPlugin,
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

    private static List<ModKey> ParseExcludedPlugins(IEnumerable<string?> names)
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
        Console.WriteLine($"Warning: follow-up removal mode {mode} is invalid; using {FollowUpRemovalSettings.DefaultMode}.");
        return FollowUpRemovalSettings.DefaultMode;
    }

    private static ProtectedInvisibleObjectsPreset ValidatePreset(ProtectedInvisibleObjectsPreset preset)
    {
        if (Enum.IsDefined(preset)) return preset;
        Console.WriteLine($"Warning: protected types {preset} is invalid; using {ProtectedInvisibleObjectsPreset.None}.");
        return ProtectedInvisibleObjectsPreset.None;
    }

    private static List<InvisibleObjectKind> ValidateKinds(IEnumerable<InvisibleObjectKind> kinds)
    {
        var valid = new List<InvisibleObjectKind>();
        foreach (var kind in kinds)
        {
            if (Enum.IsDefined(kind)) valid.Add(kind);
            else Console.WriteLine($"Warning: custom protected type {kind} is invalid and was ignored.");
        }
        return valid;
    }

    /// <summary>A value of 0 or less has no nearest valid value, so it falls back to the default.</summary>
    private static float Positive(float value, float defaultValue, string name)
    {
        if (float.IsFinite(value) && value > 0) return value;
        Console.WriteLine($"Warning: {name} {value} is not more than 0; using {defaultValue}.");
        return defaultValue;
    }

    /// <summary>Out of range values become the nearest valid value; values that are not a number become the default.</summary>
    private static float Clamp(float value, float minimum, float maximum, float defaultValue, string name)
    {
        if (value >= minimum && value <= maximum) return value;
        var clamped = float.IsNaN(value) ? defaultValue : Math.Clamp(value, minimum, maximum);
        Console.WriteLine($"Warning: {name} {value} is outside {minimum}-{maximum}; using {clamped}.");
        return clamped;
    }
}
