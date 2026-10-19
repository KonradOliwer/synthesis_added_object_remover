using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Synthesis.Settings;
using Newtonsoft.Json;

namespace AddedObjectRemover;

public class Settings : IModeSettingsValues
{
    [SynthesisOrder]
    [SynthesisSettingName("Mode")]
    [SynthesisTooltip(
        "Standard: clean up the one mod chosen under \"Standard\".\n"
        + "Batch: clean up several mods in one run, as listed in the group file named under \"Batch\".")]
    public PatchMode Mode { get; set; } = PatchMode.Standard;

    [SynthesisOrder]
    [SynthesisSettingName("Detailed log and reports")]
    [SynthesisTooltip(
        "Lists every removed object and why in the log, plus timings and statistics, and saves CSV report files for checking the results.\n"
        + "The report files go to the \"Reports\" folder in this patcher's own folder in Synthesis; the log shows the full path.\n"
        + "Applies to Standard and Batch mode. Does not change the patch.")]
    public bool DetailedLogAndReports { get; set; }

    [SynthesisOrder]
    [SynthesisSettingName("Batch")]
    [SynthesisTooltip("Cleaning up several mods in one run. Not used in Standard mode.")]
    public BatchSettings Batch { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName("Standard")]
    [SynthesisTooltip("The settings for cleaning up one mod. Not used in Batch mode.")]
    public StandardSettings Standard { get; set; } = new();

    ISettingsValues IModeSettingsValues.Standard => Standard ?? new StandardSettings();
    string? IModeSettingsValues.GroupFileName => (Batch ?? new BatchSettings()).GroupFileName;
}

public class BatchSettings
{
    [SynthesisOrder]
    [SynthesisSettingName("Group file name")]
    [SynthesisTooltip(
        "The name of the group file in the folder \"synthesis_added_object_remover\\groups\" of the game's Data folder, with or without \".json\".\n"
        + "It lists, in run order, the config files to run; each is in \"synthesis_added_object_remover\\configs\".")]
    public string GroupFileName { get; set; } = string.Empty;
}

public class StandardSettings : ISettingsValues
{
    [SynthesisOrder]
    [SynthesisSettingName("Mod to clean up")]
    [SynthesisTooltip(
        "The plugin whose added objects are checked. Its objects that clash with other mods are removed in this patch.\n"
        + "Nothing is deleted: a removed object is disabled; one that is not persistent is also moved below the world.\n"
        + "Always kept, whatever the settings: teleport doors and their destinations; anything another mod's object, a quest, a package,\n"
        + "a scene, a location or a navmesh points to; objects whose own script names a quest or scene, or whose base object\n"
        + "is this plugin's own and has a script that does; objects with a location role (except map markers). Linked objects are always removed or kept together.\n"
        + "Pick it from your load order.")]
    [JsonConverter(typeof(EmptyTextAsNullConverter))]
    public ModKey TargetPlugin { get; set; } = ModKey.Null;

    [SynthesisOrder]
    [SynthesisSettingName("Other mods that never cause removals")]
    [SynthesisTooltip(
        "Objects and terrain changes from these mods are never a reason to remove the cleaned mod's objects.\n"
        + "The base game and its official DLC never count either.")]
    public IgnoreSettings WhatToIgnore { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName("Remove objects too close to other mods' objects")]
    [SynthesisTooltip(
        "The main check. An object of the cleaned mod is removed when an object from another mod comes within a margin around it.\n"
        + "Turn on one or both margins; an object caught by either is removed once. With both off, nothing is removed for being too close.")]
    public TooCloseRemovalSettings TooClose { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName("Remove objects on terrain changed by other mods")]
    [SynthesisTooltip(
        "Removes an object of the cleaned mod when another mod changed the terrain under or around it: raised, lowered or flattened it,\n"
        + "changed the kind of ground (for example grass to road), or raised, lowered, added or removed the water there,\n"
        + "so the object may float, sink, stand under water or look out of place.\n"
        + "Off by default. Interiors and worlds without terrain are not checked.")]
    public TerrainChangeSettings TerrainChange { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName("Also remove what rests on removed objects")]
    [SynthesisTooltip(
        "When an object is removed, the cleaned mod's objects resting on it or touching it would be left floating. This removes them too.\n"
        + "Only the cleaned mod's own visible objects are removed this way.")]
    public FollowUpRemovalSettings FollowUpRemoval { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName("Invisible objects left behind")]
    [SynthesisTooltip(
        "The cleaned mod's sounds, lights, markers, spawners and similar invisible objects are removed when they end up inside\n"
        + "another mod's object (a building, a boulder) or when the visible objects around them were removed.")]
    public LeftoverInvisibleObjectSettings LeftoverInvisibleObjects { get; set; } = new();

