using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

/// <param name="Target">The cleaned mod.</param>
/// <param name="TrustRequiringMods">Whether mods that require the cleaned mod are trusted, with their masters.</param>
/// <param name="MaxOtherMastersOfRequiringMod">The most other masters a mod that requires the cleaned mod may have to be trusted.</param>
/// <param name="TrustLoadingBefore">Whether the plugins loading before the cleaned mod are trusted.</param>
public sealed record ModTrustSettings(
    PluginName Target,
    ImmutableArray<PluginName> TrustedModsAndTheirMasters,
    ImmutableArray<PluginName> ModsTrustedForOwnChangesOnly,
    ImmutableArray<PluginName> NeverTrustedMods,
    bool TrustRequiringMods,
    int MaxOtherMastersOfRequiringMod,
    bool TrustLoadingBefore);
