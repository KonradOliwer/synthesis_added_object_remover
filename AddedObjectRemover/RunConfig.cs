using System.Globalization;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;

namespace AddedObjectRemover;

/// <summary>Validated settings of one run.</summary>
internal sealed record RunConfig(
    ModKey Target,
    ISkyrimModGetter? TargetMod,
    HashSet<ModKey> IgnoredOrigins,
    IReadOnlyList<ModKey> ExcludedPlugins,
    IReadOnlyList<ModKey> TargetMasters,
    bool ExcludeTargetMasters,
    float Multiplier,
    bool UseNifBounds,
    bool KeepReferencedObjects,
    bool RemoveTouching,
    float TouchTolerance,
    float VoxelSize,
    bool Verbose,
    bool IgnoreReplacedObjects,
    float ReplacementPositionTolerance,
    float ReplacementSizeSimilarity)
{
    public const float DefaultReplacementSizeSimilarity = 0.75f;
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

    /// <summary>Null (after logging why) when the run must make no changes.</summary>
    public static RunConfig? Create(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, Settings settings)
    {
        if (!TryFindTarget(state, settings.TargetPlugin, out var target, out var targetMod)) return null;

        var excluded = ParseExcludedPlugins(settings.ExcludedPlugins);
        var masters = settings.ExcludeTargetMasters ? GetTargetMasters(target, targetMod) : [];
        var ignored = new HashSet<ModKey>(BaseGamePlugins) { target, state.PatchMod.ModKey };
        ignored.UnionWith(excluded);
        ignored.UnionWith(masters);

        return new RunConfig(
            Target: target,
            TargetMod: targetMod,
            IgnoredOrigins: ignored,
            ExcludedPlugins: excluded,
            TargetMasters: masters,
            ExcludeTargetMasters: settings.ExcludeTargetMasters,
            Multiplier: AtLeast(settings.Multiplier, 0, "multiplier"),
            UseNifBounds: settings.UseNifBounds,
            KeepReferencedObjects: settings.SkipReferencedObjects,
            RemoveTouching: settings.RemoveTouchingObjects,
            TouchTolerance: AtLeast(settings.TouchTolerance, 0, "touch tolerance"),
            VoxelSize: AtLeast(settings.VoxelSize, 1, "voxel size", " (minimum 1)"),
            Verbose: settings.VerboseLogging,
            IgnoreReplacedObjects: settings.IgnoreReplacedObjects,
            ReplacementPositionTolerance: AtLeast(settings.ReplacementPositionTolerance, 0, "replacement position tolerance"),
            ReplacementSizeSimilarity: ClampSimilarity(settings.ReplacementSizeSimilarity));
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

    private static float AtLeast(float value, float minimum, string name, string minimumNote = "")
    {
        if (float.IsFinite(value) && value >= minimum) return value;
        Console.WriteLine($"Warning: {name} {value} is invalid{minimumNote}; using {minimum}.");
        return minimum;
    }

    private static float ClampSimilarity(float value)
    {
        if (!float.IsFinite(value))
        {
            var fallback = RunConfig.DefaultReplacementSizeSimilarity.ToString(CultureInfo.InvariantCulture);
            Console.WriteLine($"Warning: replacement size similarity {value} is invalid; using {fallback}.");
            return RunConfig.DefaultReplacementSizeSimilarity;
        }
        if (value is >= 0 and <= 1) return value;
        var clamped = Math.Clamp(value, 0f, 1f);
        Console.WriteLine($"Warning: replacement size similarity {value} is out of range 0-1; using {clamped}.");
        return clamped;
    }
}
