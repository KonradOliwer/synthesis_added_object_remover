using System.Collections.Immutable;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

namespace AddedObjectRemover.Steps.IdentifyTheMods;

/// <summary>
/// Decides which plugins' objects never make a target object too close: the base game, the target
/// and this run's patch plugin, the excluded plugins, the target's masters when that is set, and
/// the mods a compatibility patch links to the target.
/// </summary>
public static class ModIdentifier
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

    public static IdentifiedMods Identify(LoadOrderPlugins mods, ModIdentificationSettings settings)
    {
        var baseGame = BaseGamePlugins.Where(mods.Mods.Knows).ToImmutableArray();
        var targetMasters = mods.Find(settings.Target)?.Masters ?? [];
        var ignoredMasters = settings.IgnoreTargetMasters ? targetMasters : [];
        var requiringMods = FindRequiringMods(mods, settings.Target);
        var patches = settings.IgnorePatched ? DetectPatches(settings, requiringMods, baseGame.ToHashSet(), targetMasters) : DetectedPatches.Off;

        var ignored = new HashSet<PluginName>(baseGame) { settings.Target, mods.PatchMod };
        ignored.UnionWith(settings.Excluded);
        ignored.UnionWith(ignoredMasters);
        ignored.UnionWith(patches.IgnoredMods);
        return new IdentifiedMods(settings.Target, ignored, targetMasters, ignoredMasters, patches, [.. requiringMods.Select(listing => listing.Mod)]);
    }

    /// <remarks>
    /// The patch plugin holds the output of earlier patchers of the same run, which masters
    /// whatever they touched, so it is never a mod that requires the target.
    /// </remarks>
    private static ImmutableArray<PluginListing> FindRequiringMods(LoadOrderPlugins mods, PluginName target) =>
        [.. mods.Listed.Where(listing => listing.Mod != target && listing.Mod != mods.PatchMod && listing.Loaded && listing.Masters.Contains(target))];

    /// <summary>
    /// Every mod that requires the target and other mods: its other masters are its masters
    /// that are not the target, the base game or any master of the target. With none it is no
    /// patch; with at most the limit it is one.
    /// </summary>
    private static DetectedPatches DetectPatches(ModIdentificationSettings settings, ImmutableArray<PluginListing> requiringMods, IReadOnlySet<PluginName> baseGame, ImmutableArray<PluginName> targetMasters)
    {
        var detected = ImmutableArray.CreateBuilder<CompatibilityPatch>();
        var skipped = ImmutableArray.CreateBuilder<CompatibilityPatch>();
        foreach (var listing in requiringMods)
        {
            var otherMasters = listing.Masters
                .Where(master => master != settings.Target && !baseGame.Contains(master) && !targetMasters.Contains(master))
                .Distinct()
                .ToImmutableArray();
            if (otherMasters.IsEmpty) continue;
            (otherMasters.Length <= settings.PatchMasterLimit ? detected : skipped).Add(new CompatibilityPatch(listing.Mod, otherMasters));
        }
        return new DetectedPatches(true, detected.ToImmutable(), skipped.ToImmutable(), requiringMods.Length);
    }
}
