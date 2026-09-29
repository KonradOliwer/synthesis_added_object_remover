using System.Collections.Immutable;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>What the load-order read collects beyond the target objects and their rivals.</summary>
/// <param name="Backdrop">Every other placed object of the target spaces, as possible supporters or obstacles.</param>
/// <param name="Terrain">The terrain of the target worldspaces.</param>
/// <param name="Navmesh">The winning navmeshes of the target spaces.</param>
/// <param name="OtherEditorIds">The Editor IDs of other mods' objects.</param>
/// <param name="OverriddenOthersList">The list of other mods' objects the target overrides.</param>
internal sealed record ReadPlan(bool Backdrop, bool Terrain, bool Navmesh, bool OtherEditorIds, bool OverriddenOthersList);

/// <param name="Folder">Always resolved, so the log can show it even when no files are written.</param>
internal sealed record ReportOptions(bool WriteFiles, string Folder);

/// <summary>The validated settings of one run.</summary>
/// <param name="ExcludedNames">The excluded plugins as parsed from the settings, loaded or not, for the settings echo.</param>
/// <param name="Leftovers">Null when the leftover invisible objects step is off.</param>
/// <param name="Relocation">Null unless kept markers are moved, which needs the leftover step.</param>
internal sealed record RunOptions(
    ModRef Target,
    ModKey TargetKey,
    ImmutableArray<string> ExcludedNames,
    StandingOptions Standing,
    ReadPlan Read,
    ClashOptions Clash,
    FollowUpOptions FollowUp,
    LeftoverOptions? Leftovers,
    RelocationOptions? Relocation,
    ReportOptions Reports,
    bool DetailedLog,
    Execution Execution);

/// <summary>A setting that was invalid and replaced, or a load-order fact that affects the run.</summary>
/// <param name="Setting">What the warning is about.</param>
/// <param name="Message">The log line, as printed.</param>
internal sealed record SettingWarning(string Setting, string Message);

internal enum StopKind { NoTargetSet, InvalidTargetName, TargetNotInLoadOrder, NoTargetObjects }

/// <param name="Name">The target plugin as the settings name it; null when there is none to name.</param>
internal sealed record StopReason(StopKind Kind, string? Name);

/// <summary>Whether a run can go ahead, with the warnings found on the way, in the order found.</summary>
internal abstract record OptionsResult(ImmutableArray<SettingWarning> Warnings)
{
    /// <summary>The run makes no changes.</summary>
    public sealed record Stop(StopReason Reason, ImmutableArray<SettingWarning> Warnings) : OptionsResult(Warnings);

    public sealed record Ready(RunOptions Options, ImmutableArray<SettingWarning> Warnings) : OptionsResult(Warnings);
}
