using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static class InvisibleOtherObjectCounter
{
    public static InvisibleOtherObjectCounts Take(CollectedObjects world, IBaseObjectShapes shapes, IVisibleTargetObjects visibleTargets)
    {
        var targetSpaces = world.TargetSpaces();
        var visibleTargetSpaces = visibleTargets.Spaces.ToHashSet();
        var invisibleOtherModObjects = world.OtherModObjects
            .Where(other => visibleTargetSpaces.Contains(other.SpaceKey))
            .Select(other => shapes.VisibilityOf(other.Base, other.IsPrimitive, other.HasMapMarker))
            .Where(visibility => !visibility.IsVisible);
        var invisibleByReason = KeyedGroups.Rank(
            KeyedGroups.CountBy(invisibleOtherModObjects, ObjectVisibilityText.Describe, StringComparer.Ordinal),
            StringComparer.Ordinal);
        var placedNpcs = world.OtherModObjects.Count(other => targetSpaces.Contains(other.SpaceKey) && other.IsPlacedNpc);
        return new InvisibleOtherObjectCounts(invisibleByReason, placedNpcs);
    }
}
