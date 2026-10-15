using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

/// <param name="AfterTooClose">The run's decisions whose last round is the too-close round; the step never adds to them.</param>
/// <param name="TimeSinceStart">The run's clock, which measures the rounds.</param>
/// <param name="ReportFiles">Whether the report files are written; the report-file details are computed only when they are performed.</param>
public sealed record RestingObjectsInput(
    ImmutableArray<TargetObject> Targets,
    IObjectsToKeep ObjectsToKeep,
    IRemovalDecisions AfterTooClose,
    AlsoRemoveSettings Settings,
    Execution Execution,
    Func<TimeSpan> TimeSinceStart,
    StepOutcome ReportFiles);
