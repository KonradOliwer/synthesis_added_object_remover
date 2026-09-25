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
    [SynthesisTooltip("Do not remove target objects that other placed objects use as Enable Parent or Linked Reference, that are teleport doors, or that are the teleport destination of another door. These are listed in the log.")]
    public bool SkipReferencedObjects { get; set; } = true;

    [SynthesisSettingName("Verbose logging")]
    [SynthesisTooltip("Log every removed object with its base, cell/worldspace and the conflicting object, plus per-space counts and unreadable meshes.")]
    public bool VerboseLogging { get; set; } = false;
}
