using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

/// <summary>Every decision of a run, from the setup through the relocations.</summary>
/// <param name="TouchChains">Null unless the touch rounds ran and the detailed log or the report files need them.</param>
/// <param name="Final">The removal decisions with every round and every move.</param>
public record StepResults(
    CollectedObjects World,
    InvisibleOtherObjectCounts Census,
    Replacements Replacements,
    IObjectsToKeep Protection,
    TooCloseResult TooClose,
    RestingObjectsResult RestingObjects,
    TouchChainSet? TouchChains,
    LeftBehindResult LeftBehind,
    MarkerMoves MarkerMoves,
    IRemovalDecisions Final);
