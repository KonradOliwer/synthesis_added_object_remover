using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using Mutagen.Bethesda.Synthesis.Settings;

namespace AddedObjectRemover;

public class LeftoverInvisibleObjectSettings : IInvisibleLeftoverSettingsValues
{
    [SynthesisOrder]
    [SynthesisSettingName("Remove invisible objects left inside other mods' objects or alone")]
    [SynthesisTooltip("Turns this removal on. Works with any \"also remove\" choice.")]
    public bool RemoveLeftoverInvisibleObjects { get; set; } = true;

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.SurroundingObjects)]
    [SynthesisTooltip(
        "For each invisible object, the cleaned mod's visible objects around it (the cleaned mod's objects, including other mods' objects it changed) are sorted into 8 directions\n"
        + "(north, north-east, ...) and measured by the ground area they cover. Objects removed by the earlier steps count as removed.\n"
        + "The invisible object is removed when enough of those directions were mostly cleared. The settings are applied in the order listed; the percentages go in steps of 10.")]
    public SurroundingObjectsSettings SurroundingObjects { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName("Never remove these as invisible objects left behind (ticked = kept)")]
    [SynthesisTooltip(
        "Ticked kinds are kept by this step (unless linked to a removed object); unticked kinds can be removed by it. "
        + "They are also kept by the area method of \"Objects left behind\", even while this step is switched off. "
        + "The too-close and changed-terrain removals are not affected; \"Also remove what rests on removed objects\" can still remove a ticked kind.\n"
        + "What \"Mod to clean up\" lists as always kept stays kept, ticked or not.")]
    public KeptInvisibleObjectSettings AlwaysKeep { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName("Move kept markers out of other mods' objects")]
    [SynthesisTooltip(
        "A kept map, X, idle or other marker that sits inside another mod's object is moved to the nearest free spot (up to 2048 units).\n"
        + "Only its position changes. Lights, sounds, furniture and door markers are never moved. Needs the switch at the top of this group.")]
    public bool MoveKeptMarkersOutOfOtherModsObjects { get; set; } = true;

    ISurroundingObjectsSettingsValues IInvisibleLeftoverSettingsValues.SurroundingObjects => SurroundingObjects ?? new();
    IReadOnlyList<InvisibleObjectKind> IInvisibleLeftoverSettingsValues.NeverRemove => (AlwaysKeep ?? new()).Kinds();
}

public class SurroundingObjectsSettings : ISurroundingObjectsSettingsValues
{
    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.SearchRadius + SettingNames.GameUnitsSuffix)]
    [SynthesisTooltip("Visible objects of the cleaned mod within this distance are its surroundings. A light, sound or trigger box that reaches less far uses its own reach. 64-8192.")]
    public float SearchRadius { get; set; } = SettingDefaults.SearchRadius;

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.MinOccupiedDirections)]
    [SynthesisTooltip("A direction has objects when a visible object of the cleaned mod lies that way. With fewer such directions the invisible object is kept. 1-8.")]
    public int MinOccupiedDirections { get; set; } = SettingDefaults.MinOccupiedDirections;

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.DirectionThreshold + SettingNames.PercentSuffix)]
    [SynthesisTooltip("Measured by the ground area the cleaned mod's visible objects cover in that direction. 10-100, in steps of 10.")]
    public int DirectionThresholdPercent { get; set; } = SettingDefaults.DirectionThresholdPercent;

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.ClearedDirections + SettingNames.PercentSuffix)]
    [SynthesisTooltip(
        "Counted only among the directions that have objects, not among all 8. Example: objects lie in 5 directions and 3 of them are cleared: "
        + "3 of 5 is 60%, so it is removed with 60 or less and kept with 70 or more. 10-100, in steps of 10.")]
    public int ClearedDirectionsPercent { get; set; } = SettingDefaults.ClearedDirectionsPercent;
}

public class KeptInvisibleObjectSettings
{
    [SynthesisOrder]
    [SynthesisSettingName("Map markers")]
    [SynthesisTooltip(
        "The icons on the world map. A removed one is disabled where it stands, so it is hidden from the map.\n"
        + "Markers a location or quest uses are always kept.")]
    public bool MapMarkers { get; set; }

    [SynthesisOrder]
    [SynthesisSettingName("Position markers (XMarker, XMarkerHeading)")]
    [SynthesisTooltip(
        "Invisible objects without a model that mark a position, or a position and a direction (XMarker and XMarkerHeading in the editor).\n"
        + "Scripts, quests and other objects use them as a place to stand, face or move to; those a quest, a script or another mod's object\n"
        + "points to are always kept.")]
    public bool XMarkers { get; set; }

