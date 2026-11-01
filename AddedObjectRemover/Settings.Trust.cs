using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Synthesis.Settings;
using Newtonsoft.Json;

namespace AddedObjectRemover;

public class TrustSettings : ITrustSettingsValues
{
    [SynthesisOrder]
    [SynthesisSettingName("Trusted mods, and their masters")]
    [SynthesisTooltip(
        "Everything these plugins and their masters contain is accepted, even values they only carry over, everywhere.\n"
        + "For example, list a compatibility patch made for the cleaned mod and a city mod: the city mod's wall standing where the cleaned mod puts a cart\n"
        + "no longer removes the cart.")]
    [JsonProperty(ItemConverterType = typeof(EmptyTextAsNullConverter))]
    public List<ModKey> TrustedModsAndTheirMasters { get; set; } = [];

    [SynthesisOrder]
    [SynthesisSettingName("Mods trusted for their own changes only")]
    [SynthesisTooltip(
        "A record these plugins add is accepted in full. In other records, only the values that differ from the plugin loading just before them are accepted.\n"
        + "Their masters are not affected. For example, a bird-sound mod's new sound markers are accepted, but a wall it only carries over from a city mod is not.")]
    [JsonProperty(ItemConverterType = typeof(EmptyTextAsNullConverter))]
    public List<ModKey> ModsTrustedForOwnChangesOnly { get; set; } = [];

    [SynthesisOrder]
    [SynthesisSettingName("Never trusted mods")]
    [SynthesisTooltip(
        "These plugins can always cause removals, even when they are listed above, require the cleaned mod, load before it, or are its masters.\n"
        + "For example, another Synthesis patcher's output. The cleaned mod and the patch cannot be listed.")]
    [JsonProperty(ItemConverterType = typeof(EmptyTextAsNullConverter))]
    public List<ModKey> NeverTrustedMods { get; set; } = [];

    [SynthesisOrder]
    [SynthesisSettingName("Also trust mods that require the cleaned mod, and their masters")]
    [SynthesisTooltip(
        "A plugin that has the cleaned mod as a master (such as a compatibility patch) was made to fit it, so it is trusted like a listed mod,\n"
        + "masters included: CityMod - CleanedMod Patch.esp makes CityMod.esp trusted. An Added Object Remover patch from another Synthesis group\n"
        + "is never trusted this way.")]
    public bool TrustModsRequiringCleanedModAndTheirMasters { get; set; } = true;

    [SynthesisOrder]
    [SynthesisSettingName("...only if it has at most this many other masters")]
    [SynthesisTooltip(
        "Other masters are those besides the cleaned mod, the base game and the cleaned mod's masters: a patch requiring Skyrim.esm, the cleaned mod\n"
        + "and CityMod.esp has 1. Mods with more (a merged plugin, a Bashed Patch) are not trusted, and the log lists them.\n"
        + "0 trusts only mods with no other masters. 0-100.")]
    public int MaxOtherMastersOfRequiringModToTrust { get; set; } = SettingDefaults.MaxOtherMastersOfRequiringModToTrust;

    [SynthesisOrder]
    [SynthesisSettingName("Trust mods loading before the cleaned mod")]
    [SynthesisTooltip(
        "The plugins above it in your plugin list, Creation Club content included; the cleaned mod was usually made to fit them.\n"
        + "Off: they can cause removals, except the always-trusted ones.")]
    public bool TrustModsLoadingBeforeCleanedMod { get; set; } = true;

    IReadOnlyList<string> ITrustSettingsValues.TrustedModsAndTheirMasters => NamesOf(TrustedModsAndTheirMasters);
    IReadOnlyList<string> ITrustSettingsValues.ModsTrustedForOwnChangesOnly => NamesOf(ModsTrustedForOwnChangesOnly);
    IReadOnlyList<string> ITrustSettingsValues.NeverTrustedMods => NamesOf(NeverTrustedMods);

    /// <summary>The settings screen saves a plugin picker left unset as an empty entry, which is no plugin.</summary>
    private static IReadOnlyList<string> NamesOf(List<ModKey>? plugins) =>
        plugins?.Where(plugin => !plugin.IsNull).Select(plugin => plugin.ToString()).ToArray() ?? [];
}
