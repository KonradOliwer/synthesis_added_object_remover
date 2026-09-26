using Mutagen.Bethesda.Synthesis.Settings;

namespace AddedObjectRemover;

public enum FollowUpRemovalMode
{
    Off,
    AnyTouch,
    Anchoring,
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
    [SynthesisSettingName("What to check")]
    public CheckSettings WhatToCheck { get; set; } = new();

    [SynthesisSettingName("What to ignore")]
    public IgnoreSettings WhatToIgnore { get; set; } = new();

    [SynthesisSettingName("Follow-up removal")]
    public FollowUpRemovalSettings FollowUpRemoval { get; set; } = new();

    [SynthesisSettingName("Leftover invisible objects")]
    public LeftoverInvisibleObjectSettings LeftoverInvisibleObjects { get; set; } = new();

    [SynthesisSettingName("Diagnostics")]
    public DiagnosticsSettings Diagnostics { get; set; } = new();
}

public class CheckSettings
{
    public const float DefaultSizeMultiplier = 0.5f;

    [SynthesisSettingName("Target plugin")]
    [SynthesisTooltip("File name of the plugin whose added objects are checked, e.g. 'SomeMod.esp'; when empty or not in the load order, nothing is changed.")]
    public string TargetPlugin { get; set; } = string.Empty;

    [SynthesisSettingName("Size multiplier")]
    [SynthesisTooltip("How far past its own edges a target object reaches, as a fraction of its size (0-5; 0.5 = half its size further out on every side). Another mod's object whose centre falls inside makes it too close. Larger removes more; 0.25-1 is typical.")]
    public float SizeMultiplier { get; set; } = DefaultSizeMultiplier;
}

public class IgnoreSettings
{
    public const int DefaultMaxOtherMastersForPatch = 10;

    [SynthesisSettingName("Excluded plugins")]
    [SynthesisTooltip("Plugins whose objects never count as a conflict (Skyrim.esm, Update.esm and the three DLCs are always ignored; Creation Club plugins are not).")]
    public List<string> ExcludedPlugins { get; set; } = [];

    [SynthesisSettingName("Ignore the target's masters")]
    [SynthesisTooltip("Also ignore objects from the plugins the target plugin was built on (its masters).")]
    public bool IgnoreTargetMasters { get; set; } = true;

    [SynthesisSettingName("Ignore mods patched with the target")]
    [SynthesisTooltip("If a plugin depends on both the target and another mod, treat it as a compatibility patch: ignore that patch and the other mod. A plugin that depends on many mods (e.g. DynDOLOD.esp) is not treated as a patch. The log lists what was ignored.")]
    public bool IgnoreModsPatchedWithTarget { get; set; } = true;

    [SynthesisSettingName("Maximum other masters for a patch")]
    [SynthesisTooltip("A plugin counts as a compatibility patch only if it depends on the target plus at most this many other mods (1-100; the base game and the target's masters do not count). Plugins with more are skipped and listed in the log.")]
    public int MaxOtherMastersForPatch { get; set; } = DefaultMaxOtherMastersForPatch;
}

public class FollowUpRemovalSettings
{
    public const FollowUpRemovalMode DefaultMode = FollowUpRemovalMode.AnyTouch;
    public const float DefaultTouchDistance = 8f;
    public const float DefaultAnchoringThresholdPercent = 50f;

    [SynthesisSettingName("Follow-up removal mode")]
    [SynthesisTooltip("What happens to target objects touching a removed one. Off: only the too-close objects are removed. AnyTouch: also every target object connected to them through touching target objects (can spread through floors and walls). Anchoring: only touching objects that lose at least the Anchoring threshold of what they rest on.")]
    public FollowUpRemovalMode Mode { get; set; } = DefaultMode;

    [SynthesisSettingName("Touch distance")]
    [SynthesisTooltip("Largest gap, in game units (0-64), between two surfaces for them to count as touching (AnyTouch and Anchoring).")]
    public float TouchDistance { get; set; } = DefaultTouchDistance;