    [SynthesisOrder]
    [SynthesisSettingName("Idle animation spots")]
    [SynthesisTooltip(
        "Spots where a character plays an idle animation, such as leaning on a wall or warming hands at a fire (idle marker objects).\n"
        + "Disabling one makes the characters who use it stop doing that there.")]
    public bool IdleMarkers { get; set; }

    [SynthesisOrder]
    [SynthesisSettingName("Furniture use spots")]
    [SynthesisTooltip(
        "Invisible furniture that marks where a character sits, sleeps, leans or works (furniture flagged as a marker).\n"
        + "Disabling one removes that spot; characters scheduled to use it may stand or walk about instead.")]
    public bool FurnitureMarkers { get; set; }

    [SynthesisOrder]
    [SynthesisSettingName("Invisible marker doors")]
    [SynthesisTooltip(
        "Invisible doors flagged as markers in the editor that lead nowhere. Doors that teleport are always kept, and so are\n"
        + "the doors they lead to; the spot a door puts you at is part of the door itself, not a separate object.")]
    public bool DoorMarkers { get; set; }

    [SynthesisOrder]
    [SynthesisSettingName("Lights without a model")]
    [SynthesisTooltip(
        "Light sources with no visible model, or whose model is only a glow effect.\n"
        + "Disabling one makes that spot darker.")]
    public bool Lights { get; set; }

    [SynthesisOrder]
    [SynthesisSettingName("Sound sources")]
    [SynthesisTooltip(
        "Invisible points that play a sound, such as wind, water or an ambient hum (sound markers).\n"
        + "Disabling one silences that sound at that place.")]
    public bool SoundMarkers { get; set; }

    [SynthesisOrder]
    [SynthesisSettingName("Echo areas")]
    [SynthesisTooltip(
        "Areas that change how sound echoes or is muffled, as in caves and halls (acoustic spaces).\n"
        + "Disabling one makes that area sound like the space around it.")]
    public bool AcousticSpaces { get; set; }

    [SynthesisOrder]
    [SynthesisSettingName("Critter spawners")]
    [SynthesisTooltip(
        "Invisible markers that make small animals such as birds, butterflies or fish appear. They are recognised by their critter spawn script.\n"
        + "Disabling one removes the animals it would spawn.")]
    public bool CritterSpawners { get; set; }

    [SynthesisOrder]
    [SynthesisSettingName("Invisible trigger areas")]
    [SynthesisTooltip(
        "Invisible boxes without a model that start something when entered or left, such as a script, a quest stage or a trap.\n"
        + "Disabling one can stop whatever it starts. Always kept: those with a script that names a quest or scene, on the trigger itself\n"
        + "or on a base object the cleaned mod defines.")]
    public bool TriggerBoxes { get; set; }

    [SynthesisOrder]
    [SynthesisSettingName("Surface decals (dirt, blood)")]
    [SynthesisTooltip(
        "Marks painted onto a surface, such as dirt or blood (objects placed from a texture set).\n"
        + "Cosmetic only: disabling one removes the mark.")]
    public bool Decals { get; set; }

    [SynthesisOrder]
    [SynthesisSettingName("Other invisible objects")]
    [SynthesisTooltip(
        "Every other object without visible geometry that is none of the kinds above, such as a marker-flagged activator\n"
        + "or an object whose model is empty. Their purpose varies, so disabling one can affect whatever uses it.")]
    public bool OtherMarkers { get; set; }

    /// <summary>The ticked kinds, in the order of <see cref="InvisibleObjectKind"/>.</summary>
    public IReadOnlyList<InvisibleObjectKind> Kinds() =>
        KindFlags().Where(flag => flag.Ticked).Select(flag => flag.Kind).ToArray();

    private IEnumerable<(InvisibleObjectKind Kind, bool Ticked)> KindFlags() =>
    [
        (InvisibleObjectKind.MapMarkers, MapMarkers),
        (InvisibleObjectKind.XMarkers, XMarkers),
        (InvisibleObjectKind.IdleMarkers, IdleMarkers),
        (InvisibleObjectKind.FurnitureMarkers, FurnitureMarkers),
        (InvisibleObjectKind.DoorMarkers, DoorMarkers),
        (InvisibleObjectKind.OtherMarkers, OtherMarkers),
        (InvisibleObjectKind.Lights, Lights),
        (InvisibleObjectKind.SoundMarkers, SoundMarkers),
        (InvisibleObjectKind.AcousticSpaces, AcousticSpaces),
        (InvisibleObjectKind.CritterSpawners, CritterSpawners),
        (InvisibleObjectKind.TriggerBoxes, TriggerBoxes),
        (InvisibleObjectKind.Decals, Decals),
    ];
}
