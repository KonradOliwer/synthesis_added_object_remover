using System.Collections.Immutable;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

namespace AddedObjectRemover.Steps.IdentifyTheMods;

/// <summary>
/// Decides how far each plugin is trusted. The first rule that names a plugin decides: never trusted; always trusted (the base game,
/// the cleaned mod and its masters); listed as trusted, with their masters; listed as trusted for own changes only; the automatic
/// rules (mods that require the cleaned mod within the other-master limit, with their masters, then the plugins loading before the
/// cleaned mod); else untrusted. The patch is not classified.
/// </summary>
public static class ModTrustClassifier
{
    /// <summary>Creation Club plugins are deliberately not part of the base game.</summary>
    private static readonly string[] BaseGameFileNames = ["Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm"];

    public static ModTrustTable Classify(LoadOrderPlugins plugins, ModTrustSettings settings)
    {
        var listings = ListingsByPlugin(plugins);
        var known = KnownPlugins(plugins);
        var alwaysTrusted = AlwaysTrusted.Of(settings.Target, listings);
        var requiringMods = RequiringMods(plugins, settings.Target);
        var otherMasterCounts = known.ToImmutableDictionary(plugin => plugin, plugin => alwaysTrusted.OtherMasterCount(MastersOf(plugin, listings)));
        var fixedDecisions = known.ToImmutableDictionary(plugin => plugin, FixedRules.Of(settings, plugins.PatchMod, alwaysTrusted, listings).Decide);
        var undecidedRequiringMods = requiringMods.Where(plugin => fixedDecisions[plugin] is null).ToImmutableHashSet();
        var automaticRules = AutomaticRules.Of(plugins, settings, listings, undecidedRequiringMods, otherMasterCounts);
        var decisions = known.ToImmutableDictionary(plugin => plugin, plugin => fixedDecisions[plugin] ?? automaticRules.Decide(plugin));
        return BuildTable(
            settings.Target, plugins.PatchMod, decisions, requiringMods, NeedingMods(plugins, settings.Target), otherMasterCounts, alwaysTrusted.CleanedModMasters);
    }

    private static ModTrustTable BuildTable(
        PluginName cleanedMod,
        PluginName patch,
        IReadOnlyDictionary<PluginName, TrustDecision> decisions,
        ImmutableHashSet<PluginName> requiringMods,
        ImmutableHashSet<PluginName> needingMods,
        IReadOnlyDictionary<PluginName, int> otherMasterCounts,
        ImmutableHashSet<PluginName> cleanedModMasters)
    {
        var trust = decisions.ToImmutableDictionary(
            entry => entry.Key,
            entry => new PluginTrust(
                entry.Value.Trust,
                entry.Value.Reason,
                requiringMods.Contains(entry.Key),
                otherMasterCounts[entry.Key],
                cleanedModMasters.Contains(entry.Key),
                needingMods.Contains(entry.Key)));
        return new ModTrustTable(cleanedMod, patch, trust, trust.Values.Count(entry => entry.Reason == TrustReason.LoadsBeforeCleanedMod));
    }

    /// <remarks>A plugin listed twice keeps its first listing.</remarks>
    private static IReadOnlyDictionary<PluginName, PluginListing> ListingsByPlugin(LoadOrderPlugins plugins)
    {
        var listings = new Dictionary<PluginName, PluginListing>();
        foreach (var listing in plugins.Listed) listings.TryAdd(listing.Mod, listing);
        return listings;
    }

    /// <summary>The listed plugins and every master they name, without the patch.</summary>
    private static ImmutableArray<PluginName> KnownPlugins(LoadOrderPlugins plugins) =>
        [.. plugins.Listed.Select(listing => listing.Mod)
            .Concat(plugins.Listed.SelectMany(listing => listing.Masters))
            .Where(plugin => plugin != plugins.PatchMod)
            .Distinct()];

    /// <returns>Empty for a plugin that is not listed or could not be read.</returns>
    private static ImmutableArray<PluginName> MastersOf(PluginName plugin, IReadOnlyDictionary<PluginName, PluginListing> listings) =>
        listings.TryGetValue(plugin, out var listing) ? listing.Masters : [];

    private static ImmutableHashSet<PluginName> MastersOfAll(IEnumerable<PluginName> plugins, IReadOnlyDictionary<PluginName, PluginListing> listings) =>
        [.. plugins.SelectMany(plugin => MastersOf(plugin, listings))];

