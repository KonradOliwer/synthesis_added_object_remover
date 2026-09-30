using Mutagen.Bethesda.Synthesis.Settings;

namespace AddedObjectRemover;

/// <summary>The zone around a cleaned mod's object in which another mod's object makes it too close.</summary>
public enum ZoneShape
{
    ObjectShape,
    BoundingBox,
}

/// <summary>How other mods' placed NPCs and creatures take part in the too-close step.</summary>
public enum NpcHandling
{
    CountLikeObjects,
    OnlyWhenStuckInObject,
    Ignore,
}

public enum FollowUpRemovalMode
{
    Nothing,
    EverythingTouching,
    ObjectsSupportedByIt,
}

/// <summary>Kinds of placed objects that cannot be seen in game.</summary>
public enum InvisibleObjectKind
{
    MapMarkers,
    XMarkers,
    IdleMarkers,
    FurnitureMarkers,
    DoorMarkers,
    OtherMarkers,
    Lights,
    SoundMarkers,
    AcousticSpaces,
    CritterSpawners,
    TriggerBoxes,
    Decals,
}

public enum ProtectedInvisibleObjectsPreset
{
    None,
    Markers,
    MarkersAndLights,
    MarkersLightsAndSounds,
    Custom,
}

public class Settings
{
    [SynthesisSettingName("Mod to clean up")]
    [SynthesisTooltip("Which plugin to clean up and when its objects are removed.")]
    public CheckSettings WhatToCheck { get; set; } = new();

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
}

public class CheckSettings
{
    public const float DefaultSizeMultiplier = 0.5f;
    public const ZoneShape DefaultZoneShape = ZoneShape.ObjectShape;

    [SynthesisSettingName("Mod to clean up")]
    [SynthesisTooltip("The plugin whose added objects may be removed.")]
    public string TargetPlugin { get; set; } = string.Empty;

    [SynthesisSettingName("Removal distance (× object size)")]
    [SynthesisTooltip("Removes the cleaned mod's object when another mod's object is this close, measured in multiples of the object's own size.")]
    public float SizeMultiplier { get; set; } = DefaultSizeMultiplier;

    [SynthesisSettingName("Removal zone")]
    [SynthesisTooltip("ObjectShape: the object's own shape, enlarged. BoundingBox: faster, its box.")]
    public ZoneShape ZoneShape { get; set; } = DefaultZoneShape;
}

public class IgnoreSettings
{
    public const int DefaultMaxOtherMastersForPatch = 10;
    public const NpcHandling DefaultNpcHandling = NpcHandling.OnlyWhenStuckInObject;

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
    public int MaxOtherMastersForPatch { get; set; } = DefaultMaxOtherMastersForPatch;

    [SynthesisSettingName("NPCs and creatures")]
    [SynthesisTooltip("Whether other mods' NPCs cause removals. Default: only when stuck in the object.")]
    public NpcHandling NpcHandling { get; set; } = DefaultNpcHandling;
}

public class FollowUpRemovalSettings
{
    public const FollowUpRemovalMode DefaultMode = FollowUpRemovalMode.EverythingTouching;
    public const float DefaultTouchDistance = 8f;
    public const float DefaultAnchoringThresholdPercent = 50f;

    [SynthesisSettingName("Also remove")]
    [SynthesisTooltip("What else goes with a removed object.")]
    public FollowUpRemovalMode Mode { get; set; } = DefaultMode;

    [SynthesisSettingName("Touch gap")]
    [SynthesisTooltip("Gap still counted as touching, in game units.")]
    public float TouchDistance { get; set; } = DefaultTouchDistance;

    [SynthesisSettingName("Support lost (%)")]
    [SynthesisTooltip("Remove an object once this much of its support is gone.")]
    public float AnchoringThresholdPercent { get; set; } = DefaultAnchoringThresholdPercent;
}

public class LeftoverInvisibleObjectSettings
{
    public const float DefaultSearchRadius = 1024f;
    public const int DefaultDirectionThresholdPercent = 50;
    public const int DefaultRemovedDirectionsPercent = 60;
    public const int DefaultOccupiedDirectionsPercent = 50;

    [SynthesisSettingName("Remove leftover sounds and markers")]
    [SynthesisTooltip("Removes invisible objects whose surroundings were removed.")]
    public bool RemoveLeftoverInvisibleObjects { get; set; } = true;

    [SynthesisSettingName("Look-around distance")]
    [SynthesisTooltip("How far around to check, in game units.")]
    public float SearchRadius { get; set; } = DefaultSearchRadius;

    [SynthesisSettingName("Direction cleared at (%)")]
    [SynthesisTooltip("Share removed for a direction to count as cleared.")]
    public int DirectionThresholdPercent { get; set; } = DefaultDirectionThresholdPercent;

    [SynthesisSettingName("Cleared directions needed (%)")]
    [SynthesisTooltip("Share of directions that must be cleared.")]
    public int RemovedDirectionsPercent { get; set; } = DefaultRemovedDirectionsPercent;

    [SynthesisSettingName("Minimum directions with objects (%)")]
    [SynthesisTooltip("Of the 8 directions, this share must contain the cleaned mod's objects before deciding.")]
    public int OccupiedDirectionsPercent { get; set; } = DefaultOccupiedDirectionsPercent;

    [SynthesisSettingName("Never remove")]
    [SynthesisTooltip("Invisible object types to always keep.")]
    public ProtectedInvisibleObjectsPreset ProtectedTypes { get; set; } = ProtectedInvisibleObjectsPreset.None;

    [SynthesisSettingName("Types to never remove (Custom)")]
    [SynthesisTooltip("Used when \"Never remove\" is Custom. Add the types to keep, e.g. MapMarkers.")]
    public List<InvisibleObjectKind> CustomProtectedTypes { get; set; } = [];

    [SynthesisSettingName("Move kept markers out of other mods' objects")]
    [SynthesisTooltip("Moves kept markers enclosed by another mod's object, e.g. a building or rock.")]
    public bool MoveKeptMarkersOutOfOtherModsObjects { get; set; }
}

public class DiagnosticsSettings
{
    public const string DefaultReportFolder = "AddedObjectRemover Reports";

    [SynthesisSettingName("Detailed log")]
    [SynthesisTooltip("Lists every removed object and why, plus timings and statistics.")]
    public bool DetailedLog { get; set; }

    [SynthesisSettingName("Write report files")]
    [SynthesisTooltip("Saves CSV files for checking the results.")]
    public bool WriteReportFiles { get; set; }

    [SynthesisSettingName("Report folder")]
    [SynthesisTooltip("Folder for report files: a full path, or relative to the patch output folder.")]
    public string DiagnosticsFolder { get; set; } = DefaultReportFolder;
}
