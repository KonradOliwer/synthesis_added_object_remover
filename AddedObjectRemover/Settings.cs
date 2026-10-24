using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.FitClashingObjects.Contracts;
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

public class StandardSettings : ISettingsValues
{
    [SynthesisOrder]
    [SynthesisSettingName("Mod to clean up")]
    [SynthesisTooltip(
        "The cleaned mod: the plugin whose added objects are checked. Those that clash with other mods are removed in the patch, this Synthesis group's output plugin.\n"
        + "Nothing is deleted: a removed object is disabled; one that is not persistent is also moved below the world.\n"
        + "Always kept, whatever the settings: teleport doors and their destinations; anything another mod's object, a quest, a package,\n"
        + "a scene, a location or a navmesh points to; objects whose own script names a quest or scene, or whose base object\n"
        + "is this plugin's own and has a script that does; objects with a location role (except map markers). Linked objects are always removed or kept together.\n"
        + "Pick it from your load order.")]
    [JsonConverter(typeof(EmptyTextAsNullConverter))]
    // The settings key is CleanedMod (the user's word); the code keeps its target names.
    public ModKey CleanedMod { get; set; } = ModKey.Null;

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
        + "Interiors and worlds without terrain are not checked.")]
    public TerrainChangeSettings TerrainChange { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName("Shrink or turn objects to make them fit")]
    [SynthesisTooltip(
        "An object that would be removed for being too close or on changed terrain may first be shrunk or turned until it fits.\n"
        + "An object of another mod that the cleaned mod moved is always tried back where that mod placed it before it is removed;\n"
        + "one the cleaned mod left in place is never checked.")]
    public ShrinkOrTurnSettings ShrinkOrTurn { get; set; } = new();

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

    string? ISettingsValues.CleanedMod => CleanedMod.IsNull ? null : CleanedMod.ToString();
    ITrustSettingsValues ISettingsValues.Trust => Trust ?? new TrustSettings();
    ITooCloseSettingsValues ISettingsValues.TooClose => TooClose ?? new TooCloseRemovalSettings();
    ITerrainChangeSettingsValues ISettingsValues.TerrainChange => TerrainChange ?? new TerrainChangeSettings();
    IShrinkOrTurnSettingsValues ISettingsValues.ShrinkOrTurn => ShrinkOrTurn ?? new ShrinkOrTurnSettings();
    IFollowUpSettingsValues ISettingsValues.FollowUpRemoval => FollowUpRemoval ?? new FollowUpRemovalSettings();
    ILeftoverSettingsValues ISettingsValues.LeftoverInvisibleObjects => LeftoverInvisibleObjects ?? new LeftoverInvisibleObjectSettings();
}

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
        + "OnlyWhenStuckInsideTheObjectWithCollision: the same, but only for objects you cannot walk through (a rock, a tree, a wall);\n"
        + "objects without collision (ferns, shrubs, thickets, kelp) are never removed because an NPC stands in them.\n"
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
    [SynthesisSettingName("Count only height changes larger than (game units)")]
    [SynthesisTooltip(
        "Small smoothing edits are common and rarely visible under an object. 0 counts every change. 0-256.\n"
        + "Counting uses it only when \"Count height changes\" is on.\n"
        + "With that switch on or off, it also decides which height changes a mod trusted for its own changes owns: only those larger than this.")]
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
        + "or out of it. Water over ground that stays covered is not seen.")]
    public bool CountWaterHeightChanges { get; set; } = SettingDefaults.CountWaterHeightChanges;

    [SynthesisOrder]
    [SynthesisSettingName("Count only water height changes larger than (game units)")]
    [SynthesisTooltip(
        "Small water level edits rarely matter. 0 counts every change. 0-256.\n"
        + "Counting uses it only when \"Count water height changes\" is on.\n"
        + "With that switch on or off, it also decides which water height changes a mod trusted for its own changes owns: only those larger than this.")]
    public float WaterHeightChangeTolerance { get; set; } = SettingDefaults.WaterHeightChangeTolerance;

    [SynthesisOrder]
    [SynthesisSettingName("Count water added/removed changes")]
    [SynthesisTooltip(
        "Whether another mod adding water to a cell or taking its water away counts, when that puts ground around the object\n"
        + "under water or out of it. Water over ground that stays covered is not seen.\n"
        + "A world that stores no default water height has no default water: adding or dropping that default adds or takes away\n"
        + "the water of the cells that use it.")]
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

public class ShrinkOrTurnSettings : IShrinkOrTurnSettingsValues
{
    [SynthesisOrder]
    [SynthesisSettingName("Distance that counts as moved (game units)")]
    [SynthesisTooltip(
        "A replacement of another mod's object counts as moved when it stands more than this many game units from the object it replaces;\n"
        + "a moved one is judged like the mod's own objects, an unmoved one is always kept.")]
    public float MovedDistanceInUnits { get; set; } = SettingDefaults.MovedDistanceInUnits;

    [SynthesisOrder]
    [SynthesisSettingName("Shrink down to (% of its size)")]
    [SynthesisTooltip(
        "An object may be shrunk down to this share of its size to make it fit, in steps of 5%; the largest size that fits is used.\n"
        + "100 or more: objects are never shrunk. At least 1.")]
    public int ShrinkLimitPercent { get; set; } = SettingDefaults.ShrinkLimitPercent;

    [SynthesisOrder]
    [SynthesisSettingName("Turn by at most (degrees either way)")]
    [SynthesisTooltip(
        "An object may be turned about its upright axis by up to this many degrees to the left or to the right to make it fit,\n"
        + "in steps of 15 degrees; the smallest turn that fits is used. It is never tilted.\n"
        + "0: objects are never turned. 180 or more: objects may face any way.")]
    public int TurnLimitDegrees { get; set; } = SettingDefaults.TurnLimitDegrees;

    [SynthesisOrder]
    [SynthesisSettingName("Which objects may be shrunk or turned")]
    [SynthesisTooltip(
        "ReplacementsAfterMovingBack: only objects of other mods that the cleaned mod moved, and only after moving them back where that mod placed them.\n"
        + "ReplacementsAtBothSpots: the same objects, first where the cleaned mod put them, then after moving them back.\n"
        + "AllObjects: as ReplacementsAtBothSpots, and also the cleaned mod's own objects where they stand.\n"
        + "With neither shrinking nor turning allowed, this changes nothing.")]
    public ShrinkOrTurnObjects WhichObjects { get; set; } = SettingDefaults.ShrinkOrTurn;
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
    public bool MoveKeptMarkersOutOfOtherModsObjects { get; set; } = true;

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