    string? ISettingsValues.TargetPlugin => TargetPlugin.IsNull ? null : TargetPlugin.ToString();
    IIgnoreSettingsValues ISettingsValues.WhatToIgnore => WhatToIgnore ?? new IgnoreSettings();
    ITooCloseSettingsValues ISettingsValues.TooClose => TooClose ?? new TooCloseRemovalSettings();
    ITerrainChangeSettingsValues ISettingsValues.TerrainChange => TerrainChange ?? new TerrainChangeSettings();
    IFollowUpSettingsValues ISettingsValues.FollowUpRemoval => FollowUpRemoval ?? new FollowUpRemovalSettings();
    ILeftoverSettingsValues ISettingsValues.LeftoverInvisibleObjects => LeftoverInvisibleObjects ?? new LeftoverInvisibleObjectSettings();
}

public class IgnoreSettings : IIgnoreSettingsValues
{
    [SynthesisOrder]
    [SynthesisSettingName("Never count these mods")]
    [SynthesisTooltip("Add plugins whose objects and terrain changes should never cause removals, for example Creation Club plugins.")]
    [JsonProperty(ItemConverterType = typeof(EmptyTextAsNullConverter))]
    public List<ModKey> ExcludedPlugins { get; set; } = [];

    [SynthesisOrder]
    [SynthesisSettingName("Never count the mods the cleaned mod requires")]
    [SynthesisTooltip("The plugins the cleaned mod lists as its masters were made to fit with it.")]
    public bool IgnoreTargetMasters { get; set; } = true;

    [SynthesisOrder]
    [SynthesisSettingName("Compatibility patches")]
    [SynthesisTooltip(
        "A compatibility patch is a plugin that requires both the cleaned mod and another mod. The other mod was already made to fit,\n"
        + "so it never counts. The log lists each patch found.")]
    public CompatibilityPatchSettings CompatibilityPatches { get; set; } = new();

    IReadOnlyList<string> IIgnoreSettingsValues.ExcludedPlugins =>
        ExcludedPlugins?.Where(plugin => !plugin.IsNull).Select(plugin => plugin.ToString()).ToArray() ?? [];

    bool IIgnoreSettingsValues.IgnoreModsPatchedWithTarget => (CompatibilityPatches ?? new()).IgnoreModsPatchedWithTarget;
    int IIgnoreSettingsValues.MaxOtherMastersForPatch => (CompatibilityPatches ?? new()).MaxOtherMastersForPatch;
}

public class CompatibilityPatchSettings
{
    [SynthesisOrder]
    [SynthesisSettingName("Never count mods that have a compatibility patch with it")]
    [SynthesisTooltip(
        "If a plugin requires both the cleaned mod and another mod, it is treated as a compatibility patch:\n"
        + "that other mod was already made to fit, so it never counts.")]
    public bool IgnoreModsPatchedWithTarget { get; set; } = true;

