using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Run.RunAllSteps.Contracts;

/// <summary>How a run ended.</summary>
public abstract record RunResult
{
    /// <summary>The run made no changes.</summary>
    public sealed record Stopped(StopReason Reason) : RunResult;

    /// <param name="Details">The evidence behind the report files; none when no files are written.</param>
    public sealed record Done(RunOutcome Outcome, ReportFileDetails Details) : RunResult;
}
