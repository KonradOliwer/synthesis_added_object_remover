using System.Collections.Immutable;
using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;
using static AddedObjectRemover.SettingCorrections;

namespace AddedObjectRemover;

/// <summary>Builds the trust settings from the lists of plugins and the limit the user set.</summary>
internal static class TrustSettingsBuilder
{
    internal const int MaxOtherMastersLimit = 100;

    private const string TrustedModsName = SettingPaths.TrustGroup + SettingNames.TrustedMods;
    private const string OwnChangesModsName = SettingPaths.TrustGroup + SettingNames.OwnChangesMods;
    private const string NeverTrustedModsName = SettingPaths.TrustGroup + SettingNames.NeverTrustedMods;
    private const string MaxOtherMastersName = SettingPaths.TrustGroup + SettingNames.MaxOtherMasters;

    /// <remarks>The limit is checked only when the rule for mods that require the cleaned mod is on; it is not used otherwise.</remarks>
    public static ModTrustSettings Create(
        IPluginRecords records, LoadOrderPlugins mods, PluginName target, ITrustSettingsValues trust, ICollection<SettingWarning> warnings)
    {
        var trusted = ParseListedPlugins(records, mods, trust.TrustedModsAndTheirMasters, TrustedModsName, warnings);
        var ownChangesOnly = ParseListedPlugins(records, mods, trust.ModsTrustedForOwnChangesOnly, OwnChangesModsName, warnings);
        var neverTrusted = ParseListedPlugins(records, mods, trust.NeverTrustedMods, NeverTrustedModsName, warnings);
        WarnOfUnlistableNeverTrusted(neverTrusted, target, mods.PatchMod, warnings);
        var requiringModsTrusted = trust.TrustModsRequiringCleanedModAndTheirMasters;
        return new ModTrustSettings(
            target,
            trusted,
            ownChangesOnly,
            neverTrusted,
            requiringModsTrusted,
            requiringModsTrusted
                ? Clamp(trust.MaxOtherMastersOfRequiringModToTrust, 0, MaxOtherMastersLimit, MaxOtherMastersName, warnings)
                : trust.MaxOtherMastersOfRequiringModToTrust,
            trust.TrustModsLoadingBeforeCleanedMod);
    }

    /// <summary>
    /// A listed plugin that is not in the plugin list changes nothing; it stays listed and is reported. The patch may have no entry
    /// in the plugin list, so it is never reported as missing.
    /// </summary>
    private static ImmutableArray<PluginName> ParseListedPlugins(
        IPluginRecords records, LoadOrderPlugins mods, IReadOnlyList<string> names, string listName, ICollection<SettingWarning> warnings)
    {
        ImmutableArray<PluginName> listed = [.. names.Select(name => LoadOrderChecks.ParsePluginName(records, name))];
        foreach (var plugin in listed.Where(plugin => !mods.Positions.Contains(plugin) && plugin != mods.PatchMod))
        {
            warnings.Add(new SettingWarning(listName, $"Warning: {listName}: {plugin} is not in your plugin list."));
        }
        return listed;
    }

    /// <summary>Such entries have no effect: the cleaned mod is always trusted, and the patch is never classified.</summary>
    private static void WarnOfUnlistableNeverTrusted(
        ImmutableArray<PluginName> neverTrusted, PluginName target, PluginName patch, ICollection<SettingWarning> warnings)
    {
        foreach (var plugin in neverTrusted)
        {
            if (plugin == target)
            {
                warnings.Add(new SettingWarning(
                    NeverTrustedModsName,
                    $"Warning: {NeverTrustedModsName}: {plugin} is the mod to clean up, which cannot be listed here; the entry has no effect."));
            }
            else if (plugin == patch)
            {
                warnings.Add(new SettingWarning(
                    NeverTrustedModsName,
                    $"Warning: {NeverTrustedModsName}: {plugin} is the patch this patcher writes, which cannot be listed here; the entry has no effect."));
            }
        }
    }
}
