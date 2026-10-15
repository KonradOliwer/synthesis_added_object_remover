using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

/// <summary>
/// How the load order's plugins stand to the target plugin, decided before the load order is scanned: which never count as
/// clashing with it, its masters, the plugins that depend on it and the base game.
/// </summary>
/// <param name="IgnoredOrigins">The plugins that never count as clashing: their objects never make a target object too close, and they never count as changing its terrain.</param>
/// <param name="TargetMasters">Every master of the target plugin, ignored or not.</param>
/// <param name="IgnoredMasters">The target's masters that are ignored: all of them or none.</param>
/// <param name="Dependants">Every loaded plugin, other than the patch plugin, that lists the target as a master, in load order.</param>
/// <param name="BaseGame">The base game plugins present (updates and DLC, no Creation Club content), in load order.</param>
public sealed record IdentifiedMods(
    PluginName Target,
    IReadOnlySet<PluginName> IgnoredOrigins,
    ImmutableArray<PluginName> TargetMasters,
    ImmutableArray<PluginName> IgnoredMasters,
    DetectedPatches Patches,
    ImmutableArray<PluginName> Dependants,
    ImmutableArray<PluginName> BaseGame);
