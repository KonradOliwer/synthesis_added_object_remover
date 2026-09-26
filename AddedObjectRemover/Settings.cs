using Mutagen.Bethesda.Synthesis.Settings;

namespace AddedObjectRemover;

public enum FollowUpRemovalMode
{
    Off,
    AnyTouch,
    Anchoring,
}

public class Settings
{
    [SynthesisSettingName("What to check")]
    public CheckSettings WhatToCheck { get; set; } = new();

    [SynthesisSettingName("What to ignore")]
    public IgnoreSettings WhatToIgnore { get; set; } = new();

    [SynthesisSettingName("Follow-up removal")]
    public FollowUpRemovalSettings FollowUpRemoval { get; set; } = new();

    [SynthesisSettingName("Diagnostics")]
    public DiagnosticsSettings Diagnostics { get; set; } = new();
}

public class CheckSettings
{
    [SynthesisSettingName("Target plugin")]
    [SynthesisTooltip("File name of the plugin whose added objects are checked, e.g. 'SomeMod.esp'; when empty or not in the load order, nothing is changed.")]
    public string TargetPlugin { get; set; } = string.Empty;

    [SynthesisSettingName("Size multiplier")]
    [SynthesisTooltip("How far past its own edges a target object reaches, as a fraction of its size (0.5 = half its size further out on every side); another mod's object whose centre falls inside makes it too close.")]
    public float SizeMultiplier { get; set; } = 0.5f;
}

public class IgnoreSettings
{
    [SynthesisSettingName("Excluded plugins")]
    [SynthesisTooltip("Plugins whose objects never count as a conflict (the base game plugins are always ignored).")]
    public List<string> ExcludedPlugins { get; set; } = [];

    [SynthesisSettingName("Ignore the target's masters")]
    [SynthesisTooltip("Also ignore objects from the plugins the target plugin was built on (its masters).")]
    public bool IgnoreTargetMasters { get; set; } = true;
}

public class FollowUpRemovalSettings
{
    public const float DefaultAnchoringThresholdPercent = 50f;

    [SynthesisSettingName("Follow-up removal mode")]
    [SynthesisTooltip("What happens to target objects touching a removed one: Off keeps them, AnyTouch removes every touching object, Anchoring removes only objects that lose most of their support.")]
    public FollowUpRemovalMode Mode { get; set; } = FollowUpRemovalMode.AnyTouch;

    [SynthesisSettingName("Touch distance")]
    [SynthesisTooltip("Largest gap, in game units, between two surfaces for them to count as touching (used by AnyTouch and Anchoring).")]
    public float TouchDistance { get; set; } = 8f;

    [SynthesisSettingName("Anchoring threshold")]
    [SynthesisTooltip("Anchoring only: an object is removed when at least this percentage (1-99) of its support comes from removed objects.")]
    public float AnchoringThresholdPercent { get; set; } = DefaultAnchoringThresholdPercent;
}

public class DiagnosticsSettings
{
    [SynthesisSettingName("Detailed log")]
    [SynthesisTooltip("Log every removed and kept object with its reason, per-space counts and unreadable meshes.")]
    public bool DetailedLog { get; set; }

    [SynthesisSettingName("Diagnostics folder")]
    [SynthesisTooltip("Folder to write diagnostics spreadsheets (CSV files) to; leave empty to write nothing.")]
    public string DiagnosticsFolder { get; set; } = string.Empty;
}
