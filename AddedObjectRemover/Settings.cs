using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;
using Mutagen.Bethesda.Synthesis.Settings;

namespace AddedObjectRemover;

public class Settings : ISettingsValues
{
    [SynthesisSettingName("Mod to clean up")]
    [SynthesisTooltip("Which plugin to clean up.")]
    public CheckSettings WhatToCheck { get; set; } = new();

    [SynthesisSettingName("Terrain changed by other mods")]
    [SynthesisTooltip("Removes the cleaned mod's objects where another mod changed the terrain under or near them.")]
    public TerrainChangeSettings TerrainChange { get; set; } = new();

    [SynthesisSettingName("Too close to other mods' objects")]
    [SynthesisTooltip("Removes the cleaned mod's objects that sit too close to another mod's objects.")]
    public TooCloseRemovalSettings TooClose { get; set; } = new();

    [SynthesisSettingName("Mods that never count as clashing")]
    [SynthesisTooltip("Objects from these mods never cause removals.")]
    public IgnoreSettings WhatToIgnore { get; set; } = new();

    [SynthesisSettingName("Objects resting on removed ones")]
    [SynthesisTooltip("What else is removed together with a removed object.")]
    public FollowUpRemovalSettings FollowUpRemoval { get; set; } = new();

    [SynthesisSettingName("Invisible objects left behind")]
    [SynthesisTooltip("Sounds, markers, lights and spawners inside other mods' objects or left without surroundings.")]
    public LeftoverInvisibleObjectSettings LeftoverInvisibleObjects { get; set; } = new();

    [SynthesisSettingName("Logs and reports")]
    [SynthesisTooltip("Extra output for checking the results.")]
    public DiagnosticsSettings Diagnostics { get; set; } = new();

    ICheckSettingsValues ISettingsValues.WhatToCheck => WhatToCheck ?? new CheckSettings();
    ITerrainChangeSettingsValues ISettingsValues.TerrainChange => TerrainChange ?? new TerrainChangeSettings();
    ITooCloseSettingsValues ISettingsValues.TooClose => TooClose ?? new TooCloseRemovalSettings();
    IIgnoreSettingsValues ISettingsValues.WhatToIgnore => WhatToIgnore ?? new IgnoreSettings();
    IFollowUpSettingsValues ISettingsValues.FollowUpRemoval => FollowUpRemoval ?? new FollowUpRemovalSettings();
    ILeftoverSettingsValues ISettingsValues.LeftoverInvisibleObjects => LeftoverInvisibleObjects ?? new LeftoverInvisibleObjectSettings();
    IDiagnosticsSettingsValues ISettingsValues.Diagnostics => Diagnostics ?? new DiagnosticsSettings();
}

public class CheckSettings : ICheckSettingsValues
{
    [SynthesisSettingName("Mod to clean up")]
    [SynthesisTooltip("The plugin whose added objects may be removed.")]
    public string TargetPlugin { get; set; } = string.Empty;

    string? ICheckSettingsValues.TargetPlugin => TargetPlugin;
}

public class TooCloseRemovalSettings : ITooCloseSettingsValues
{
    [SynthesisSettingName("Remove when too close (× object size)")]
    [SynthesisTooltip("Removes the cleaned mod's object when another mod's object is within the removal distance, measured in multiples of the object's own size.")]
    public bool RemoveTooCloseBySize { get; set; } = SettingDefaults.RemoveTooCloseBySize;

    [SynthesisSettingName("Removal distance (× object size)")]
    [SynthesisTooltip("How close counts as too close, in multiples of the object's own size. Only used when \"Remove when too close (× object size)\" is on.")]
    public float SizeMultiplier { get; set; } = SettingDefaults.SizeMultiplier;

    [SynthesisSettingName("Remove when too close (game units)")]
    [SynthesisTooltip("Removes the cleaned mod's object when another mod's object comes within the removal distance, measured in game units.")]
    public bool RemoveTooCloseByDistance { get; set; } = SettingDefaults.RemoveTooCloseByDistance;