    private static ImmutableHashSet<PluginName> RequiringMods(LoadOrderPlugins plugins, PluginName cleanedMod) =>
        [.. plugins.ListingsRequiring(cleanedMod).Select(listing => listing.Mod)];

    /// <summary>The listed plugins that cannot load without the cleaned mod: the mods requiring it, and the mods requiring those.</summary>
    private static ImmutableHashSet<PluginName> NeedingMods(LoadOrderPlugins plugins, PluginName cleanedMod)
    {
        var needing = ImmutableHashSet.CreateBuilder<PluginName>();
        var pending = new Queue<PluginName>([cleanedMod]);
        while (pending.TryDequeue(out var master))
        {
            foreach (var listing in plugins.ListingsRequiring(master))
            {
                if (needing.Add(listing.Mod)) pending.Enqueue(listing.Mod);
            }
        }
        return needing.ToImmutable();
    }

    private static ImmutableHashSet<PluginName> CarryingPatchMarker(LoadOrderPlugins plugins) =>
        [.. plugins.Listed.Where(listing => listing.CarriesPatchMarker).Select(listing => listing.Mod)];

    /// <remarks>A plugin that is not in the game's plugin list does not load before the cleaned mod.</remarks>
    private static ImmutableHashSet<PluginName> LoadingBefore(LoadOrderPlugins plugins, PluginName cleanedMod)
    {
        var cleanedModPosition = plugins.Positions.Of(cleanedMod);
        return [.. plugins.Listed
            .Select(listing => listing.Mod)
            .Where(plugin => plugins.Positions.Contains(plugin) && plugins.Positions.Of(plugin) < cleanedModPosition)];
    }

    /// <summary>The cleaned mod and the patch cannot be listed: such entries are left out, so the patch's masters never become trusted through a list.</summary>
    private static ImmutableHashSet<PluginName> UsableEntries(ImmutableArray<PluginName> entries, PluginName cleanedMod, PluginName patch) =>
        [.. entries.Where(entry => entry != cleanedMod && entry != patch)];

    private sealed record TrustDecision(ModTrust Trust, TrustReason? Reason)
    {
        public static TrustDecision Trusted(TrustReason reason) => new(ModTrust.Trusted, reason);

        public static TrustDecision Untrusted(TrustReason? reason) => new(ModTrust.Untrusted, reason);
    }

    /// <summary>The base game, the cleaned mod and the cleaned mod's masters.</summary>
    private sealed record AlwaysTrusted(PluginName CleanedMod, ImmutableHashSet<PluginName> BaseGame, ImmutableHashSet<PluginName> CleanedModMasters)
    {
        public static AlwaysTrusted Of(PluginName cleanedMod, IReadOnlyDictionary<PluginName, PluginListing> listings) =>
            new(cleanedMod, [.. BaseGameFileNames.Select(fileName => new PluginName(fileName))], [.. listings[cleanedMod].Masters]);

        public TrustReason? ReasonFor(PluginName plugin)
        {
            if (BaseGame.Contains(plugin)) return TrustReason.BaseGame;
            if (plugin == CleanedMod) return TrustReason.CleanedMod;
            if (CleanedModMasters.Contains(plugin)) return TrustReason.CleanedModMaster;
            return null;
        }

        /// <summary>Other masters are the masters that are not always trusted.</summary>
        public int OtherMasterCount(ImmutableArray<PluginName> masters) => masters.Where(master => ReasonFor(master) is null).Distinct().Count();
    }

