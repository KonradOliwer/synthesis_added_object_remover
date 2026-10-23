using System.Collections.Immutable;
using System.Diagnostics;

namespace AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

/// <summary>How far each plugin is trusted, decided once before the load order is scanned.</summary>
/// <param name="CleanedMod">The cleaned mod as the load order spells it.</param>
/// <param name="Patch">This group's output plugin. It has no trust: its values are simply the final ones.</param>
/// <param name="Plugins">
/// Every listed plugin and every master they name, except the patch. Its order is not the load order.
/// </param>
/// <param name="LoadsBeforeCleanedModCount">The number of plugins trusted because they load before the cleaned mod.</param>
public sealed record ModTrustTable(
    PluginName CleanedMod, PluginName Patch, ImmutableDictionary<PluginName, PluginTrust> Plugins, int LoadsBeforeCleanedModCount)
{
    public bool IsPatch(PluginName plugin) => plugin == Patch;

    /// <summary>
    /// The trust of the plugin's version of a record in the accepted-value rule (<see cref="AcceptedVersions"/>): the one mapping of
    /// plugin trust to version trust. The patch is transparent: it holds what earlier patchers of the group wrote, which is never
    /// accepted, and a value it only carries is traced to the plugin it came from.
    /// </summary>
    public VersionTrust VersionTrustOf(PluginName plugin) =>
        IsPatch(plugin)
            ? VersionTrust.Transparent
            : Plugins[plugin].Trust switch
            {
                ModTrust.Trusted => VersionTrust.Full,
                ModTrust.TrustedForOwnChanges => VersionTrust.OwnEdits,
                ModTrust.Untrusted => VersionTrust.None,
                var unknown => throw new UnreachableException($"Unknown trust {unknown}."),
            };
}

/// <param name="Reason">Null for an untrusted plugin that no rule names.</param>
/// <param name="RequiresCleanedMod">Whether the plugin has the cleaned mod as a master, trusted or not.</param>
/// <param name="OtherMasterCount">The plugin's masters other than the base game, the cleaned mod and the cleaned mod's masters.</param>
public sealed record PluginTrust(ModTrust Trust, TrustReason? Reason, bool RequiresCleanedMod, int OtherMasterCount);
