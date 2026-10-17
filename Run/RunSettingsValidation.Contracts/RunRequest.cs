using System.Collections.Immutable;

namespace AddedObjectRemover.Run.RunSettingsValidation.Contracts;

/// <summary>A setting that was invalid and replaced, or a load-order fact that affects the run.</summary>
/// <param name="Setting">What the warning is about.</param>
/// <param name="Message">The log line, as printed.</param>
public sealed record SettingWarning(string Setting, string Message);

/// <summary>Whether a run can go ahead, with the warnings found on the way, in the order found.</summary>
public abstract record RunRequest(SettingsSource Source, ImmutableArray<SettingWarning> Warnings)
{
    /// <summary>The run makes no changes.</summary>
    public sealed record Stop(StopReason Reason, SettingsSource Source, ImmutableArray<SettingWarning> Warnings) : RunRequest(Source, Warnings);

    public sealed record Ready(RunSettings Settings, SettingsSource Source, ImmutableArray<SettingWarning> Warnings) : RunRequest(Source, Warnings);
}