    /// <summary>The rules before the automatic ones: never trusted, always trusted, and the two lists of trusted mods.</summary>
    /// <param name="Listed">The listed trusted mods that are not also never trusted; a never trusted entry does not make its masters trusted.</param>
    private sealed record FixedRules(
        AlwaysTrusted AlwaysTrusted,
        ImmutableHashSet<PluginName> NeverTrusted,
        ImmutableHashSet<PluginName> Listed,
        ImmutableHashSet<PluginName> MastersOfListed,
        ImmutableHashSet<PluginName> OwnChangesOnly)
    {
        public static FixedRules Of(
            ModTrustSettings settings, PluginName patch, AlwaysTrusted alwaysTrusted, IReadOnlyDictionary<PluginName, PluginListing> listings)
        {
            var neverTrusted = UsableEntries(settings.NeverTrustedMods, settings.Target, patch);
            var listed = UsableEntries(settings.TrustedModsAndTheirMasters, settings.Target, patch).Except(neverTrusted);
            return new FixedRules(
                alwaysTrusted,
                neverTrusted,
                listed,
                MastersOfAll(listed, listings),
                UsableEntries(settings.ModsTrustedForOwnChangesOnly, settings.Target, patch));
        }

        /// <returns>Null when the automatic rules decide.</returns>
        public TrustDecision? Decide(PluginName plugin)
        {
            if (NeverTrusted.Contains(plugin)) return TrustDecision.Untrusted(TrustReason.NeverTrusted);
            if (AlwaysTrusted.ReasonFor(plugin) is { } alwaysTrustedReason) return TrustDecision.Trusted(alwaysTrustedReason);
            if (Listed.Contains(plugin)) return TrustDecision.Trusted(TrustReason.Listed);
            if (MastersOfListed.Contains(plugin)) return TrustDecision.Trusted(TrustReason.MasterOfListed);
            if (OwnChangesOnly.Contains(plugin)) return new TrustDecision(ModTrust.TrustedForOwnChanges, TrustReason.ListedOwnChangesOnly);
            return null;
        }
    }

    /// <summary>The automatic rules, asked only for the plugins the fixed rules leave undecided.</summary>
    /// <param name="CaughtRequiringMods">The mods that require the cleaned mod within the other-master limit, patch marker or not.</param>
    /// <param name="MastersOfCaughtRequiringMods">Only the masters of those without the patch marker, which the rule skips.</param>
    private sealed record AutomaticRules(
        ImmutableHashSet<PluginName> CaughtRequiringMods,
        ImmutableHashSet<PluginName> MastersOfCaughtRequiringMods,
        ImmutableHashSet<PluginName> RequiringModsOverLimit,
        ImmutableHashSet<PluginName> LoadingBefore,
        ImmutableHashSet<PluginName> CarryingPatchMarker)
    {
        /// <param name="undecidedRequiringMods">The mods that require the cleaned mod and that no earlier rule decided.</param>
        public static AutomaticRules Of(
            LoadOrderPlugins plugins,
            ModTrustSettings settings,
            IReadOnlyDictionary<PluginName, PluginListing> listings,
            ImmutableHashSet<PluginName> undecidedRequiringMods,
            IReadOnlyDictionary<PluginName, int> otherMasterCounts)
        {
            ImmutableHashSet<PluginName> considered = settings.TrustRequiringMods ? undecidedRequiringMods : [];
            var caught = considered.Where(plugin => otherMasterCounts[plugin] <= settings.MaxOtherMastersOfRequiringMod).ToImmutableHashSet();
            var carryingPatchMarker = ModTrustClassifier.CarryingPatchMarker(plugins);
            return new AutomaticRules(
                caught,
                MastersOfAll(caught.Except(carryingPatchMarker), listings),
                considered.Except(caught),
                settings.TrustLoadingBefore ? ModTrustClassifier.LoadingBefore(plugins, settings.Target) : [],
                carryingPatchMarker);
        }

        /// <remarks>
        /// The patch marker decides before the other-master limit: a marked plugin a rule considers is named as another group's patch,
        /// the reason no setting can change.
        /// </remarks>
        public TrustDecision Decide(PluginName plugin)
        {
            var caughtBy = CaughtBy(plugin);
            var overLimit = RequiringModsOverLimit.Contains(plugin);
            if ((caughtBy != null || overLimit) && CarryingPatchMarker.Contains(plugin)) return TrustDecision.Untrusted(TrustReason.OtherGroupPatch);
            if (caughtBy is { } reason) return TrustDecision.Trusted(reason);
            return TrustDecision.Untrusted(overLimit ? TrustReason.RequiringModTooManyMasters : null);
        }

        private TrustReason? CaughtBy(PluginName plugin)
        {
            if (CaughtRequiringMods.Contains(plugin)) return TrustReason.RequiringMod;
            if (MastersOfCaughtRequiringMods.Contains(plugin)) return TrustReason.MasterOfRequiringMod;
            if (LoadingBefore.Contains(plugin)) return TrustReason.LoadsBeforeCleanedMod;
            return null;
        }
    }
}
