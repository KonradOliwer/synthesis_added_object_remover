using System.Collections.Immutable;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>Reads an NPC's raw facts from the records. Thread-safe, like the link cache.</summary>
internal sealed class NpcTraitsReader(ILinkCache linkCache, PluginNames names)
{
    /// <returns>Null when the record is not an NPC.</returns>
    public NpcTraits? Read(RecordKey npcKey)
    {
        var npcFormKey = npcKey.ToFormKey();
        if (MutagenCalls.Read(() => linkCache.TryResolve<INpcGetter>(npcFormKey, out var resolved) ? resolved : null) is not { } npc) return null;

        var stored = MutagenCalls.Read(() => (
            Female: npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Female),
            npc.Height,
            BoundsFirst: npc.ObjectBounds.First,
            BoundsSecond: npc.ObjectBounds.Second,
            Race: npc.Race.FormKey,
            WornArmor: npc.WornArmor.FormKeyNullable));
        var race = MutagenCalls.Read(() => linkCache.TryResolve<IRaceGetter>(stored.Race, out var resolved) ? resolved : null);
        return new NpcTraits(
            stored.Female,
            stored.Height,
            BaseFactsReader.ToBox(stored.BoundsFirst, stored.BoundsSecond),
            names.KeyOf(stored.Race),
            race is null ? null : ReadRace(race),
            race is null ? [] : ReadBodyAddons(stored.WornArmor, race));
    }

    private RaceTraits ReadRace(IRaceGetter race)
    {
        var stored = MutagenCalls.Read(() => (
            race.EditorID,
            Playable: race.Flags.HasFlag(Race.Flag.Playable),
            Male: race.Height.Male,
            Female: race.Height.Female,
            ArmorRace: race.ArmorRace.FormKeyNullable));
        return new(stored.EditorID, stored.Playable, stored.Male, stored.Female, KeyOfNullable(stored.ArmorRace));
    }

    private RecordKey? KeyOfNullable(FormKey? key) => key is { } formKey ? names.KeyOf(formKey) : null;

    /// <param name="wornArmor">The NPC's own worn armor; null or a null key when it has none and wears its race's skin.</param>
    private ImmutableArray<ArmorAddonTraits> ReadBodyAddons(FormKey? wornArmor, IRaceGetter race)
    {
        var armorKey = wornArmor is { IsNull: false } own ? own : MutagenCalls.Read(() => race.Skin.FormKey);
        var addonKeys = MutagenCalls.Read(() =>
            linkCache.TryResolve<IArmorGetter>(armorKey, out var armor) ? armor.Armature.Select(addon => addon.FormKey).ToArray() : null);
        if (addonKeys is null) return [];

        var addons = ImmutableArray.CreateBuilder<ArmorAddonTraits>();
        foreach (var addonKey in addonKeys)
        {
            if (MutagenCalls.Read(() => linkCache.TryResolve<IArmorAddonGetter>(addonKey, out var addon) ? addon : null) is { } addon) addons.Add(ReadAddon(addon));
        }
        return addons.ToImmutable();
    }

    private ArmorAddonTraits ReadAddon(IArmorAddonGetter addon)
    {
        var stored = MutagenCalls.Read(() => (
            FirstPersonFlags: addon.BodyTemplate?.FirstPersonFlags ?? default,
            Race: addon.Race.FormKeyNullable,
            AdditionalRaces: addon.AdditionalRaces.Select(additional => additional.FormKey).ToArray(),
            Male: addon.WorldModel?.Male?.File.GivenPath,
            Female: addon.WorldModel?.Female?.File.GivenPath));
        return new(
            (uint)stored.FirstPersonFlags,
            KeyOfNullable(stored.Race),
            [.. stored.AdditionalRaces.Select(names.KeyOf)],
            stored.Male,
            stored.Female);
    }
}
