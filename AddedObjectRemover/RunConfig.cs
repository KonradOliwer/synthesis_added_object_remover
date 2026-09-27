using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;

namespace AddedObjectRemover;

/// <summary>Validated settings of one run.</summary>
/// <param name="AnchoringThreshold">Fraction (0-1) of an object's support that must come from removed objects for ObjectsSupportedByIt to remove it.</param>
/// <param name="WritesDiagnostics">Whether report files (CSVs) are written; DiagnosticsFolder is resolved regardless so the folder can be shown in the log.</param>
internal sealed record RunConfig(
    ModKey Target,
    ISkyrimModGetter? TargetMod,
    HashSet<ModKey> IgnoredOrigins,
    IReadOnlyList<ModKey> ExcludedPlugins,
    IReadOnlyList<ModKey> TargetMasters,
    bool IgnoreTargetMasters,
    CompatibilityPatches CompatibilityPatches,
    NpcHandling NpcHandling,
    float SizeMultiplier,
    ZoneShape ZoneShape,
    FollowUpRemovalMode FollowUpMode,
    float TouchDistance,
    float AnchoringThreshold,
    LeftoverConfig Leftovers,
    bool DetailedLog,
    bool WritesDiagnostics,
    string DiagnosticsFolder);

/// <summary>Validated settings of the leftover invisible objects step.</summary>
/// <param name="SearchRadius">Largest distance from an invisible target object to the target's visible objects that count as its surroundings.</param>
/// <param name="DirectionThresholdPercent">Removed share of a direction's ground area (10-100) that makes the direction removed.</param>
/// <param name="RemovedDirectionsPercent">Share of the occupied directions (10-100) that must be removed for removal.</param>
/// <param name="OccupiedDirectionsPercent">Share of all directions (10-100) that must be occupied for the direction rule to apply.</param>
/// <param name="ProtectedKinds">Invisible object kinds that are never removed as leftovers.</param>
/// <param name="MovesKeptMarkers">Kept markers inside another mod's object are moved to a free spot; false whenever the step is off.</param>
internal sealed record LeftoverConfig(
    bool Enabled,
    float SearchRadius,
    int DirectionThresholdPercent,
    int RemovedDirectionsPercent,
    int OccupiedDirectionsPercent,
    ProtectedInvisibleObjectsPreset ProtectedPreset,
    IReadOnlySet<InvisibleObjectKind> ProtectedKinds,
    bool MovesKeptMarkers);

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

    private static readonly HashSet<ModKey> BaseGamePluginSet = BaseGamePlugins.ToHashSet();

    private const float MaxSizeMultiplier = 5f;
    private const float MaxTouchDistance = 64f;
    private const float MinSearchRadius = 64f;
    private const float MaxSearchRadius = 8192f;
    private const int MinOtherMastersForPatch = 1;
    private const int MaxOtherMastersForPatch = 100;
    private const float MinPercent = 1f;
    private const int PercentStep = 10;

    /// <summary>Null (after logging why) when the run must make no changes.</summary>
    public static RunConfig? Create(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, Settings settings)
    {
        var check = settings.WhatToCheck ?? new CheckSettings();
        if (!TryFindTarget(state, check.TargetPlugin, out var target, out var targetMod)) return null;

        var ignore = settings.WhatToIgnore ?? new IgnoreSettings();
        var excluded = ParseExcludedPlugins(ignore.ExcludedPlugins ?? []);
        var allTargetMasters = GetTargetMasters(target, targetMod);
        var masters = ignore.IgnoreTargetMasters ? allTargetMasters : [];
        var ignored = new HashSet<ModKey>(BaseGamePlugins) { target, state.PatchMod.ModKey };
        ignored.UnionWith(excluded);
        ignored.UnionWith(masters);

        var compatibilityPatches = ignore.IgnoreModsPatchedWithTarget
            ? CompatibilityPatchDetector.Find(
                state, target, allTargetMasters.ToHashSet(), BaseGamePluginSet,
                Clamp(ignore.MaxOtherMastersForPatch, MinOtherMastersForPatch, MaxOtherMastersForPatch, "maximum other masters for a patch"))
            : CompatibilityPatches.None;
        ignored.UnionWith(compatibilityPatches.CollectIgnoredMods());

        var followUp = settings.FollowUpRemoval ?? new FollowUpRemovalSettings();
        return new RunConfig(
            Target: target,
            TargetMod: targetMod,
            IgnoredOrigins: ignored,
            ExcludedPlugins: excluded,
            TargetMasters: masters,
            IgnoreTargetMasters: ignore.IgnoreTargetMasters,
            CompatibilityPatches: compatibilityPatches,
            NpcHandling: ValidateNpcHandling(ignore.NpcHandling),
            SizeMultiplier: Clamp(check.SizeMultiplier, 0, MaxSizeMultiplier, CheckSettings.DefaultSizeMultiplier, "size multiplier"),
            ZoneShape: ValidateZoneShape(check.ZoneShape),
            FollowUpMode: ValidateMode(followUp.Mode),
            TouchDistance: Clamp(followUp.TouchDistance, 0, MaxTouchDistance, FollowUpRemovalSettings.DefaultTouchDistance, "touch distance"),
            AnchoringThreshold: Clamp(
                followUp.AnchoringThresholdPercent, MinPercent, Percent.PerWhole,
                FollowUpRemovalSettings.DefaultAnchoringThresholdPercent, "anchoring threshold") / Percent.PerWhole,
            Leftovers: CreateLeftoverConfig(settings.LeftoverInvisibleObjects ?? new LeftoverInvisibleObjectSettings()),
            DetailedLog: settings.Diagnostics?.DetailedLog ?? false,
            WritesDiagnostics: settings.Diagnostics?.WriteReportFiles ?? false,
            DiagnosticsFolder: ResolveReportFolder(state, settings));
    }

    private static LeftoverConfig CreateLeftoverConfig(LeftoverInvisibleObjectSettings leftovers)
    {
        var protectedPreset = ValidatePreset(leftovers.ProtectedTypes);
        return new LeftoverConfig(
            Enabled: leftovers.RemoveLeftoverInvisibleObjects,
            SearchRadius: Clamp(leftovers.SearchRadius, MinSearchRadius, MaxSearchRadius, LeftoverInvisibleObjectSettings.DefaultSearchRadius, "search radius"),
            DirectionThresholdPercent: WholeTens(leftovers.DirectionThresholdPercent, "removed area per direction"),
            RemovedDirectionsPercent: WholeTens(leftovers.RemovedDirectionsPercent, "removed directions required"),
            OccupiedDirectionsPercent: WholeTens(leftovers.OccupiedDirectionsPercent, "occupied directions required"),
            ProtectedPreset: protectedPreset,
            ProtectedKinds: ProtectedInvisibleObjects.Resolve(protectedPreset, ValidateKinds(leftovers.CustomProtectedTypes ?? [])),
            MovesKeptMarkers: leftovers.RemoveLeftoverInvisibleObjects && leftovers.MoveKeptMarkersOutOfOtherModsObjects);
    }

    /// <summary>A rooted path is used as is; a relative one is resolved against the output plugin's folder.</summary>
    public static string ResolveReportFolder(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, Settings settings)
    {
        var configured = settings.Diagnostics?.DiagnosticsFolder?.Trim();
        var folder = string.IsNullOrEmpty(configured) ? DiagnosticsSettings.DefaultReportFolder : configured;
        if (Path.IsPathRooted(folder)) return folder;
        var outputDirectory = Path.GetDirectoryName(state.OutputPath.Path) ?? string.Empty;
        return Path.Combine(outputDirectory, folder);
    }

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

    private static ZoneShape ValidateZoneShape(ZoneShape zoneShape)
    {
        if (Enum.IsDefined(zoneShape)) return zoneShape;
        Console.WriteLine($"Warning: removal zone {zoneShape} is invalid; using {CheckSettings.DefaultZoneShape}.");
        return CheckSettings.DefaultZoneShape;
    }

    private static NpcHandling ValidateNpcHandling(NpcHandling handling)
    {
        if (Enum.IsDefined(handling)) return handling;
        Console.WriteLine($"Warning: NPCs and creatures setting {handling} is invalid; using {IgnoreSettings.DefaultNpcHandling}.");
        return IgnoreSettings.DefaultNpcHandling;
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

    /// <summary>A percentage limited to 10-100 and rounded to the nearest ten.</summary>
    private static int WholeTens(int percent, string name)
    {
        var valid = (int)Math.Round(Math.Clamp(percent, PercentStep, Percent.PerWhole) / (double)PercentStep, MidpointRounding.AwayFromZero) * PercentStep;
        if (valid != percent) Console.WriteLine($"Warning: {name} {percent} is not a multiple of {PercentStep} from {PercentStep} to {Percent.PerWhole}; using {valid}.");
        return valid;
    }

    private static int Clamp(int value, int minimum, int maximum, string name)
    {
        var clamped = Math.Clamp(value, minimum, maximum);
        if (clamped != value) Console.WriteLine($"Warning: {name} {value} is outside {minimum}-{maximum}; using {clamped}.");
        return clamped;
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
