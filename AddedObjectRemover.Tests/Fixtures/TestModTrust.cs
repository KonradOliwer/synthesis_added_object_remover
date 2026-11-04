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
    /// <param name="otherGroupPatches">Added Object Remover patches of other groups, recognised by their marker.</param>
    /// <param name="loadingBefore">The plugins that load before the cleaned mod; by default the base game and the cleaned mod's masters.</param>
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
        IEnumerable<PluginName>? needingCleanedMod = null,
        IEnumerable<PluginName>? loadingBefore = null,
        IEnumerable<PluginName>? otherGroupPatches = null)
    {
        var before = (loadingBefore ?? [.. baseGame ?? [], .. cleanedModMasters ?? []]).ToHashSet();
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
        foreach (var plugin in otherGroupPatches ?? []) plugins[plugin] = Entry(ModTrust.Untrusted, TrustReason.OtherGroupPatch) with { CarriesPatchMarker = true };
        plugins[cleanedMod] = Entry(ModTrust.Trusted, TrustReason.CleanedMod);
        foreach (var plugin in plugins.Keys.ToList()) plugins[plugin] = plugins[plugin] with { LoadsBeforeCleanedMod = before.Contains(plugin) };
        return new ModTrustTable(cleanedMod, patch, plugins.ToImmutable(), LoadsBeforeCleanedModCount: 0);
    }

    private static PluginTrust Entry(
        ModTrust trust, TrustReason? reason, bool requiresCleanedMod = false, bool isMasterOfCleanedMod = false, bool needsCleanedMod = false) =>
        new(trust, reason, requiresCleanedMod, OtherMasterCount: 0, isMasterOfCleanedMod, needsCleanedMod, LoadsBeforeCleanedMod: false, CarriesPatchMarker: false);
}