    [SynthesisOrder]
    [SynthesisSettingName("A patch requires at most this many other mods")]
    [SynthesisTooltip(
        "Plugins that require more other mods than this (for example a merged or Bashed Patch) are not treated as compatibility patches.\n"
        + "The base game and the cleaned mod's masters are not counted. 1-100. Only used when the switch above is on.")]
    public int MaxOtherMastersForPatch { get; set; } = SettingDefaults.MaxOtherMastersForPatch;
}

internal static class MarginTexts
{
    public const string UseName = "Use this margin";
    public const string UseTooltip = "Turns this margin on or off. When both margins of the group are on, the group acts when either one finds something.";
}

/// <summary>
/// One margin around an object, grown from its size; the look shared by the groups that check an area around each object.
/// Each group has its own subtype that supplies the group's defaults, so a partly written or null group in settings.json
/// still falls back to that group's defaults.
/// </summary>
public abstract class MarginBySizeSettings(bool use, float sizeMultiplier)
{
    [SynthesisOrder]
    [SynthesisSettingName(MarginTexts.UseName)]
    [SynthesisTooltip(MarginTexts.UseTooltip)]
    public bool Use { get; set; } = use;

    [SynthesisOrder]
    [SynthesisSettingName("Margin on every side (× object size)")]
    [SynthesisTooltip(
        "A multiple of the object's own size, added on every side. 0.5 adds half the object's size on every side,\n"
        + "so the checked area is twice as large as the object; 1 adds a whole size on every side (three times as large).\n"
        + "Bigger objects get a bigger margin. 0-5.")]
    public float SizeMultiplier { get; set; } = sizeMultiplier;
}

/// <summary>One margin around an object, a fixed distance; see <see cref="MarginBySizeSettings"/> for the per-group subtypes.</summary>
public abstract class MarginByDistanceSettings(bool use, float distanceInUnits)
{
    [SynthesisOrder]
    [SynthesisSettingName(MarginTexts.UseName)]
    [SynthesisTooltip(MarginTexts.UseTooltip)]
    public bool Use { get; set; } = use;

    [SynthesisOrder]
    [SynthesisSettingName("Margin on every side (game units)")]
    [SynthesisTooltip("The same margin for every object, whatever its size, added on every side. 0 adds no margin. 0-1024.")]
    public float DistanceInUnits { get; set; } = distanceInUnits;
}

public sealed class TooCloseBySizeSettings() : MarginBySizeSettings(SettingDefaults.RemoveTooCloseBySize, SettingDefaults.SizeMultiplier);

public sealed class TooCloseByDistanceSettings() : MarginByDistanceSettings(SettingDefaults.RemoveTooCloseByDistance, SettingDefaults.RemovalDistanceInUnits);

public sealed class TerrainBySizeSettings() : MarginBySizeSettings(SettingDefaults.TerrainAreaBySize, SettingDefaults.SizeMultiplier);

public sealed class TerrainByDistanceSettings() : MarginByDistanceSettings(SettingDefaults.TerrainAreaByDistance, SettingDefaults.TerrainAreaDistanceInUnits);

public class TooCloseRemovalSettings : ITooCloseSettingsValues
{
    [SynthesisOrder]
    [SynthesisSettingName("Margin by object size")]
    [SynthesisTooltip("Bigger objects get a bigger margin. An object is removed when another mod's object comes within this margin around it.")]
    public TooCloseBySizeSettings BySize { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName("Margin by fixed distance")]
    [SynthesisTooltip("Every object gets the same margin. An object is removed when another mod's object comes within this margin around it.")]
    public TooCloseByDistanceSettings ByDistance { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName("Use rough boxes instead of exact shapes (faster)")]
    [SynthesisTooltip(
        "Off: the object's real shape, enlarged by the margin, is compared with the other object's real shape.\n"
        + "On: the object's box, enlarged by the margin, is compared with the other object's centre point. Faster, but two large\n"
        + "objects that overlap only at their edges can be missed.\n"
        + "Applies to both margins.")]
    public bool UseRoughBoxes { get; set; } = SettingDefaults.UseRoughBoxes;

