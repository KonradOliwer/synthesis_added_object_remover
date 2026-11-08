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
    [SynthesisSettingName("Standard")]
    [SynthesisTooltip("The settings for cleaning up one mod. Not used in Batch mode.")]
    public StandardSettings Standard { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName("Batch")]
    [SynthesisTooltip("Cleaning up several mods in one run. Not used in Standard mode.")]
    public BatchSettings Batch { get; set; } = new();

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
    [SynthesisSettingName(SettingNames.CleanedMod)]
    [SynthesisTooltip(
        "The cleaned mod: the plugin whose added objects are checked; those that clash with other mods are disabled in the patch, not deleted.\n"
        + "Some objects are always kept: see \"What is always kept\" in the README.")]
    [JsonConverter(typeof(EmptyTextAsNullConverter))]
    // The settings key is CleanedMod (the user's word); the code keeps its target names.
    public ModKey CleanedMod { get; set; } = ModKey.Null;

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.TooClose)]
    [SynthesisTooltip(
        "The main check. An object of the cleaned mod is removed when an object from another mod comes within a margin around it.\n"
        + "Turn on one or both margins; an object caught by either is removed once. With both off, nothing is removed for being too close.")]
    public TooCloseRemovalSettings TooClose { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.ChangedTerrain)]
    [SynthesisTooltip(
        "Removes an object of the cleaned mod when another mod changed the terrain under or around it (height, kind of ground or water), so it may float, sink or look out of place. Interiors and worlds without terrain are not checked.\n"
        + "Its two \"Count only ... larger than\" values also decide which height changes \"Restore what the cleaned mod changed where it was removed\" puts back.")]
    public TerrainChangeSettings TerrainChange { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.ShrinkOrTurn)]
    [SynthesisTooltip(
        "An object of the cleaned mod that would be removed for being too close to another mod's object is first tried shrunk or turned where it stands;\n"
        + "if it fits, it is kept that way. Applies to the cleaned mod's own objects only. Never shrunk or turned: objects on changed terrain,\n"
        + "objects that other objects rest on, invisible, always kept and linked objects (see the README).")]
    public ShrinkOrTurnSettings ShrinkOrTurn { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.Trust)]
    [SynthesisTooltip(
        "Only untrusted mods' objects and terrain changes can remove the cleaned mod's objects.\n"
        + "Always trusted: the base game (Skyrim.esm, Update.esm and the three DLCs), the cleaned mod, and its masters (the plugins it requires).")]
    public TrustSettings Trust { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.AlsoRemove)]
    [SynthesisTooltip(
        "When an object is removed by any removal step (too close, changed terrain, objects left behind), the cleaned mod's objects resting on it or touching it would be left floating. This removes them too.\n"
        + "Only the cleaned mod's own visible objects are removed this way; the last setting of this group decides about other mods' objects.")]
    public FollowUpRemovalSettings FollowUpRemoval { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.LeftoverObjects)]
    [SynthesisTooltip(
        "Removes what is left of the cleaned mod where most of it around was removed by the other removal steps (too close, changed terrain, also remove).\n"
        + "The direction method looks at the cleaned mod's visible objects around an object in eight directions and works in rounds.\n"
        + "Only the cleaned mod's objects, including other mods' objects it changed, count as surroundings.")]
    public LeftoverObjectSettings LeftoverObjects { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.InvisibleLeftBehind)]
    [SynthesisTooltip(
        "The cleaned mod's sounds, lights, markers, spawners and similar invisible objects are removed when they end up inside\n"
        + "another mod's object (a building, a boulder) or when the visible objects around them were removed.")]
    public LeftoverInvisibleObjectSettings LeftoverInvisibleObjects { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.ChangedObjects)]
    [SynthesisTooltip(
        "The cleaned mod can override an object of the base game, of one of its masters or of another mod: move it, or give it another model.\n"
        + "These settings decide how such an object is checked and what happens when it would be removed. The cleaned mod's own objects are not affected.")]
    public OtherModsChangedObjectsSettings ChangedObjects { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.RestoreWhereRemoved)]
    [SynthesisTooltip(
        "Where the cleaned mod's objects were removed from an area, its other changes there go back to how the game showed them without it:\n"
        + "other mods' objects it changed, the terrain, the cells' settings and the navmeshes.\n"
        + "What a later plugin changed again stays, as does anything that would not fit the objects that stay.")]
    public RestoreWhereRemovedSettings RestoreWhereRemoved { get; set; } = new();

    string? ISettingsValues.CleanedMod => CleanedMod.IsNull ? null : CleanedMod.ToString();
    IChangedObjectsSettingsValues ISettingsValues.ChangedObjects => ChangedObjects ?? new OtherModsChangedObjectsSettings();
    ITrustSettingsValues ISettingsValues.Trust => Trust ?? new TrustSettings();
    ICollisionSettingsValues ISettingsValues.Collision => this;
    ITooCloseSettingsValues ICollisionSettingsValues.TooClose => TooClose ?? new TooCloseRemovalSettings();
    ITerrainChangeSettingsValues ICollisionSettingsValues.TerrainChange => TerrainChange ?? new TerrainChangeSettings();
    IShrinkOrTurnSettingsValues ICollisionSettingsValues.ShrinkOrTurn => ShrinkOrTurn ?? new ShrinkOrTurnSettings();
    IFollowUpSettingsValues ISettingsValues.FollowUpRemoval => FollowUpRemoval ?? new FollowUpRemovalSettings();
    ILeftoverObjectsSettingsValues ISettingsValues.LeftoverObjects => LeftoverObjects ?? new LeftoverObjectSettings();
    IInvisibleLeftoverSettingsValues ISettingsValues.LeftoverInvisibleObjects => LeftoverInvisibleObjects ?? new LeftoverInvisibleObjectSettings();
    IRestoreWhereRemovedSettingsValues ISettingsValues.RestoreWhereRemoved => RestoreWhereRemoved ?? new RestoreWhereRemovedSettings();
}
