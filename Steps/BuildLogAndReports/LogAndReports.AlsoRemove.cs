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
        BuildAlsoRemoveSections(
            WithStepTimeWhenPerformed(
                LogSections.BuildAlsoRemoveSection(world, alsoRemove, decisions, _caches.Triangles.GetStats(), _context), alsoRemove.Outcome, LogSections.AlsoRemovePass.First.StepName, elapsed),
            LogSections.AlsoRemovePass.First,
            alsoRemove,
            decisions);

    /// <param name="decisions">The run's decisions with the follow-up rounds applied.</param>
    /// <remarks>
    /// Prints nothing, not even an empty section, unless a pass started from a visible left-behind removal or the follow-ups were stopped.
    /// The follow-ups are no timed step of their own: the section's rounds line carries their time, so it has no step-time line.
    /// </remarks>
    private ImmutableArray<LogSection> BuildFollowUpAfterVisibleLeftBehindSections(RestingObjectsResult alsoRemove, IRemovalDecisions decisions, CollectedObjects world) =>
        LogSections.PrintsAfterVisibleLeftBehind(alsoRemove)
            ? BuildAlsoRemoveSections(
                LogSections.BuildAlsoRemoveAfterVisibleLeftBehindSection(world, alsoRemove, decisions, _caches.Triangles.GetStats(), _context),
                LogSections.AlsoRemovePass.AfterVisibleLeftBehind,
                alsoRemove,
                decisions)
            : [];

    /// <summary>The also-remove section of one pass, the line for the removals linked to its own, then its unexpected errors.</summary>
    private ImmutableArray<LogSection> BuildAlsoRemoveSections(
        LogSection section, LogSections.AlsoRemovePass pass, RestingObjectsResult alsoRemove, IRemovalDecisions decisions) =>
        [
            section,
            LogSections.BuildLinkedSection(decisions, alsoRemove.Rounds, pass.LinkedStep, pass.IdSuffix, _context),
            TakeUnexpectedErrors(Present(alsoRemove.Failure, alsoRemove.TouchChainsFailure)),
        ];
}
