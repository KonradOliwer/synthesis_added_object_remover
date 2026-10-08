using System.Collections.Immutable;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

namespace AddedObjectRemover.Steps.IdentifyTheMods;

/// <summary>
/// Decides which plugins' objects never make a target object too close: the base game, the target
/// and this run's patch plugin, the excluded plugins, the target's masters when that is set, and
/// the mods a compatibility patch links to the target.
/// </summary>
internal static class IdentifyTheMods
{
    /// <summary>Base game plugins never count as "other mods". Creation Club plugins deliberately are not listed.</summary>
    private static readonly PluginName[] BaseGamePlugins =
    [
        new("Skyrim.esm"),
        new("Update.esm"),
        new("Dawnguard.esm"),
        new("HearthFires.esm"),
        new("Dragonborn.esm"),
    ];

    public static IdentifiedMods Decide(LoadOrderPlugins mods, ModIdentificationSettings options)
    {
        var baseGame = BaseGamePlugins.Where(mods.Mods.Knows).ToHashSet();
        var targetMasters = mods.Find(options.Target)?.Masters ?? [];
        var ignoredMasters = options.IgnoreTargetMasters ? targetMasters : [];
        var patches = options.IgnorePatched ? DetectPatches(mods, options, baseGame, targetMasters) : DetectedPatches.Off;

        var ignored = new HashSet<PluginName>(baseGame) { options.Target, mods.PatchMod };
        ignored.UnionWith(options.Excluded);
        ignored.UnionWith(ignoredMasters);
        ignored.UnionWith(patches.IgnoredMods);
        return new IdentifiedMods(options.Target, ignored, targetMasters, ignoredMasters, patches);
    }

    /// <summary>
    /// Every loaded plugin other than the target and the patch plugin that masters the target and
    /// other mods: its other masters are its masters that are not the target, the base game or any
    /// master of the target. With none it is no patch; with at most the limit it is one.
    /// </summary>
    /// <remarks>
    /// The patch plugin holds the output of earlier patchers of the same run, which masters
    /// whatever they touched, so it is never a compatibility patch.
    /// </remarks>
    private static DetectedPatches DetectPatches(LoadOrderPlugins mods, ModIdentificationSettings options, IReadOnlySet<PluginName> baseGame, ImmutableArray<PluginName> targetMasters)
    {
        var detected = ImmutableArray.CreateBuilder<CompatibilityPatch>();
        var skipped = ImmutableArray.CreateBuilder<CompatibilityPatch>();
        var mastering = 0;
        foreach (var listing in mods.Listed)
        {
            if (listing.Mod == options.Target || listing.Mod == mods.PatchMod || !listing.Loaded || !listing.Masters.Contains(options.Target)) continue;
            mastering++;

            var otherMasters = listing.Masters
                .Where(master => master != options.Target && !baseGame.Contains(master) && !targetMasters.Contains(master))
                .Distinct()
                .ToImmutableArray();
            if (otherMasters.IsEmpty) continue;
            (otherMasters.Length <= options.PatchMasterLimit ? detected : skipped).Add(new CompatibilityPatch(listing.Mod, otherMasters));
        }
        return new DetectedPatches(true, detected.ToImmutable(), skipped.ToImmutable(), mastering);
    }
}
