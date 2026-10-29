using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects;

/// <param name="Selected">Hands out the other mods' objects that can cause removals for each pass's NPC handling.</param>
/// <param name="Npcs">Read only when NPCs count only when stuck in an object.</param>
internal sealed record TooCloseInput(
    ImmutableArray<TargetObject> Targets,
    IVisibleTargetObjects VisibleTargets,
    WorkOrder Order,
    SelectedObjects Selected,
    INpcsThatCanSpawn Npcs,
    IBaseObjectShapes Shapes,
    ITriangleMeshes Triangles,
    Execution Exec);
