using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects;

/// <summary>Counts the other-mod objects no check of the step measures against: the invisible ones, and the placed NPCs of the target spaces.</summary>
internal static class OtherObjectsLeftOutCounter
{
    public static OtherObjectsLeftOut Take(CollectedObjects world, IBaseObjectShapes shapes, IVisibleTargetObjects visibleTargets)
    {
        var targetSpaces = world.TargetSpaces();
        var visibleTargetSpaces = visibleTargets.Spaces.ToHashSet();
        var invisible = world.OtherModObjects
            .Where(other => visibleTargetSpaces.Contains(other.SpaceKey))
            .Select(other => shapes.VisibilityOf(other))
            .Where(visibility => !visibility.IsVisible);
        var placedNpcs = world.OtherModObjects.Count(other => targetSpaces.Contains(other.SpaceKey) && other.IsPlacedNpc);
        return new OtherObjectsLeftOut(
            [.. KeyedGroups.CountBy(invisible, visibility => visibility, EqualityComparer<ObjectVisibility>.Default).Select(entry => new InvisibleObjectCount(entry.Key, entry.Value))],
            placedNpcs);
    }
}
