using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
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
    [SynthesisSettingName(RunSettings.OnlyWarnSettingName)]
    [SynthesisTooltip(
        "Such a mod below the patch in your plugin list overrides what the patch writes, so the run normally stops; moving the patch below it fixes that.\n"
        + "On: one warning per mod, and the cleaned mod's objects it overrides are left unchanged.")]
    public bool OnlyWarnWhenModRequiringCleanedModLoadsAfterPatch { get; set; }

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

public class StandardSettings : ISettingsValues, ICollisionSettingsValues
{
    [SynthesisOrder]
    [SynthesisSettingName("Mod to clean up")]
    [SynthesisTooltip(
        "The cleaned mod: the plugin whose added objects are checked. Those that clash with other mods are removed in the patch, this Synthesis group's output plugin.\n"
        + "Nothing is deleted: a removed object is disabled; one that is not persistent is also moved below the world.\n"
        + "Always kept, whatever the settings: teleport doors and their destinations; anything another mod's object, a quest, a package,\n"
        + "a scene, a location or a navmesh points to; objects whose own script names a quest or scene, or whose base object\n"
        + "is this plugin's own and has a script that does; objects with a location role (except map markers); with \"Other mods' objects left without support\"\n"
        + "on KeepSupportingObject, also the objects another mod's object rests on. Linked objects are always removed or kept together.\n"
        + "Pick it from your load order.")]
    [JsonConverter(typeof(EmptyTextAsNullConverter))]
    // The settings key is CleanedMod (the user's word); the code keeps its target names.
    public ModKey CleanedMod { get; set; } = ModKey.Null;

    [SynthesisOrder]
    [SynthesisSettingName("Other mods' objects the cleaned mod changed")]
    [SynthesisTooltip(
        "The cleaned mod can override an object of the base game, of one of its masters or of another mod: move it, or give it another model.\n"
        + "These settings decide how such an object is checked and what happens when it would be removed. The cleaned mod's own objects are not affected.")]
    public OtherModsChangedObjectsSettings ChangedObjects { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName("Which other mods can cause removals")]
    [SynthesisTooltip(
        "Only untrusted mods' objects and terrain changes can remove the cleaned mod's objects.\n"
        + "Always trusted: the base game (Skyrim.esm, Update.esm and the three DLCs), the cleaned mod, and its masters (the plugins it requires).")]
    public TrustSettings Trust { get; set; } = new();

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
        + "Interiors and worlds without terrain are not checked.\n"
        + "Its two \"Count only ... larger than\" values also decide which ground and water height changes\n"
        + "\"Restore what the cleaned mod changed where it was removed\" puts back: only those larger than them.")]
    public TerrainChangeSettings TerrainChange { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName("Shrink or turn objects to make them fit")]
    [SynthesisTooltip(
        "An object of the cleaned mod that would be removed for being too close to another mod's object\n"
        + "is first tried shrunk or turned where it stands, when \"Shrink or turn objects that are too close\" is on. If it fits, it is kept that way; otherwise it is removed.\n"
        + "Only the cleaned mod's own objects follow this group. Other mods' objects the cleaned mod changed follow \"Attempt resizing before removal\" under \"Other mods' objects the cleaned mod changed\".\n"
        + "Never shrunk or turned, but removed as before: an object on terrain another mod changed; an object that another object of the cleaned mod\n"
        + "rests on from above and would lose its support without (objects that only touch it from the side do not count; with \"Nothing\" in\n"
        + "\"Also remove what rests on removed objects\", none counts); and, whatever the settings, an object\n"
        + "that another mod's object rests on (of the mods \"Other mods' objects left without support\" names).\n"
        + "Invisible objects, objects that are always kept and linked objects are never shrunk or turned.")]
    public ShrinkOrTurnSettings ShrinkOrTurn { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName("Also remove what rests on removed objects")]
    [SynthesisTooltip(
        "When an object is removed by any removal step (too close, changed terrain, visible objects left behind), the cleaned mod's objects resting on it or touching it would be left floating. This removes them too.\n"
        + "Only the cleaned mod's own visible objects are removed this way; the last setting of this group decides about other mods' objects.")]
    public FollowUpRemovalSettings FollowUpRemoval { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName("Visible objects left behind")]
    [SynthesisTooltip(
        "The cleaned mod's visible objects are removed when most of the cleaned mod's visible objects around them were removed by the other removal steps\n"
        + "(too close, changed terrain, also remove). Only the cleaned mod's objects, including other mods' objects it changed, count as surroundings.")]
    public LeftoverVisibleObjectSettings LeftoverVisibleObjects { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName("Invisible objects left behind")]
    [SynthesisTooltip(
        "The cleaned mod's sounds, lights, markers, spawners and similar invisible objects are removed when they end up inside\n"
        + "another mod's object (a building, a boulder) or when the visible objects around them were removed.")]
    public LeftoverInvisibleObjectSettings LeftoverInvisibleObjects { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName("Restore what the cleaned mod changed where it was removed")]
    [SynthesisTooltip(
        "Where the cleaned mod's objects were removed from an area, the area's other changes by the cleaned mod go back to how the game showed them without it:\n"
        + "other mods' objects it moved, turned, hid or replaced, the terrain, the cells' settings and the navmeshes.\n"
        + "What a later plugin or the patch changed again stays, unless the patch only repeats the cleaned mod's value; so does anything that would not fit the objects that stay.")]
    public RestoreWhereRemovedSettings RestoreWhereRemoved { get; set; } = new();

    string? ISettingsValues.CleanedMod => CleanedMod.IsNull ? null : CleanedMod.ToString();
    IChangedObjectsSettingsValues ISettingsValues.ChangedObjects => ChangedObjects ?? new OtherModsChangedObjectsSettings();
    ITrustSettingsValues ISettingsValues.Trust => Trust ?? new TrustSettings();
    ICollisionSettingsValues ISettingsValues.Collision => this;
    ITooCloseSettingsValues ICollisionSettingsValues.TooClose => TooClose ?? new TooCloseRemovalSettings();
    ITerrainChangeSettingsValues ICollisionSettingsValues.TerrainChange => TerrainChange ?? new TerrainChangeSettings();
    IShrinkOrTurnSettingsValues ICollisionSettingsValues.ShrinkOrTurn => ShrinkOrTurn ?? new ShrinkOrTurnSettings();
    IFollowUpSettingsValues ISettingsValues.FollowUpRemoval => FollowUpRemoval ?? new FollowUpRemovalSettings();
    IVisibleLeftoverSettingsValues ISettingsValues.LeftoverVisibleObjects => LeftoverVisibleObjects ?? new LeftoverVisibleObjectSettings();
    ILeftoverSettingsValues ISettingsValues.LeftoverInvisibleObjects => LeftoverInvisibleObjects ?? new LeftoverInvisibleObjectSettings();
    IRestoreWhereRemovedSettingsValues ISettingsValues.RestoreWhereRemoved => RestoreWhereRemoved ?? new RestoreWhereRemovedSettings();
}