    [SynthesisOrder]
    [SynthesisSettingName("Other mods' NPCs and creatures cause removal")]
    [SynthesisTooltip(
        "AlwaysLikeAnyObject: they count like any other object.\n"
        + "OnlyWhenStuckInsideTheObject: only when their body sinks into the object or they stand inside it (a boulder, a room of a building);\n"
        + "standing on or leaning against it does not count.\n"
        + "Never: they never cause removal.")]
    public NpcHandling NpcHandling { get; set; } = SettingDefaults.Npcs;

    bool ITooCloseSettingsValues.BySize => (BySize ?? new()).Use;
    float ITooCloseSettingsValues.SizeMultiplier => (BySize ?? new()).SizeMultiplier;
    bool ITooCloseSettingsValues.ByDistance => (ByDistance ?? new()).Use;
    float ITooCloseSettingsValues.DistanceInUnits => (ByDistance ?? new()).DistanceInUnits;
    NpcHandling ITooCloseSettingsValues.Npcs => NpcHandling;
}

public class TerrainChangeSettings : ITerrainChangeSettingsValues
{
    [SynthesisOrder]
    [SynthesisSettingName("Remove objects on changed terrain")]
    [SynthesisTooltip("Switches this removal on.")]
    public bool RemoveOnChangedTerrain { get; set; } = SettingDefaults.RemoveOnChangedTerrain;

    [SynthesisOrder]
    [SynthesisSettingName("Which of the cleaned mod's objects are checked")]
    [SynthesisTooltip(
        "VisibleObjectsOnTheGround: visible objects that reach the ground (objects on roofs, bridges or high shelves are skipped).\n"
        + "VisibleObjectsAtAnyHeight: every visible object.\n"
        + "AllObjectsIncludingInvisibleOnes: also markers, lights and sounds.")]
    public TerrainCheckedObjects TerrainCheckedObjects { get; set; } = SettingDefaults.TerrainChecked;

    [SynthesisOrder]
    [SynthesisSettingName("Margin by object size")]
    [SynthesisTooltip(
        "The terrain under the object plus this margin around it is checked for height, ground type and water changes.\n"
        + "Bigger objects get a bigger margin. The terrain points right around the object are always checked.")]
    public TerrainBySizeSettings BySize { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName("Margin by fixed distance")]
    [SynthesisTooltip(
        "The terrain under the object plus this margin around it is checked for height, ground type and water changes.\n"
        + "Every object gets the same margin. The terrain points right around the object are always checked.")]
    public TerrainByDistanceSettings ByDistance { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName("Count height changes")]
    [SynthesisTooltip("Whether a change of the ground's height counts.")]
    public bool CountHeightChanges { get; set; } = SettingDefaults.CountHeightChanges;

    [SynthesisOrder]
    [SynthesisSettingName("Ignore height changes up to (game units)")]
    [SynthesisTooltip(
        "Small smoothing edits are common and rarely visible under an object. 0 counts every change. 0-256.\n"
        + "Only used when \"Count height changes\" is on.")]
    public float HeightChangeTolerance { get; set; } = SettingDefaults.HeightChangeTolerance;

    [SynthesisOrder]
    [SynthesisSettingName("Count ground type changes (for example grass to road)")]
    [SynthesisTooltip("The kind of ground: grass, dirt, stone, snow and so on. One grass texture replaced by another is not a change.")]
    public bool CountTerrainTypeChanges { get; set; } = SettingDefaults.CountTerrainTypeChanges;

    [SynthesisOrder]
    [SynthesisSettingName("Remove only when this share of the ground points changed type (%)")]
    [SynthesisTooltip(
        "A ground type change removes an object only when at least this share of the terrain points checked around it changed type.\n"
        + "Small touch-ups at the edges of painted patches then don't remove objects. 0 removes on any one changed point. 0-100.\n"
        + "Height changes are not affected. Only used when \"Count ground type changes\" is on.")]
    public int MinTerrainTypeChangePercent { get; set; } = SettingDefaults.MinTerrainTypeChangePercent;

