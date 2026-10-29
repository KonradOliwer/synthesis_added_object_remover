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
    /// Whether a rule names the plugin itself as trusted: listed, requiring the cleaned mod, or loading before it. Not the base game,
    /// the cleaned mod, a plugin trusted only as a master, one trusted for its own changes only, an untrusted one, the patch or an
    /// unknown one.
    /// </summary>
    public bool IsTrustedItself(PluginName plugin) =>
        Plugins.TryGetValue(plugin, out var trust)
        && trust is { Trust: ModTrust.Trusted, Reason: TrustReason.Listed or TrustReason.RequiringMod or TrustReason.LoadsBeforeCleanedMod };

    /// <summary>Whether the plugin is one of the game's own (Skyrim.esm, Update.esm and the three DLCs), a master of the cleaned mod or not.</summary>
    public bool IsBaseGame(PluginName plugin) => Plugins.TryGetValue(plugin, out var trust) && trust.Reason == TrustReason.BaseGame;

    /// <summary>Whether the cleaned mod lists the plugin as a master; false for the patch and an unknown plugin.</summary>
    public bool IsMasterOfCleanedMod(PluginName plugin) => Plugins.TryGetValue(plugin, out var trust) && trust.IsMasterOfCleanedMod;

    /// <summary>Whether the user listed the plugin under the never trusted mods, which wins over every other rule.</summary>
    public bool IsNeverTrusted(PluginName plugin) => Plugins.TryGetValue(plugin, out var trust) && trust.Reason == TrustReason.NeverTrusted;

    /// <summary>
    /// The world the cleaned mod's objects are judged in (<see cref="AcceptedVersions"/>): the one mapping of plugin trust to version
    /// trust. A value the patch only carries is traced to the plugin it came from.
    /// </summary>
    public AcceptedWorld AcceptedWorld { get; } = new(Patch, Plugins.ToImmutableDictionary(entry => entry.Key, entry => VersionTrustOf(entry.Value.Trust)));

    /// <summary>
    /// The world without the cleaned mod: what the game shows when the cleaned mod is not installed. Every plugin that loads without it
    /// is believed, whatever the trust settings say; the cleaned mod and the plugins that cannot load without it are not.
    /// </summary>
    public AcceptedWorld AcceptedWorldWithoutCleanedMod { get; } = new(
        Patch,
        Plugins.ToImmutableDictionary(entry => entry.Key, entry => entry.Key == CleanedMod || entry.Value.NeedsCleanedMod ? VersionTrust.None : VersionTrust.Full));

    private static VersionTrust VersionTrustOf(ModTrust trust) =>
        trust switch
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
/// <param name="IsMasterOfCleanedMod">Whether the cleaned mod lists the plugin as a master, whatever rule trusts it.</param>
/// <param name="NeedsCleanedMod">Whether the plugin cannot load without the cleaned mod: it has it as a master, directly or through another plugin that needs it.</param>
public sealed record PluginTrust(
    ModTrust Trust, TrustReason? Reason, bool RequiresCleanedMod, int OtherMasterCount, bool IsMasterOfCleanedMod, bool NeedsCleanedMod);
