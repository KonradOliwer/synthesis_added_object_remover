using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.MoveKeptMarkers;

/// <summary>What a moved marker must stay clear of.</summary>
/// <param name="ObjectsOfAnyPlugin">Every visible object that is not a target object, replaced other-mod objects included.</param>
/// <param name="Targets">The visible target objects; those <paramref name="Final"/> removes are not in the way.</param>
internal sealed record MarkerSurroundings(IVisibleObjectsOfAnyPlugin ObjectsOfAnyPlugin, IVisibleTargetObjects Targets, IRemovalDecisions Final);

/// <param name="Candidates">The kept markers to try to move, in target order.</param>
/// <param name="Plugin">For the exterior cell each marker belongs to.</param>
/// <param name="Navmeshes">The winning navmeshes of each target space.</param>
internal sealed record MarkerMoveInput(
    ImmutableArray<TargetObject> Targets,
    ImmutableArray<LeftBehindCheck> Candidates,
    MarkerSurroundings Surroundings,
    IPluginRecords Plugin,
    INavmeshes Navmeshes,
    ITerrainHeights Terrain,
    IBaseObjectShapes Shapes,
    Execution Exec);