    [SynthesisOrder]
    [SynthesisSettingName("Count water height changes")]
    [SynthesisTooltip(
        "Whether another mod raising or lowering a cell's water counts, when that puts ground around the object under water\n"
        + "or out of it. Water over ground that stays covered is not seen. A cell whose default water height is unknown is not checked.")]
    public bool CountWaterHeightChanges { get; set; } = SettingDefaults.CountWaterHeightChanges;

    [SynthesisOrder]
    [SynthesisSettingName("Ignore water height changes up to (game units)")]
    [SynthesisTooltip(
        "Small water level edits rarely matter. 0 counts every change. 0-256.\n"
        + "Only used when \"Count water height changes\" is on.")]
    public float WaterHeightChangeTolerance { get; set; } = SettingDefaults.WaterHeightChangeTolerance;

    [SynthesisOrder]
    [SynthesisSettingName("Count water added/removed changes")]
    [SynthesisTooltip(
        "Whether another mod adding water to a cell or taking its water away counts, when that puts ground around the object\n"
        + "under water or out of it. Water over ground that stays covered is not seen. A cell whose default water height is unknown is not checked.")]
    public bool CountWaterAddedOrRemoved { get; set; } = SettingDefaults.CountWaterAddedOrRemoved;

    [SynthesisOrder]
    [SynthesisSettingName("Remove only when this share of the ground got covered/uncovered (%)")]
    [SynthesisTooltip(
        "A water change removes an object only when at least this share of the terrain points checked around it went under water\n"
        + "or came out of it. A point that changed only because the ground itself was raised or lowered is left to the height check.\n"
        + "0 removes on any one such point. 0-100. Only used when one of the two water switches above is on.")]
    public int MinWaterCoverChangePercent { get; set; } = SettingDefaults.MinWaterCoverChangePercent;

    bool ITerrainChangeSettingsValues.AreaBySize => (BySize ?? new()).Use;
    float ITerrainChangeSettingsValues.AreaSizeMultiplier => (BySize ?? new()).SizeMultiplier;
    bool ITerrainChangeSettingsValues.AreaByDistance => (ByDistance ?? new()).Use;
    float ITerrainChangeSettingsValues.AreaDistanceInUnits => (ByDistance ?? new()).DistanceInUnits;
}

public class FollowUpRemovalSettings : IFollowUpSettingsValues
{
    [SynthesisOrder]
    [SynthesisSettingName("When an object is removed, also remove")]
    [SynthesisTooltip(
        "Nothing: only the objects found too close or on changed terrain.\n"
        + "EverythingTouchingIt: everything connected to it through touching objects, spreading one object at a time; this can reach a whole room.\n"
        + "OnlyWhatLosesItsSupport: a touching object only when enough of what it rests on was removed.")]
    public FollowUpRemovalMode Mode { get; set; } = SettingDefaults.FollowUpMode;

    [SynthesisOrder]
    [SynthesisSettingName("Gap that still counts as touching (game units)")]
    [SynthesisTooltip("Two objects this close or closer count as touching. 0-64. Not used with \"Nothing\".")]
    public float TouchDistance { get; set; } = SettingDefaults.TouchDistance;

    [SynthesisOrder]
    [SynthesisSettingName("OnlyWhatLosesItsSupport: remove when this much support is gone (%)")]
    [SynthesisTooltip("The ground and every plugin's objects count as support. 1-100.")]
    public float AnchoringThresholdPercent { get; set; } = SettingDefaults.AnchoringThresholdPercent;
}

