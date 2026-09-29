using System.Numerics;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>In-memory race, NPC and leveled NPC records, and other mods' placed NPCs.</summary>
internal static class TestNpcs
{
    public static Race CreateRace(FormKey formKey, FormKey? skin, bool playable, FormKey? armorRace) =>
        new(formKey, SkyrimRelease.SkyrimSE)
        {
            Flags = playable ? Race.Flag.Playable : default,
            Height = new GenderedItem<float>(1f, 1f),
            Skin = new FormLinkNullable<IArmorGetter>(skin),
            ArmorRace = new FormLinkNullable<IRaceGetter>(armorRace),
        };

    /// <param name="template">A spawn the NPC takes its traits from.</param>
    public static Mutagen.Bethesda.Skyrim.Npc CreateNpc(FormKey formKey, FormKey race, bool female, FormKey? template)
    {
        var npc = new Mutagen.Bethesda.Skyrim.Npc(formKey, SkyrimRelease.SkyrimSE)
        {
            Race = new FormLink<IRaceGetter>(race),
            Height = 1f,
            Template = new FormLinkNullable<INpcSpawnGetter>(template),
        };
        if (female) npc.Configuration.Flags |= NpcConfiguration.Flag.Female;
        if (template != null) npc.Configuration.TemplateFlags |= NpcConfiguration.TemplateFlag.Traits;
        return npc;
    }

    public static LeveledNpc CreateLeveledList(FormKey formKey, params FormKey[] spawns) =>
        new(formKey, SkyrimRelease.SkyrimSE)
        {
            Entries = new ExtendedList<LeveledNpcEntry>(spawns.Select(spawn => new LeveledNpcEntry
            {
                Data = new LeveledNpcEntryData { Reference = new FormLink<INpcSpawnGetter>(spawn), Level = 1, Count = 1 },
            })),
        };

    public static OtherObject Place(ModKey mod, int index, FormKey npc, Vector3 position, P3Float rotation) =>
        new(new OtherId(index), new FormKey(mod, 0x900 + (uint)index), TestTargets.Space, mod, EditorId: null, new BaseRef(npc, typeof(INpcGetter)), position, rotation, 1f,
            IsPrimitive: false, HasMapMarker: false);
}
