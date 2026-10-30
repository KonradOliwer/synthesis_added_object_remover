using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.JudgeChangedObjects.Contracts;
using AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

/// <summary>Every decision of a run, from the setup through the relocations.</summary>
/// <param name="RemovalRisks">The target objects whose removal the log warns about.</param>
/// <param name="ChangedObjects">What a removal does with each target object, with the old-place checks and the ones that failed.</param>
/// <param name="OldPlaceWorld">The objects the old-place checks were made among, which their collisions' ids refer to; null when no check was made.</param>
/// <param name="OtherModsObjects">What became of the other mods' objects left without support.</param>
/// <param name="RestingObjects">The also-remove rounds that follow the removals before the visible left-behind step.</param>
/// <param name="Final">The removal decisions with every round and every move.</param>
public record StepResults(
    CollectedObjects Collected,
    IObjectsToKeep ObjectsToKeep,
    RemovalRisks RemovalRisks,
    ChangedObjectJudgingResult ChangedObjects,
    CollectedObjects? OldPlaceWorld,
    ChangedTerrainResult ChangedTerrain,
    RestingObjectsResult RestingObjects,
    VisibleLeftBehindResult VisibleLeftBehind,
    LeftBehindResult LeftBehind,
    MarkerMoves MarkerMoves,
    OtherModsObjectsResult OtherModsObjects,
    IRemovalDecisions Final);