public class LeftoverInvisibleObjectSettings : ILeftoverSettingsValues
{
    [SynthesisOrder]
    [SynthesisSettingName("Remove invisible objects left inside other mods' objects or alone")]
    [SynthesisTooltip("Turns this removal on. Works with any \"also remove\" choice.")]
    public bool RemoveLeftoverInvisibleObjects { get; set; } = true;

    [SynthesisOrder]
    [SynthesisSettingName("When the cleaned mod's visible objects around it were removed")]
    [SynthesisTooltip(
        "For each invisible object, the cleaned mod's visible objects around it (never other mods' objects) are sorted into 8 directions\n"
        + "(north, north-east, ...) and measured by the ground area they cover. Objects removed by the earlier steps count as removed.\n"
        + "The invisible object is removed when enough of those directions were mostly cleared. The percentages are applied in the order listed, in steps of 10.")]
    public SurroundingObjectsSettings SurroundingObjects { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName("Never remove these as left behind (ticked = kept)")]
    [SynthesisTooltip(
        "Ticked kinds are kept by this step (unless linked to a removed object); unticked kinds can be removed by it. "
        + "Only this left-behind step is affected, not the too-close or changed-terrain removals.\n"
        + "What \"Mod to clean up\" lists as always kept stays kept, ticked or not.")]
    public KeptInvisibleObjectSettings AlwaysKeep { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName("Move kept markers out of other mods' objects")]
    [SynthesisTooltip(
        "A kept map, X, idle or other marker that sits inside another mod's object is moved to the nearest free spot (up to 2048 units).\n"
        + "Only its position changes. Lights, sounds, furniture and door markers are never moved. Needs the switch at the top of this group.")]
    public bool MoveKeptMarkersOutOfOtherModsObjects { get; set; }

    float ILeftoverSettingsValues.SearchRadius => (SurroundingObjects ?? new()).SearchRadius;
    int ILeftoverSettingsValues.OccupiedDirectionsPercent => (SurroundingObjects ?? new()).OccupiedDirectionsPercent;
    int ILeftoverSettingsValues.DirectionThresholdPercent => (SurroundingObjects ?? new()).DirectionThresholdPercent;
    int ILeftoverSettingsValues.RemovedDirectionsPercent => (SurroundingObjects ?? new()).RemovedDirectionsPercent;
    IReadOnlyList<InvisibleObjectKind> ILeftoverSettingsValues.NeverRemove => (AlwaysKeep ?? new()).Kinds();
}

public class SurroundingObjectsSettings
{
    [SynthesisOrder]
    [SynthesisSettingName("Look this far around it (game units)")]
    [SynthesisTooltip("Visible objects of the cleaned mod within this distance are its surroundings. A light, sound or trigger box that reaches less far uses its own reach. 64-8192.")]
    public float SearchRadius { get; set; } = SettingDefaults.SearchRadius;

    [SynthesisOrder]
    [SynthesisSettingName("Decide only if this share of the 8 directions has objects (%)")]
    [SynthesisTooltip("A direction has objects when a visible object of the cleaned mod lies that way. With fewer such directions the invisible object is kept. 10-100, in steps of 10.")]
    public int OccupiedDirectionsPercent { get; set; } = SettingDefaults.OccupiedDirectionsPercent;

    [SynthesisOrder]
    [SynthesisSettingName("A direction is cleared when this much of it was removed (%)")]
    [SynthesisTooltip("Measured by the ground area the cleaned mod's visible objects cover in that direction. 10-100, in steps of 10.")]
    public int DirectionThresholdPercent { get; set; } = SettingDefaults.DirectionThresholdPercent;

    [SynthesisOrder]
    [SynthesisSettingName("Remove when this share of those directions is cleared (%)")]
    [SynthesisTooltip("Counted among the directions that have objects. 10-100, in steps of 10.")]
    public int RemovedDirectionsPercent { get; set; } = SettingDefaults.RemovedDirectionsPercent;
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
        + "or on a base object the mod to clean up defines.")]
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
