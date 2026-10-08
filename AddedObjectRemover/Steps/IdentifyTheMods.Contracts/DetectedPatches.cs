using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

/// <summary>A plugin that masters both the target plugin and at least one other, non-base-game mod.</summary>
/// <param name="OtherMasters">The other, non-base-game masters it links the target with.</param>
public sealed record CompatibilityPatch(PluginName Patch, ImmutableArray<PluginName> OtherMasters);

/// <param name="Detected">Plugins treated as compatibility patches.</param>
/// <param name="SkippedTooManyMasters">Plugins that master the target but too many other mods to be a patch, such as generated or merged plugins.</param>
/// <param name="PluginsMasteringTarget">Every plugin that masters the target, patch or not.</param>
public sealed record DetectedPatches(
    bool DetectionOn,
    ImmutableArray<CompatibilityPatch> Detected,
    ImmutableArray<CompatibilityPatch> SkippedTooManyMasters,
    int PluginsMasteringTarget)
{
    public static DetectedPatches Off { get; } =new(false, [], [], 0);

    /// <summary>Every mod ignored because a detected patch links it to the target: the patches themselves and their other masters.</summary>
    public IEnumerable<PluginName> IgnoredMods => Detected.SelectMany(patch => patch.OtherMasters.Prepend(patch.Patch)).Distinct();
}
