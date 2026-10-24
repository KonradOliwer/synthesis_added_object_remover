using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

/// <param name="Targets">Where they stand: an object kept shrunk or turned at its new placement.</param>
/// <param name="EarlierRounds">The run's decisions with the removal rounds of the steps before this one; every one of those rounds seeds the step, which never adds to them.</param>
/// <param name="TimeSinceStart">The run's clock, which measures the rounds.</param>
/// <param name="ReportFiles">Whether the report files are written; the report-file details are computed only when they are performed.</param>
public sealed record RestingObjectsInput(
    ImmutableArray<TargetObject> Targets,
    IObjectsToKeep ObjectsToKeep,
    IRemovalDecisions EarlierRounds,
    AlsoRemoveSettings Settings,
    Execution Execution,
    Func<TimeSpan> TimeSinceStart,
    StepOutcome ReportFiles);
