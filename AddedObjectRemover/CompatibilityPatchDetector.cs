using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>A plugin that masters both the target plugin and at least one other, non-base-game mod.</summary>
/// <param name="Patch">The plugin that masters the target and the other mods.</param>
/// <param name="OtherMasters">The other, non-base-game masters it links the target with.</param>
internal readonly record struct CompatibilityPatch(ModKey Patch, IReadOnlyList<ModKey> OtherMasters);

/// <param name="Patches">Plugins treated as compatibility patches.</param>
/// <param name="SkippedTooManyMasters">Plugins that master the target but too many other mods to be a patch, such as generated or merged plugins.</param>
/// <param name="PluginsMasteringTarget">Every plugin that masters the target, patch or not: the detector's input.</param>
internal sealed record CompatibilityPatches(
    IReadOnlyList<CompatibilityPatch> Patches,
    IReadOnlyList<CompatibilityPatch> SkippedTooManyMasters,
    int PluginsMasteringTarget)
{
    public static CompatibilityPatches None { get; } = new([], [], 0);

    /// <summary>Every mod ignored because a compatibility patch links it to the target: the patches themselves and their other masters.</summary>
    public HashSet<ModKey> CollectIgnoredMods()
    {
        var ignored = new HashSet<ModKey>();
        foreach (var patch in Patches)
        {
            ignored.Add(patch.Patch);
            ignored.UnionWith(patch.OtherMasters);
        }
        return ignored;
    }
}

/// <summary>
/// Finds plugins that exist to make the target plugin work with another mod: they master both,
/// so their other masters are as much "the target's own mods" as the target's own masters are.
/// </summary>
internal static class CompatibilityPatchDetector
{
    /// <summary>
    /// Every plugin in the load order that masters <paramref name="target"/> and other, non-base-game
    /// mods; it is a patch when there are at most <paramref name="maxOtherMasters"/> of those.
    /// </summary>
    public static CompatibilityPatches Find(
        IEnumerable<IModListingGetter<ISkyrimModGetter>> loadOrder,
        ModKey target,
        IReadOnlySet<ModKey> targetMasters,
        IReadOnlySet<ModKey> baseGamePlugins,
        int maxOtherMasters)
    {
        var patches = new List<CompatibilityPatch>();
        var skipped = new List<CompatibilityPatch>();
        var mastering = 0;
        foreach (var listing in loadOrder)
        {
            if (listing.ModKey == target || listing.Mod is not { } mod) continue;

            var masters = mod.MasterReferences.Select(master => master.Master).ToList();
            if (!masters.Contains(target)) continue;
            mastering++;

            var otherMasters = masters
                .Where(master => master != target && !baseGamePlugins.Contains(master) && !targetMasters.Contains(master))
                .Distinct()
                .ToList();
            if (otherMasters.Count == 0) continue;
            var candidate = new CompatibilityPatch(listing.ModKey, otherMasters);
            (otherMasters.Count <= maxOtherMasters ? patches : skipped).Add(candidate);
        }
        return new CompatibilityPatches(patches, skipped, mastering);
    }
}
