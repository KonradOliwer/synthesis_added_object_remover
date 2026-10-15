using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Run.RunAllSteps.Contracts;

/// <summary>How a run ended.</summary>
public abstract record RunResult
{
    /// <summary>The run made no changes.</summary>
    public sealed record Stopped(StopReason Reason) : RunResult;

    public sealed record Done(RunOutcome Outcome) : RunResult;
}