    [SynthesisSettingName("Anchoring threshold")]
    [SynthesisTooltip("Anchoring only: a touching object is removed when at least this percentage (1-100) of what it rests on or touches was removed; the ground and objects of any plugin count as support.")]
    public float AnchoringThresholdPercent { get; set; } = DefaultAnchoringThresholdPercent;
}

public class LeftoverInvisibleObjectSettings
{
    public const float DefaultSearchRadius = 1024f;
    public const int DefaultDirectionThresholdPercent = 50;
    public const int DefaultRemovedDirectionsPercent = 60;
    public const int DefaultOccupiedDirectionsPercent = 50;

    [SynthesisSettingName("Remove leftover invisible objects")]
    [SynthesisTooltip("Remove the target's invisible objects (lights, sounds, markers, insect spawners, trigger boxes, ...) that sit inside another mod's object or whose surrounding target objects were removed.")]
    public bool RemoveLeftoverInvisibleObjects { get; set; } = true;

    [SynthesisSettingName("Protected types")]
    [SynthesisTooltip("Invisible object types that are always kept.\nNone: nothing is protected.\nMarkers: map, X, idle, furniture and door markers.\nMarkersAndLights: Markers plus lights.\nMarkersLightsAndSounds: MarkersAndLights plus sound markers and acoustic spaces.\nCustom: the types listed in Custom protected types.")]
    public ProtectedInvisibleObjectsPreset ProtectedTypes { get; set; } = ProtectedInvisibleObjectsPreset.None;

    [SynthesisSettingName("Custom protected types")]
    [SynthesisTooltip("Used only when Protected types is Custom: the invisible object types to keep.")]
    public List<InvisibleObjectKind> CustomProtectedTypes { get; set; } = [];

    [SynthesisSettingName("Search radius")]
    [SynthesisTooltip("Largest distance in game units (64-8192) from an invisible object to the target's visible objects that count as its surroundings. A light, sound or trigger box that reaches less far uses its own reach.")]
    public float SearchRadius { get; set; } = DefaultSearchRadius;

    [SynthesisSettingName("Removed area per direction")]
    [SynthesisTooltip("A direction counts as removed when at least this percentage (10-100, in steps of 10) of the ground area of the target objects in it was removed.")]
    public int DirectionThresholdPercent { get; set; } = DefaultDirectionThresholdPercent;

    [SynthesisSettingName("Removed directions required")]
    [SynthesisTooltip("An invisible object is removed when at least this percentage (10-100, in steps of 10) of the directions holding target objects are removed.")]
    public int RemovedDirectionsPercent { get; set; } = DefaultRemovedDirectionsPercent;

    [SynthesisSettingName("Occupied directions required")]
    [SynthesisTooltip("Invisible objects with target objects in fewer than this percentage (10-100, in steps of 10) of the 8 directions around them are kept.")]
    public int OccupiedDirectionsPercent { get; set; } = DefaultOccupiedDirectionsPercent;

    [SynthesisSettingName("Move kept markers out of other mods' objects")]
    [SynthesisTooltip("Move a kept map, X (including heading), idle or other marker that sits inside another mod's object to the nearest free spot on the navmesh or, failing that, the ground. Lights, sounds, acoustic spaces, trigger boxes, critter spawners, decals, furniture and door markers are never moved.")]
    public bool MoveKeptMarkersOutOfOtherModsObjects { get; set; }
}

public class DiagnosticsSettings
{
    [SynthesisSettingName("Detailed log")]
    [SynthesisTooltip("Log every removed and kept object with its reason, per-space counts and unreadable meshes.")]
    public bool DetailedLog { get; set; }

    [SynthesisSettingName("Diagnostics folder")]
    [SynthesisTooltip("Absolute folder path for diagnostics spreadsheets (CSV files); leave empty to write nothing. Never changes the result.")]
    public string DiagnosticsFolder { get; set; } = string.Empty;
}
