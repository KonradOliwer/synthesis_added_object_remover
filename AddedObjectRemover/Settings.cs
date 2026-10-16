using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Synthesis.Settings;

namespace AddedObjectRemover;

public class Settings : ISettingsValues
{
    [SynthesisOrder]
    [SynthesisSettingName("Mod to clean up")]
    [SynthesisTooltip(
        "The plugin whose added objects are checked. Its objects that clash with other mods are removed in this patch.\n"
        + "Nothing is deleted: a removed object is disabled and moved below the world.\n"
        + "Pick it from your load order.")]
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
        + "or changed the kind of ground (for example grass to road), so the object may float, sink or look out of place.\n"
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

    [SynthesisOrder]
    [SynthesisSettingName("Logs and reports")]
    [SynthesisTooltip("Extra output for checking what was removed and why. Neither setting changes the patch.")]
    public DiagnosticsSettings Diagnostics { get; set; } = new();

    string? ISettingsValues.TargetPlugin => TargetPlugin.IsNull ? null : TargetPlugin.ToString();
    IIgnoreSettingsValues ISettingsValues.WhatToIgnore => WhatToIgnore ?? new IgnoreSettings();
    ITooCloseSettingsValues ISettingsValues.TooClose => TooClose ?? new TooCloseRemovalSettings();
    ITerrainChangeSettingsValues ISettingsValues.TerrainChange => TerrainChange ?? new TerrainChangeSettings();
    IFollowUpSettingsValues ISettingsValues.FollowUpRemoval => FollowUpRemoval ?? new FollowUpRemovalSettings();
    ILeftoverSettingsValues ISettingsValues.LeftoverInvisibleObjects => LeftoverInvisibleObjects ?? new LeftoverInvisibleObjectSettings();
    IDiagnosticsSettingsValues ISettingsValues.Diagnostics => Diagnostics ?? new DiagnosticsSettings();
}

