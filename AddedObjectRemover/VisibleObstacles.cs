using System.Numerics;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>
/// The visible placed objects that remain after this run: every non-target object of any plugin
/// and the target's visible objects that are not removed. Thread-safe.
/// </summary>
internal sealed class VisibleObstacles(
    SupporterIndex nonTargetObjects,
    VisibleTargetIndex remainingTargets,
    ObjectContainment containment,
    BaseObjectShapeProvider shapes)
{
    public bool IsInsideAny(FormKey spaceKey, Vector3 point, SpatialQueryScratch scratch) =>
        containment.FindContainingVisible(nonTargetObjects.GetSpace(spaceKey), point, skipReplaced: false, scratch) >= 0
        || remainingTargets.AnyContains(spaceKey, point, containment, scratch);

    /// <summary>
    /// The boxes of the obstacles that may come within <paramref name="radius"/> of
    /// <paramref name="point"/>, and possibly a few farther ones; none whose world AABB lies
    /// horizontally farther away.
    /// </summary>
    public List<OrientedBox> FindBoxesNear(FormKey spaceKey, Vector3 point, float radius, SpatialQueryScratch scratch) =>
        FindNonTargetBoxesNear(spaceKey, point, radius, scratch)
            .Concat(remainingTargets.FindBoxesNear(spaceKey, point, radius))
            .Where(box => IsHorizontallyWithin(box.WorldAabb(0f), point, radius))
            .ToList();

    private List<OrientedBox> FindNonTargetBoxesNear(FormKey spaceKey, Vector3 point, float radius, SpatialQueryScratch scratch)
    {
        var index = nonTargetObjects.GetSpace(spaceKey);
        index.Bounds.CollectCandidates(new Box(point, point).Grown(radius), scratch.Slots, scratch.Candidates);
        return scratch.Candidates
            .Where(index.IsVisible)
            .Select(candidate => OrientedBox.FromLocal(shapes.GetLocalBox(index[candidate].Base), index[candidate].Transform))
            .ToList();
    }

    private static bool IsHorizontallyWithin(Box box, Vector3 point, float radius)
    {
        var belowMin = new Vector2(box.Min.X - point.X, box.Min.Y - point.Y);
        var aboveMax = new Vector2(point.X - box.Max.X, point.Y - box.Max.Y);
        return Vector2.Max(Vector2.Zero, Vector2.Max(belowMin, aboveMax)).Length() <= radius;
    }
}
