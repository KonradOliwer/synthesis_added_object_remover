using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.ObjectsThatCanCauseRemovals.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.TooCloseToOtherModsObjects.Contracts;

/// <summary>The objects a too-close check measures the target objects among.</summary>
/// <param name="Selected">Each check takes the objects that can cause removals for its own NPC handling.</param>
/// <param name="PlacedNpcs">The check leaves out the NPCs the replacements in <paramref name="Selected"/> replace.</param>
public sealed record TooCloseWorld(
    IReadOnlyList<TargetObject> Targets,
    IVisibleTargetObjects VisibleTargets,
    SelectedObjects Selected,
    IPlacedNpcsBySpace PlacedNpcs,
    IBaseObjectShapes Shapes,
    ITriangleMeshes Triangles);
