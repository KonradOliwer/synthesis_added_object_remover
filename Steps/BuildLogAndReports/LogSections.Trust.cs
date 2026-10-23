using System.Collections.Immutable;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    private const string OtherGroupPatchLabel = "Added Object Remover patch of another group";

    /// <summary>The trust settings as set; list entries as the settings spell them, found in the plugin list or not.</summary>
    private static string DescribeTrustSettings(ModTrustSettings trust) =>
        "Which other mods can cause removals: "
        + $"Trusted mods, and their masters: {JoinPluginsOrNone(trust.TrustedModsAndTheirMasters)}; "
        + $"Mods trusted for their own changes only: {JoinPluginsOrNone(trust.ModsTrustedForOwnChangesOnly)}; "
        + $"Never trusted mods: {JoinPluginsOrNone(trust.NeverTrustedMods)}; "
        + $"Also trust mods that require the cleaned mod, and their masters: {OnWith(trust.TrustRequiringMods, $"only if it has at most {OtherMasters(trust.MaxOtherMastersOfRequiringMod)}")}; "
        + $"Trust mods loading before the cleaned mod: {OnOff(trust.TrustLoadingBefore)}.";

    private static string JoinPluginsOrNone(ImmutableArray<PluginName> plugins) => JoinOrNone(plugins.Select(plugin => plugin.ToString()));

    private static string OtherMasters(int count) => TextFormat.Count(count, "other master", "other masters");

    /// <summary>
    /// One line per trust reason that names a plugin, plugins in load order. The masters a plugin made trusted follow it in
    /// parentheses; the plugins trusted for loading before the cleaned mod are only counted.
    /// </summary>
    /// <param name="limit">The most other masters a mod that requires the cleaned mod may have to be trusted.</param>
    private static ImmutableArray<string> DescribeTrustReasons(PluginName cleanedMod, int limit, ModTrustTable trust, LoadOrderPlugins plugins)
    {
        var inLoadOrder = ClassifiedInLoadOrder(trust, plugins);
        ImmutableArray<string> Plain(string label, TrustReason reason) => TrustReasonLine(label, PluginsWith(reason, inLoadOrder, trust));
        ImmutableArray<string> WithMasters(string label, TrustReason reason, TrustReason masterReason) =>
            TrustReasonLine(label, PluginsWithTheirMasters(reason, masterReason, inLoadOrder, trust, plugins));
        return
        [
            .. Plain("Trusted, base game", TrustReason.BaseGame),
            .. Plain($"Trusted, masters of {cleanedMod}", TrustReason.CleanedModMaster),
            .. WithMasters("Trusted, listed with masters", TrustReason.Listed, TrustReason.MasterOfListed),
            .. WithMasters($"Trusted, requires {cleanedMod}", TrustReason.RequiringMod, TrustReason.MasterOfRequiringMod),
            .. Plain("Trusted for own changes only, listed", TrustReason.ListedOwnChangesOnly),
            .. Plain("Never trusted, listed", TrustReason.NeverTrusted),
            .. DescribeRequiringModsWithTooManyMasters(cleanedMod, limit, inLoadOrder, trust),
            .. DescribePatchesOfOtherGroups(cleanedMod, inLoadOrder, trust, plugins),
            .. DescribeLoadingBefore(cleanedMod, trust),
        ];
    }

    /// <summary>The classified plugins in load order; masters that are not in the load order come last, by name.</summary>
    private static ImmutableArray<PluginName> ClassifiedInLoadOrder(ModTrustTable trust, LoadOrderPlugins plugins)
    {
        var listed = plugins.Listed.Select(listing => listing.Mod).Where(trust.Plugins.ContainsKey).Distinct().ToList();
        return [.. listed, .. trust.Plugins.Keys.Except(listed).OrderBy(plugin => plugin.FileName, StringComparer.OrdinalIgnoreCase)];
    }

    private static ImmutableArray<string> PluginsWith(TrustReason reason, ImmutableArray<PluginName> inLoadOrder, ModTrustTable trust) =>
        [.. inLoadOrder.Where(plugin => trust.Plugins[plugin].Reason == reason).Select(plugin => plugin.ToString())];

    /// <summary>Each plugin with the masters it made trusted, such as "A.esp (masters: B.esm, C.esm)".</summary>
    private static ImmutableArray<string> PluginsWithTheirMasters(
        TrustReason reason, TrustReason masterReason, ImmutableArray<PluginName> inLoadOrder, ModTrustTable trust, LoadOrderPlugins plugins) =>
    [
        .. inLoadOrder
            .Where(plugin => trust.Plugins[plugin].Reason == reason)
            .Select(plugin => DescribeWithMasters(plugin, MastersWith(masterReason, plugin, trust, plugins))),
    ];

    /// <remarks>The patch can be a plugin's master; it has no trust entry.</remarks>
    private static ImmutableArray<PluginName> MastersWith(TrustReason reason, PluginName plugin, ModTrustTable trust, LoadOrderPlugins plugins) =>
        [.. (plugins.Find(plugin)?.Masters ?? []).Where(master => trust.Plugins.TryGetValue(master, out var entry) && entry.Reason == reason).Distinct()];

    private static string DescribeWithMasters(PluginName plugin, ImmutableArray<PluginName> masters) =>
        masters.IsEmpty ? plugin.ToString() : $"{plugin} (masters: {string.Join(Describe.ListSeparator, masters)})";

    /// <summary>One line per number of other masters, so each line names the number that is over the limit.</summary>
    private static ImmutableArray<string> DescribeRequiringModsWithTooManyMasters(
        PluginName cleanedMod, int limit, ImmutableArray<PluginName> inLoadOrder, ModTrustTable trust) =>
    [
        .. inLoadOrder
            .Where(plugin => trust.Plugins[plugin].Reason == TrustReason.RequiringModTooManyMasters)
            .GroupBy(plugin => trust.Plugins[plugin].OtherMasterCount)
            .Select(group =>
                $"Requires {cleanedMod} but not trusted, {OtherMasters(group.Key)} (limit {TextFormat.Count(limit)}): {string.Join(Describe.ListSeparator, group)}"),
    ];

    /// <summary>
    /// The Added Object Remover patches of other groups, which both automatic rules skip, by the fact that would have trusted them:
    /// requiring the cleaned mod, else loading before it, else being a master of a trusted mod that requires it.
    /// </summary>
    private static ImmutableArray<string> DescribePatchesOfOtherGroups(
        PluginName cleanedMod, ImmutableArray<PluginName> inLoadOrder, ModTrustTable trust, LoadOrderPlugins plugins)
    {
        var byFact = inLoadOrder
            .Where(plugin => trust.Plugins[plugin].Reason == TrustReason.OtherGroupPatch)
            .ToLookup(plugin => FactAbout(plugin, cleanedMod, trust, plugins), plugin => plugin.ToString());
        return
        [
            .. TrustReasonLine($"Requires {cleanedMod} but not trusted, {OtherGroupPatchLabel}", [.. byFact[OtherGroupPatchFact.Requires]]),
            .. TrustReasonLine($"Loads before {cleanedMod} but not trusted, {OtherGroupPatchLabel}", [.. byFact[OtherGroupPatchFact.LoadsBefore]]),
            .. TrustReasonLine(
                $"Master of a mod that requires {cleanedMod} but not trusted, {OtherGroupPatchLabel}", [.. byFact[OtherGroupPatchFact.MasterOfRequiringMod]]),
        ];
    }

    private enum OtherGroupPatchFact { Requires, LoadsBefore, MasterOfRequiringMod }

    /// <remarks>A patch of another group that no automatic rule would have trusted has no trust reason, so it is not asked about.</remarks>
    private static OtherGroupPatchFact FactAbout(PluginName plugin, PluginName cleanedMod, ModTrustTable trust, LoadOrderPlugins plugins)
    {
        if (trust.Plugins[plugin].RequiresCleanedMod) return OtherGroupPatchFact.Requires;
        var positions = plugins.Positions;
        return positions.Contains(plugin) && positions.Of(plugin) < positions.Of(cleanedMod) ? OtherGroupPatchFact.LoadsBefore : OtherGroupPatchFact.MasterOfRequiringMod;
    }

    private static ImmutableArray<string> DescribeLoadingBefore(PluginName cleanedMod, ModTrustTable trust) =>
        trust.LoadsBeforeCleanedModCount == 0
            ? []
            : [$"Trusted, loading before {cleanedMod}: {TextFormat.Count(trust.LoadsBeforeCleanedModCount, "mod", "mods")}"];

    private static ImmutableArray<string> TrustReasonLine(string label, ImmutableArray<string> plugins) =>
        plugins.IsEmpty ? [] : [$"{label}: {string.Join(Describe.ListSeparator, plugins)}"];
}
