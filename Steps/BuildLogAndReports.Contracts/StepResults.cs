using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

/// <summary>Every decision of a run, from the setup through the relocations.</summary>
/// <param name="Final">The removal decisions with every round and every move.</param>
public record StepResults(
    CollectedObjects Collected,
    IObjectsToKeep ObjectsToKeep,
    ChangedTerrainResult ChangedTerrain,
    RestingObjectsResult RestingObjects,
    LeftBehindResult LeftBehind,
    MarkerMoves MarkerMoves,
    IRemovalDecisions Final);
