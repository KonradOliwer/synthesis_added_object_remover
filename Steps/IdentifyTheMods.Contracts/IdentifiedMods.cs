using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

/// <summary>Which plugins' objects never make a target object too close, decided before the load order is scanned.</summary>
/// <param name="TargetMasters">Every master of the target plugin, ignored or not.</param>
/// <param name="IgnoredMasters">The target's masters that are ignored: all of them or none.</param>
public sealed record IdentifiedMods(
    PluginName Target,
    IReadOnlySet<PluginName> IgnoredOrigins,
    ImmutableArray<PluginName> TargetMasters,
    ImmutableArray<PluginName> IgnoredMasters,
    DetectedPatches Patches);
