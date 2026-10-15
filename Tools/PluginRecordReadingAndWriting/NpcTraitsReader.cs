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
        if (!linkCache.TryResolve<INpcGetter>(npcKey.ToFormKey(), out var npc)) return null;

        linkCache.TryResolve<IRaceGetter>(npc.Race.FormKey, out var race);
        return new NpcTraits(
            npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Female),
            npc.Height,
            BaseFactsReader.ToBox(npc.ObjectBounds),
            names.KeyOf(npc.Race.FormKey),
            race is null ? null : ReadRace(race),
            race is null ? [] : ReadBodyAddons(npc, race));
    }

    private RaceTraits ReadRace(IRaceGetter race) =>
        new(race.EditorID, race.Flags.HasFlag(Race.Flag.Playable), race.Height.Male, race.Height.Female, KeyOfNullable(race.ArmorRace.FormKeyNullable));

    private RecordKey? KeyOfNullable(FormKey? key) => key is { } formKey ? names.KeyOf(formKey) : null;

    private ImmutableArray<ArmorAddonTraits> ReadBodyAddons(INpcGetter npc, IRaceGetter race)
    {
        var armorKey = npc.WornArmor.IsNull ? race.Skin.FormKey : npc.WornArmor.FormKey;
        if (!linkCache.TryResolve<IArmorGetter>(armorKey, out var armor)) return [];

        var addons = ImmutableArray.CreateBuilder<ArmorAddonTraits>();
        foreach (var addonLink in armor.Armature)
        {
            if (linkCache.TryResolve<IArmorAddonGetter>(addonLink.FormKey, out var addon)) addons.Add(ReadAddon(addon));
        }
        return addons.ToImmutable();
    }

    private ArmorAddonTraits ReadAddon(IArmorAddonGetter addon) =>
        new(
            (uint)(addon.BodyTemplate?.FirstPersonFlags ?? default),
            KeyOfNullable(addon.Race.FormKeyNullable),
            [.. addon.AdditionalRaces.Select(additional => names.KeyOf(additional.FormKey))],
            addon.WorldModel?.Male?.File.GivenPath,
            addon.WorldModel?.Female?.File.GivenPath);
}
