using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.MoveKeptMarkers;

/// <summary>
/// The visible placed objects that remain after this run: every visible object of any plugin and the
/// visible target objects that are not removed. Thread-safe.
/// </summary>
/// <param name="removed">By <see cref="TargetId"/> index.</param>
internal sealed class OtherObjectsAroundMarker(
    IVisibleObjectsOfAnyPlugin objectsOfAnyPlugin, IVisibleTargetObjects targets, IReadOnlySet<int> removed, IBaseObjectShapes shapes)
{
    public bool IsInsideAny(RecordKey spaceKey, Vector3 point, ObjectQueryScratch scratch) =>
        objectsOfAnyPlugin.Contains(spaceKey, point, scratch) || targets.AnyContains(spaceKey, point, Remains, scratch);

    /// <summary>
    /// The boxes of the remaining visible objects that may come within <paramref name="radius"/> of
    /// <paramref name="point"/>, and possibly a few farther ones; none whose world AABB lies
    /// horizontally farther away.
    /// </summary>
    public List<OrientedBox> FindBoxesNear(RecordKey spaceKey, Vector3 point, float radius, ObjectQueryScratch scratch) =>
        FindObjectOfAnyPluginBoxesNear(spaceKey, point, radius, scratch)
            .Concat(targets.BoxesNear(spaceKey, point, radius, Remains))
            .Where(box => Boxes.HorizontalDistance(box.WorldAabb(0f), point) <= radius)
            .ToList();

    private bool Remains(TargetId target) => !removed.Contains(target.Index);

    private List<OrientedBox> FindObjectOfAnyPluginBoxesNear(RecordKey spaceKey, Vector3 point, float radius, ObjectQueryScratch scratch)
    {
        objectsOfAnyPlugin.Overlapping(spaceKey, new Box(point, point).Grown(radius), scratch, scratch.Others);
        return scratch.Others
            .Select(objectsOfAnyPlugin.Get)
            .Select(other => OrientedBox.FromLocal(shapes.Of(other.Base).Box, other.Transform))
            .ToList();
    }
}