    [SynthesisSettingName("Removal distance (game units)")]
    [SynthesisTooltip("How close counts as too close, in game units. 0 means only touching or overlapping objects. Only used when \"Remove when too close (game units)\" is on.")]
    public float RemovalDistanceInUnits { get; set; } = SettingDefaults.RemovalDistanceInUnits;

    [SynthesisSettingName("Removal zone")]
    [SynthesisTooltip("ObjectShape: the object's own shape, enlarged or with the distance around it. BoundingBox: faster, its box. Applies to both removal distances.")]
    public ZoneShape ZoneShape { get; set; } = SettingDefaults.Zone;
}

public class TerrainChangeSettings : ITerrainChangeSettingsValues
{
    [SynthesisSettingName("Remove objects on changed terrain")]
    [SynthesisTooltip("Removes the cleaned mod's objects when another mod changed the terrain under or near them.")]
    public bool RemoveOnChangedTerrain { get; set; } = SettingDefaults.RemoveOnChangedTerrain;

    [SynthesisSettingName("Objects checked")]
    [SynthesisTooltip("VisibleObjectsOnTheGround: visible objects that reach the ground. AllVisibleObjects: every visible object, at any height. AllObjects: also invisible ones, such as markers and sounds.")]
    public TerrainCheckedObjects TerrainCheckedObjects { get; set; } = SettingDefaults.TerrainChecked;

    [SynthesisSettingName("Count height changes")]
    [SynthesisTooltip("Whether a change of terrain height counts.")]
    public bool CountHeightChanges { get; set; } = SettingDefaults.CountHeightChanges;

    [SynthesisSettingName("Height change tolerance")]
    [SynthesisTooltip("A height change up to this many game units is ignored. 0 counts every change.")]
    public float HeightChangeTolerance { get; set; } = SettingDefaults.HeightChangeTolerance;

    [SynthesisSettingName("Count terrain type changes")]
    [SynthesisTooltip("Whether a change of ground type counts, for example grass becoming road or stone. A different texture of the same type does not count.")]
    public bool CountTerrainTypeChanges { get; set; } = SettingDefaults.CountTerrainTypeChanges;

    [SynthesisSettingName("Terrain distance measured in")]
    [SynthesisTooltip("GameUnits: \"Terrain distance (game units)\" is used. ObjectSize: \"Terrain distance (× object size)\" is used.")]
    public TerrainDistanceMode TerrainDistanceMode { get; set; } = SettingDefaults.TerrainDistance;

    [SynthesisSettingName("Terrain distance (game units)")]
    [SynthesisTooltip("How far around the object the terrain is checked, in game units. The terrain points around the object are always checked.")]
    public float TerrainDistanceInUnits { get; set; } = SettingDefaults.TerrainDistanceInUnits;

    [SynthesisSettingName("Terrain distance (× object size)")]
    [SynthesisTooltip("How far around the object the terrain is checked, in multiples of the object's own size.")]
    public float TerrainDistanceSizeMultiplier { get; set; } = SettingDefaults.TerrainDistanceSizeMultiplier;
}

public class IgnoreSettings : IIgnoreSettingsValues
{
    [SynthesisSettingName("Mods to ignore")]
    [SynthesisTooltip("Objects from these plugins never cause removals.")]
    public List<string> ExcludedPlugins { get; set; } = [];

    [SynthesisSettingName("Ignore the mod's own masters")]
    [SynthesisTooltip("Objects from mods it requires never cause removals.")]
    public bool IgnoreTargetMasters { get; set; } = true;

    [SynthesisSettingName("Ignore mods sharing a patch")]
    [SynthesisTooltip("If a patch combines both mods, they don't clash.")]
    public bool IgnoreModsPatchedWithTarget { get; set; } = true;

    [SynthesisSettingName("Patch master limit")]
    [SynthesisTooltip("Plugins with more masters than this aren't treated as patches.")]
    public int MaxOtherMastersForPatch { get; set; } = SettingDefaults.MaxOtherMastersForPatch;

