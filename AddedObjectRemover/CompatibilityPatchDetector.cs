using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;

namespace AddedObjectRemover;

/// <summary>A plugin that masters both the target plugin and at least one other, non-base-game mod.</summary>
/// <param name="Patch">The plugin that masters the target and the other mods.</param>
/// <param name="OtherMasters">The other, non-base-game masters it links the target with.</param>
internal readonly record struct CompatibilityPatch(ModKey Patch, IReadOnlyList<ModKey> OtherMasters);

/// <summary>
/// Finds plugins that exist to make the target plugin work with another mod: they master both,
/// so their other masters are as much "the target's own mods" as the target's own masters are.
/// </summary>
internal static class CompatibilityPatchDetector
{
    /// <summary>Every plugin in the load order that masters both <paramref name="target"/> and another, non-base-game mod.</summary>
    public static List<CompatibilityPatch> Find(
        IPatcherState<ISkyrimMod, ISkyrimModGetter> state,
        ModKey target,
        IReadOnlySet<ModKey> targetMasters,
        IReadOnlySet<ModKey> baseGamePlugins)
    {
        var patches = new List<CompatibilityPatch>();
        foreach (var listing in state.LoadOrder.ListedOrder)
        {
            if (listing.ModKey == target || listing.Mod is not { } mod) continue;

            var masters = mod.MasterReferences.Select(master => master.Master).ToList();
            if (!masters.Contains(target)) continue;

            var otherMasters = masters
                .Where(master => master != target && !baseGamePlugins.Contains(master) && !targetMasters.Contains(master))
                .Distinct()
                .ToList();
            if (otherMasters.Count > 0) patches.Add(new CompatibilityPatch(listing.ModKey, otherMasters));
        }
        return patches;
    }

    /// <summary>Every mod ignored because a compatibility patch links it to the target: the patches themselves and their other masters.</summary>
    public static HashSet<ModKey> CollectIgnoredMods(IReadOnlyList<CompatibilityPatch> patches)
    {
        var ignored = new HashSet<ModKey>();
        foreach (var patch in patches)
        {
            ignored.Add(patch.Patch);
            ignored.UnionWith(patch.OtherMasters);
        }
        return ignored;
    }
}
