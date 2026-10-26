using System.Collections.Immutable;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

public sealed partial class LogAndReports
{
    /// <param name="decisions">The run's decisions with the also-remove rounds applied.</param>
    /// <param name="elapsed">The whole step's time; the section's own time is that of the rounds alone.</param>
    public ImmutableArray<LogSection> BuildStepSections(RestingObjectsResult alsoRemove, IRemovalDecisions decisions, CollectedObjects world, TimeSpan elapsed) =>
        BuildAlsoRemoveSections(LogSections.BuildAlsoRemoveSection, LogSections.AlsoRemovePass.First, alsoRemove, decisions, world, elapsed);

    /// <param name="decisions">The run's decisions with the second also-remove pass's rounds applied.</param>
    /// <param name="elapsed">The whole step's time; the section's own time is that of the rounds alone.</param>
    /// <remarks>Prints nothing, not even an empty section, unless the pass started from a visible left-behind removal or was stopped.</remarks>
    public ImmutableArray<LogSection> BuildFollowUpAfterVisibleLeftBehindSections(
        RestingObjectsResult alsoRemove, IRemovalDecisions decisions, CollectedObjects world, TimeSpan elapsed) =>
        LogSections.PrintsAfterVisibleLeftBehind(alsoRemove)
            ? BuildAlsoRemoveSections(
                LogSections.BuildAlsoRemoveAfterVisibleLeftBehindSection, LogSections.AlsoRemovePass.AfterVisibleLeftBehind, alsoRemove, decisions, world, elapsed)
            : [];

    /// <summary>The also-remove section of one pass, its time, the line for the removals linked to its own, then its unexpected errors.</summary>
    private ImmutableArray<LogSection> BuildAlsoRemoveSections(
        Func<CollectedObjects, RestingObjectsResult, IRemovalDecisions, TriangleStoreStats, ReportContext, LogSection> buildSection,
        LogSections.AlsoRemovePass pass,
        RestingObjectsResult alsoRemove,
        IRemovalDecisions decisions,
        CollectedObjects world,
        TimeSpan elapsed) =>
        [
            WithStepTimeWhenPerformed(buildSection(world, alsoRemove, decisions, _caches.Triangles.GetStats(), _context), alsoRemove.Outcome, pass.StepName, elapsed),
            LogSections.BuildLinkedSection(decisions, alsoRemove.Rounds, pass.LinkedStep, pass.IdSuffix, _context),
            TakeUnexpectedErrors(Present(alsoRemove.Failure, alsoRemove.TouchChainsFailure)),
        ];
}
