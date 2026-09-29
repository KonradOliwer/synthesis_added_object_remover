using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>One in-memory plugin of the fixture, with helpers that add base records and return their FormKeys.</summary>
internal sealed class FixtureMod
{
    public const SkyrimRelease Release = SkyrimRelease.SkyrimSE;

    /// <param name="masters">Set explicitly, because in-memory mods do not compute their master list.</param>
    public FixtureMod(string fileName, params FixtureMod[] masters)
    {
        Mod = new SkyrimMod(ModKey.FromNameAndExtension(fileName), Release);
        foreach (var master in masters) Mod.ModHeader.MasterReferences.Add(new MasterReference { Master = master.Key });
    }

    public SkyrimMod Mod { get; }

    public ModKey Key => Mod.ModKey;

    /// <summary>The plugin's copy of the interior cell; set when the shared spaces are added.</summary>
    public FixtureCell Interior { get; set; } = null!;

    /// <summary>The plugin's copy of the exterior cell; set when the shared spaces are added.</summary>
    public FixtureCell Exterior { get; set; } = null!;

    public FormKey NextKey() => Mod.GetNextFormKey();

    public FormKey AddModeledStatic(string editorId, string model)
    {
        var record = new Static(NextKey(), Release) { EditorID = editorId, Model = new Model { File = model } };
        Mod.Statics.Add(record);
        return record.FormKey;
    }

    /// <summary>A static without a model, so it is measured by its Object Bounds.</summary>
    public FormKey AddBoundsOnlyStatic(string editorId, Box bounds)
    {
        var record = new Static(NextKey(), Release) { EditorID = editorId, ObjectBounds = ToObjectBounds(bounds) };
        Mod.Statics.Add(record);
        return record.FormKey;
    }

    public FormKey AddMarkerStatic(string editorId)
    {
        var record = new Static(NextKey(), Release) { EditorID = editorId, MajorFlags = Static.MajorFlag.IsMarker };
        Mod.Statics.Add(record);
        return record.FormKey;
    }

    public FormKey AddModeledDoor(string editorId, string model)
    {
        var record = new Door(NextKey(), Release) { EditorID = editorId, Model = new Model { File = model } };
        Mod.Doors.Add(record);
        return record.FormKey;
    }

    public FormKey AddModelessLight(string editorId)
    {
        var record = new Light(NextKey(), Release) { EditorID = editorId };
        Mod.Lights.Add(record);
        return record.FormKey;
    }

    public FormKey AddSoundMarker(string editorId)
    {
        var record = new SoundMarker(NextKey(), Release) { EditorID = editorId };
        Mod.SoundMarkers.Add(record);
        return record.FormKey;
    }

    /// <summary>A non-playable race, so an NPC of it is sized by its Object Bounds.</summary>
    public FormKey AddRace(string editorId)
    {
        var record = Fixtures.TestNpcs.CreateRace(NextKey(), skin: null, playable: false, armorRace: null);
        record.EditorID = editorId;
        Mod.Races.Add(record);
        return record.FormKey;
    }

    public FormKey AddNpc(string editorId, FormKey race, Box bounds)
    {
        var record = Fixtures.TestNpcs.CreateNpc(NextKey(), race, female: false, template: null);
        record.EditorID = editorId;
        record.ObjectBounds = ToObjectBounds(bounds);
        Mod.Npcs.Add(record);
        return record.FormKey;
    }

    public FormList AddFormList(string editorId)
    {
        var record = new FormList(NextKey(), Release) { EditorID = editorId };
        Mod.FormLists.Add(record);
        return record;
    }

    private static ObjectBounds ToObjectBounds(Box box) => new()
    {
        First = new P3Int16((short)box.Min.X, (short)box.Min.Y, (short)box.Min.Z),
        Second = new P3Int16((short)box.Max.X, (short)box.Max.Y, (short)box.Max.Z),
    };
}
