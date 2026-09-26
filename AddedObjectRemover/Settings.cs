using Mutagen.Bethesda.Synthesis.Settings;

namespace AddedObjectRemover;

public class Settings
{
    [SynthesisSettingName("Target plugin")]
    [SynthesisTooltip("File name of the plugin whose added objects should be checked, e.g. 'SomeMod.esp'. Case-insensitive. If empty or not in the load order, the patcher does nothing.")]
    public string TargetPlugin { get; set; } = string.Empty;

    [SynthesisSettingName("Size multiplier")]
    [SynthesisTooltip("How far beyond its own bounds a target object 'reaches'. The target's scaled bounding box is grown on every side by this fraction of its size along that axis (0.5 = the box becomes twice as large in each dimension). Another mod's object whose bounds center falls inside this grown box makes the target object too close.")]
    public float Multiplier { get; set; } = 0.5f;

    [SynthesisSettingName("Excluded plugins")]
    [SynthesisTooltip("Plugin file names whose objects are ignored as 'other mods' objects. Case-insensitive. Skyrim.esm, Update.esm, Dawnguard.esm, HearthFires.esm and Dragonborn.esm are always ignored.")]
    public List<string> ExcludedPlugins { get; set; } = [];

    [SynthesisSettingName("Exclude masters of the target plugin")]
    [SynthesisTooltip("Also ignore objects from plugins that are masters of the target plugin (the target mod was built around them).")]
    public bool ExcludeTargetMasters { get; set; } = true;

    [SynthesisSettingName("Measure size from meshes (NIF)")]
    [SynthesisTooltip("Read the bounding box of each base object's mesh (loose files first, then BSA archives). Falls back to the base record's Object Bounds (OBND) when the mesh cannot be read. If disabled, only OBND is used.")]
    public bool UseNifBounds { get; set; } = true;

    [SynthesisSettingName("Keep referenced objects")]
    [SynthesisTooltip("Do not remove target objects that other placed objects link to (Enable Parent, Linked Reference, Activate Parent, door teleport destination, script properties or any other reference field), or that are teleport doors. Quest aliases, packages and scripts on non-placed records are not checked. Kept objects are listed in the log.")]
    public bool SkipReferencedObjects { get; set; } = true;

    [SynthesisSettingName("Remove touching objects")]
    [SynthesisTooltip("Also remove target plugin objects whose mesh physically touches a removed target object, repeatedly, so whole touching groups go together (e.g. a tree standing on a removed rock does not stay floating). Only target plugin objects in the same cell/worldspace are considered. Touching is decided from the actual mesh triangles; objects without a readable mesh never count as touching.")]
    public bool RemoveTouchingObjects { get; set; } = true;

    [SynthesisSettingName("Touch tolerance")]
    [SynthesisTooltip("Maximum gap, in game units, between two meshes' surfaces for them to count as touching. Must be 0 or more. The test is exact: gaps up to this value count, larger gaps never do.")]
    public float TouchTolerance { get; set; } = 8f;

    [SynthesisSettingName("Verbose logging")]
    [SynthesisTooltip("Log every removed object with its base, cell/worldspace and the conflicting object, plus per-space counts and unreadable meshes.")]
    public bool VerboseLogging { get; set; }

    [SynthesisSettingName("Ignore replaced objects")]
    [SynthesisTooltip("Do not treat another mod's object as an 'other mod' object for proximity purposes when a target plugin object in the same cell/worldspace sits at essentially the same position and has a similar size (it looks like the target plugin replaced it). Records the target plugin itself overrides are always ignored this way, regardless of this setting.")]
    public bool IgnoreReplacedObjects { get; set; } = true;

    [SynthesisSettingName("Replacement position tolerance")]
    [SynthesisTooltip("Maximum distance, in game units, between a target plugin object's position and another mod's object's position for the other object to be considered a possible replacement. Must be 0 or more.")]
    public float ReplacementPositionTolerance { get; set; } = 16f;

    [SynthesisSettingName("Replacement size similarity")]
    [SynthesisTooltip("How similar in size (scaled bounds) a target plugin object and another mod's object must be to be considered a replacement match: the smallest-to-largest ratio of each pair of matching dimensions must be at least this value (0.75 = within about 25%). Clamped to 0-1.")]
    public float ReplacementSizeSimilarity { get; set; } = RunConfig.DefaultReplacementSizeSimilarity;

    [SynthesisSettingName("Touch diagnostics file")]
    [SynthesisTooltip("Optional: path of a file to write touch-diagnostics CSVs to, so touching chains can be judged from the output alone (without xEdit). Leave empty to write nothing (default). Writing them never changes the results but adds run time; a write error is only a warning. Relative paths resolve against the patcher's working directory; an absolute path is recommended. Two files are written: '<path>.edges.csv' (every touching edge within a touching component, including seed-to-seed touches) and '<path>.components.csv' (one row per touching component). See the README for the column layout.")]
    public string TouchDiagnosticsFile { get; set; } = string.Empty;
}
