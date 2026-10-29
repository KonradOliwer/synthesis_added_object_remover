using System.Collections.Immutable;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Hand-made trust tables for the steps that read one, without the classifier's rules.</summary>
internal static class TestModTrust
{
    /// <summary>The cleaned mod is trusted as the cleaned mod whatever the lists say; the patch has no entry.</summary>
    /// <param name="baseGame">Trusted as the game's own plugins.</param>
    /// <param name="cleanedModMasters">Trusted as the cleaned mod's masters.</param>
    /// <param name="mastersOfTrusted">Trusted only because a trusted mod has them as masters.</param>
    /// <param name="neverTrustedCleanedModMasters">Masters of the cleaned mod listed as never trusted.</param>
    /// <param name="neverTrusted">Plugins listed as never trusted that the cleaned mod does not list as masters.</param>
    /// <param name="needingCleanedMod">Plugins that cannot load without the cleaned mod, trusted for it as the rule for mods that require it trusts them.</param>
    public static ModTrustTable Of(
        PluginName cleanedMod,
        PluginName patch,
        IEnumerable<PluginName> trusted,
        IEnumerable<PluginName> untrusted,
        IEnumerable<PluginName>? trustedForOwnChanges = null,
        IEnumerable<PluginName>? cleanedModMasters = null,
        IEnumerable<PluginName>? mastersOfTrusted = null,
        IEnumerable<PluginName>? baseGame = null,
        IEnumerable<PluginName>? neverTrustedCleanedModMasters = null,
        IEnumerable<PluginName>? neverTrusted = null,
        IEnumerable<PluginName>? needingCleanedMod = null)
    {
        var plugins = ImmutableDictionary.CreateBuilder<PluginName, PluginTrust>();
        foreach (var plugin in neverTrusted ?? []) plugins[plugin] = Entry(ModTrust.Untrusted, TrustReason.NeverTrusted);
        foreach (var plugin in needingCleanedMod ?? []) plugins[plugin] = Entry(ModTrust.Trusted, TrustReason.RequiringMod, requiresCleanedMod: true, needsCleanedMod: true);
        foreach (var plugin in neverTrustedCleanedModMasters ?? []) plugins[plugin] = Entry(ModTrust.Untrusted, TrustReason.NeverTrusted, isMasterOfCleanedMod: true);
        foreach (var plugin in untrusted) plugins[plugin] = Entry(ModTrust.Untrusted, reason: null);
        foreach (var plugin in trustedForOwnChanges ?? []) plugins[plugin] = Entry(ModTrust.TrustedForOwnChanges, TrustReason.ListedOwnChangesOnly);
        foreach (var plugin in mastersOfTrusted ?? []) plugins[plugin] = Entry(ModTrust.Trusted, TrustReason.MasterOfListed);
        foreach (var plugin in baseGame ?? []) plugins[plugin] = Entry(ModTrust.Trusted, TrustReason.BaseGame);
        foreach (var plugin in cleanedModMasters ?? []) plugins[plugin] = Entry(ModTrust.Trusted, TrustReason.CleanedModMaster, isMasterOfCleanedMod: true);
        foreach (var plugin in trusted) plugins[plugin] = Entry(ModTrust.Trusted, TrustReason.Listed);
        plugins[cleanedMod] = Entry(ModTrust.Trusted, TrustReason.CleanedMod);
        return new ModTrustTable(cleanedMod, patch, plugins.ToImmutable(), LoadsBeforeCleanedModCount: 0);
    }

    private static PluginTrust Entry(
        ModTrust trust, TrustReason? reason, bool requiresCleanedMod = false, bool isMasterOfCleanedMod = false, bool needsCleanedMod = false) =>
        new(trust, reason, requiresCleanedMod, OtherMasterCount: 0, isMasterOfCleanedMod, needsCleanedMod);
}
