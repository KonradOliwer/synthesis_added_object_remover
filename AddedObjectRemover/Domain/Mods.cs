using System.Collections.Immutable;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>A plugin's position in <see cref="ModTable"/>.</summary>
internal readonly record struct ModRef(int Index);

/// <summary>
/// Every plugin a run knows of: the listed load order, the patch plugin, then masters that are not
/// listed, in the order first seen. Built once; a <see cref="ModRef"/> is only valid for its table.
/// </summary>
internal sealed class ModTable
{
    private readonly ImmutableArray<ModKey> _keys;
    private readonly Dictionary<ModKey, ModRef> _refs;

    public ModTable(IEnumerable<ModKey> keys)
    {
        _keys = [.. keys.Distinct()];
        _refs = Enumerable.Range(0, _keys.Length).ToDictionary(index => _keys[index], index => new ModRef(index));
    }

    public ModRef? Find(ModKey key) => _refs.TryGetValue(key, out var mod) ? mod : null;

    /// <summary>For plugins the table was built with.</summary>
    public ModRef RefOf(ModKey key) => _refs[key];

    public ModKey KeyOf(ModRef mod) => _keys[mod.Index];
}

/// <param name="Loaded">False when the plugin is listed but could not be read.</param>
/// <param name="Masters">Empty when the plugin could not be read.</param>
internal sealed record ModListing(ModRef Mod, bool Loaded, ImmutableArray<ModRef> Masters);

/// <summary>The load order as read once, before the settings are checked.</summary>
/// <param name="Listed">The listed plugins, lowest priority first, the patch plugin included.</param>
internal sealed record ModFacts(ModTable Mods, ModRef PatchMod, ImmutableArray<ModListing> Listed)
{
    public ModListing? Find(ModRef mod) => Listed.FirstOrDefault(listing => listing.Mod == mod);
}

/// <summary>A plugin that masters both the target plugin and at least one other, non-base-game mod.</summary>
/// <param name="OtherMasters">The other, non-base-game masters it links the target with.</param>
internal sealed record CompatibilityPatch(ModRef Patch, ImmutableArray<ModRef> OtherMasters);

/// <param name="Detected">Plugins treated as compatibility patches.</param>
/// <param name="SkippedTooManyMasters">Plugins that master the target but too many other mods to be a patch, such as generated or merged plugins.</param>
/// <param name="PluginsMasteringTarget">Every plugin that masters the target, patch or not.</param>
internal sealed record PatchReport(
    bool DetectionOn,
    ImmutableArray<CompatibilityPatch> Detected,
    ImmutableArray<CompatibilityPatch> SkippedTooManyMasters,
    int PluginsMasteringTarget)
{
    public static PatchReport Off { get; } = new(false, [], [], 0);

    /// <summary>Every mod ignored because a detected patch links it to the target: the patches themselves and their other masters.</summary>
    public IEnumerable<ModRef> IgnoredMods => Detected.SelectMany(patch => patch.OtherMasters.Prepend(patch.Patch)).Distinct();
}

/// <param name="Excluded">The excluded plugins that the table knows of.</param>
/// <param name="PatchMasterLimit">The most other masters a compatibility patch may have.</param>
internal sealed record StandingOptions(ModRef Target, ImmutableArray<ModRef> Excluded, bool IgnoreTargetMasters, bool IgnorePatched, int PatchMasterLimit);

/// <summary>Which plugins' objects never make a target object too close, decided before the load order is scanned.</summary>
/// <param name="TargetMasters">Every master of the target plugin, ignored or not.</param>
/// <param name="IgnoredMasters">The target's masters that are ignored: all of them or none.</param>
internal sealed record ModStanding(
    ModRef Target,
    IReadOnlySet<ModRef> IgnoredOrigins,
    ImmutableArray<ModRef> TargetMasters,
    ImmutableArray<ModRef> IgnoredMasters,
    PatchReport Patches);