    [SynthesisSettingName("NPCs and creatures")]
    [SynthesisTooltip("Whether other mods' NPCs cause removals. Default: only when stuck in the object.")]
    public NpcHandling NpcHandling { get; set; } = SettingDefaults.Npcs;

    IReadOnlyList<string?> IIgnoreSettingsValues.ExcludedPlugins => ExcludedPlugins?.ToArray() ?? [];
}

public class FollowUpRemovalSettings : IFollowUpSettingsValues
{
    [SynthesisSettingName("Also remove")]
    [SynthesisTooltip("What else goes with a removed object.")]
    public FollowUpRemovalMode Mode { get; set; } = SettingDefaults.FollowUpMode;

    [SynthesisSettingName("Touch gap")]
    [SynthesisTooltip("Gap still counted as touching, in game units.")]
    public float TouchDistance { get; set; } = SettingDefaults.TouchDistance;

    [SynthesisSettingName("Support lost (%)")]
    [SynthesisTooltip("Remove an object once this much of its support is gone.")]
    public float AnchoringThresholdPercent { get; set; } = SettingDefaults.AnchoringThresholdPercent;
}

public class LeftoverInvisibleObjectSettings : ILeftoverSettingsValues
{
    [SynthesisSettingName("Remove leftover sounds and markers")]
    [SynthesisTooltip("Removes invisible objects whose surroundings were removed.")]
    public bool RemoveLeftoverInvisibleObjects { get; set; } = true;

    [SynthesisSettingName("Look-around distance")]
    [SynthesisTooltip("How far around to check, in game units.")]
    public float SearchRadius { get; set; } = SettingDefaults.SearchRadius;

    [SynthesisSettingName("Direction cleared at (%)")]
    [SynthesisTooltip("Share removed for a direction to count as cleared.")]
    public int DirectionThresholdPercent { get; set; } = SettingDefaults.DirectionThresholdPercent;

    [SynthesisSettingName("Cleared directions needed (%)")]
    [SynthesisTooltip("Share of directions that must be cleared.")]
    public int RemovedDirectionsPercent { get; set; } = SettingDefaults.RemovedDirectionsPercent;

    [SynthesisSettingName("Minimum directions with objects (%)")]
    [SynthesisTooltip("Of the 8 directions, this share must contain the cleaned mod's objects before deciding.")]
    public int OccupiedDirectionsPercent { get; set; } = SettingDefaults.OccupiedDirectionsPercent;

    [SynthesisSettingName("Never remove")]
    [SynthesisTooltip("Invisible object types to always keep.")]
    public ProtectedInvisibleObjectsPreset ProtectedTypes { get; set; } = ProtectedInvisibleObjectsPreset.None;

    [SynthesisSettingName("Types to never remove (Custom)")]
    [SynthesisTooltip("Used when \"Never remove\" is Custom. Add the types to keep, e.g. MapMarkers.")]
    public List<InvisibleObjectKind> CustomProtectedTypes { get; set; } = [];

    [SynthesisSettingName("Move kept markers out of other mods' objects")]
    [SynthesisTooltip("Moves kept markers enclosed by another mod's object, e.g. a building or rock.")]
    public bool MoveKeptMarkersOutOfOtherModsObjects { get; set; }

    IReadOnlyList<InvisibleObjectKind> ILeftoverSettingsValues.CustomProtectedTypes => CustomProtectedTypes?.ToArray() ?? [];
}

public class DiagnosticsSettings : IDiagnosticsSettingsValues
{
    [SynthesisSettingName("Detailed log")]
    [SynthesisTooltip("Lists every removed object and why, plus timings and statistics.")]
    public bool DetailedLog { get; set; }

    [SynthesisSettingName("Write report files")]
    [SynthesisTooltip("Saves CSV files for checking the results.")]
    public bool WriteReportFiles { get; set; }

    [SynthesisSettingName("Report folder")]
    [SynthesisTooltip("Folder for report files: a full path, or relative to the patch output folder.")]
    public string DiagnosticsFolder { get; set; } = SettingDefaults.ReportFolder;

    string? IDiagnosticsSettingsValues.ReportFolder => DiagnosticsFolder;
}
