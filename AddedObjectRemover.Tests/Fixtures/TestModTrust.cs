using System.Collections.Immutable;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Hand-made trust tables for the steps that read one, without the classifier's rules.</summary>
internal static class TestModTrust
{
    /// <summary>The cleaned mod is trusted as the cleaned mod whatever the lists say; the patch has no entry.</summary>
    public static ModTrustTable Of(
        PluginName cleanedMod,
        PluginName patch,
        IEnumerable<PluginName> trusted,
        IEnumerable<PluginName> untrusted,
        IEnumerable<PluginName>? trustedForOwnChanges = null)
    {
        var plugins = ImmutableDictionary.CreateBuilder<PluginName, PluginTrust>();
        foreach (var plugin in untrusted) plugins[plugin] = new PluginTrust(ModTrust.Untrusted, Reason: null, RequiresCleanedMod: false, OtherMasterCount: 0);
        foreach (var plugin in trustedForOwnChanges ?? []) plugins[plugin] = new PluginTrust(ModTrust.TrustedForOwnChanges, TrustReason.ListedOwnChangesOnly, RequiresCleanedMod: false, OtherMasterCount: 0);
        foreach (var plugin in trusted) plugins[plugin] = new PluginTrust(ModTrust.Trusted, TrustReason.Listed, RequiresCleanedMod: false, OtherMasterCount: 0);
        plugins[cleanedMod] = new PluginTrust(ModTrust.Trusted, TrustReason.CleanedMod, RequiresCleanedMod: false, OtherMasterCount: 0);
        return new ModTrustTable(cleanedMod, patch, plugins.ToImmutable(), LoadsBeforeCleanedModCount: 0);
    }
}