public class IgnoreSettings : IIgnoreSettingsValues
{
    [SynthesisOrder]
    [SynthesisSettingName("Never count these mods")]
    [SynthesisTooltip("Add plugins whose objects and terrain changes should never cause removals, for example Creation Club plugins.")]
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
        "The terrain under the object plus this margin around it is checked for both height and ground type changes.\n"
        + "Bigger objects get a bigger margin. The terrain points right around the object are always checked.")]
    public TerrainBySizeSettings BySize { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName("Margin by fixed distance")]
    [SynthesisTooltip(
        "The terrain under the object plus this margin around it is checked for both height and ground type changes.\n"
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
    [SynthesisSettingName("When an invisible object counts as alone")]
    [SynthesisTooltip(
        "The area around it is split into 8 directions (north, north-east, ...). Only the cleaned mod's visible objects count.\n"
        + "The three percentages are applied in the order listed, in steps of 10.")]
    public AloneSettings WhenAlone { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName("Always keep these invisible objects")]
    [SynthesisTooltip("Ticked kinds are never removed by this step, unless they are linked to a removed object.")]
    public KeptInvisibleObjectSettings AlwaysKeep { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName("Move kept markers out of other mods' objects")]
    [SynthesisTooltip(
        "A kept map, X, idle or other marker that sits inside another mod's object is moved to the nearest free spot (up to 2048 units).\n"
        + "Only its position changes. Lights, sounds, furniture and door markers are never moved. Needs the switch at the top of this group.")]
    public bool MoveKeptMarkersOutOfOtherModsObjects { get; set; }

    float ILeftoverSettingsValues.SearchRadius => (WhenAlone ?? new()).SearchRadius;
    int ILeftoverSettingsValues.OccupiedDirectionsPercent => (WhenAlone ?? new()).OccupiedDirectionsPercent;
    int ILeftoverSettingsValues.DirectionThresholdPercent => (WhenAlone ?? new()).DirectionThresholdPercent;
    int ILeftoverSettingsValues.RemovedDirectionsPercent => (WhenAlone ?? new()).RemovedDirectionsPercent;
    IReadOnlyList<InvisibleObjectKind> ILeftoverSettingsValues.NeverRemove => (AlwaysKeep ?? new()).Kinds();
}

public class AloneSettings
{
    [SynthesisOrder]
    [SynthesisSettingName("Look this far around it (game units)")]
    [SynthesisTooltip("A light, sound or trigger box that reaches less far uses its own reach. 64-8192.")]
    public float SearchRadius { get; set; } = SettingDefaults.SearchRadius;

    [SynthesisOrder]
    [SynthesisSettingName("Decide only if this share of the 8 directions has objects (%)")]
    [SynthesisTooltip("10-100, in steps of 10.")]
    public int OccupiedDirectionsPercent { get; set; } = SettingDefaults.OccupiedDirectionsPercent;

    [SynthesisOrder]
    [SynthesisSettingName("A direction is cleared when this much of it was removed (%)")]
    [SynthesisTooltip("Measured by the ground area the cleaned mod's objects cover in that direction. 10-100, in steps of 10.")]
    public int DirectionThresholdPercent { get; set; } = SettingDefaults.DirectionThresholdPercent;

    [SynthesisOrder]
    [SynthesisSettingName("Remove when this share of those directions is cleared (%)")]
    [SynthesisTooltip("10-100, in steps of 10.")]
    public int RemovedDirectionsPercent { get; set; } = SettingDefaults.RemovedDirectionsPercent;
}

public class KeptInvisibleObjectSettings
{
    [SynthesisOrder]
    [SynthesisSettingName("Map markers")]
    [SynthesisTooltip("The icons on the world map.")]
    public bool MapMarkers { get; set; }

    [SynthesisOrder]
    [SynthesisSettingName("X markers")]
    [SynthesisTooltip("Invisible reference points, including those with a heading.")]
    public bool XMarkers { get; set; }

    [SynthesisOrder]
    [SynthesisSettingName("Idle markers")]
    [SynthesisTooltip("Spots where characters play an idle animation.")]
    public bool IdleMarkers { get; set; }

    [SynthesisOrder]
    [SynthesisSettingName("Furniture markers")]
    [SynthesisTooltip("Positions where characters use furniture.")]
    public bool FurnitureMarkers { get; set; }

    [SynthesisOrder]
    [SynthesisSettingName("Door markers")]
    [SynthesisTooltip("The spots where doors place whoever uses them.")]
    public bool DoorMarkers { get; set; }

    [SynthesisOrder]
    [SynthesisSettingName("Other markers")]
    [SynthesisTooltip("Any other invisible marker.")]
    public bool OtherMarkers { get; set; }

    [SynthesisOrder]
    [SynthesisSettingName("Lights")]
    [SynthesisTooltip("Light sources that have no visible model.")]
    public bool Lights { get; set; }

    [SynthesisOrder]
    [SynthesisSettingName("Sound markers")]
    [SynthesisTooltip("Spots that play a sound.")]
    public bool SoundMarkers { get; set; }

    [SynthesisOrder]
    [SynthesisSettingName("Acoustic spaces")]
    [SynthesisTooltip("Areas that change how sound echoes.")]
    public bool AcousticSpaces { get; set; }

    [SynthesisOrder]
    [SynthesisSettingName("Critter spawners")]
    [SynthesisTooltip("Spots where small animals appear.")]
    public bool CritterSpawners { get; set; }

    [SynthesisOrder]
    [SynthesisSettingName("Trigger boxes")]
    [SynthesisTooltip("Invisible areas that start something when entered.")]
    public bool TriggerBoxes { get; set; }

    [SynthesisOrder]
    [SynthesisSettingName("Decals")]
    [SynthesisTooltip("Marks painted onto surfaces, such as dirt or blood.")]
    public bool Decals { get; set; }

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

public class DiagnosticsSettings : IDiagnosticsSettingsValues
{
    [SynthesisOrder]
    [SynthesisSettingName("Detailed log (every removed object and why)")]
    [SynthesisTooltip("Lists every removed object and why, plus timings and statistics.")]
    public bool DetailedLog { get; set; }

    [SynthesisOrder]
    [SynthesisSettingName("Write report files (CSV)")]
    [SynthesisTooltip("Saves CSV files for checking the results.")]
    public bool WriteReportFiles { get; set; }

    [SynthesisOrder]
    [SynthesisSettingName("Folder for report files")]
    [SynthesisTooltip("A full path, or a folder next to the output plugin. Only used when report files are on.")]
    public string DiagnosticsFolder { get; set; } = SettingDefaults.ReportFolder;

    string? IDiagnosticsSettingsValues.ReportFolder => DiagnosticsFolder;
}
