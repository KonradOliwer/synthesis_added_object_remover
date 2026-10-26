using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

/// <param name="Targets">Where they stand: an object kept shrunk or turned at its new placement.</param>
/// <param name="EarlierRounds">The run's decisions with the removal rounds of the steps before this one, which the step never adds to; the objects they kept never take part in the touch rounds.</param>
/// <param name="SeedRounds">The rounds of <paramref name="EarlierRounds"/> whose removals the step starts from, in round order.</param>
/// <param name="TimeSinceStart">The run's clock, which measures the rounds.</param>
/// <param name="ReportFiles">Whether the report files are written; the report-file details are computed only when they are performed.</param>
public sealed record RestingObjectsInput(
    ImmutableArray<TargetObject> Targets,
    IObjectsToKeep ObjectsToKeep,
    IRemovalDecisions EarlierRounds,
    ImmutableArray<Round> SeedRounds,
    AlsoRemoveSettings Settings,
    Execution Execution,
    Func<TimeSpan> TimeSinceStart,
    StepOutcome ReportFiles);
