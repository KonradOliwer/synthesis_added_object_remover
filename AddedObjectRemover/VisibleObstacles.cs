using System.Numerics;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>
/// The visible placed objects that remain after this run: every solid and the visible target
/// objects that are not removed. Thread-safe.
/// </summary>
/// <param name="removed">By <see cref="TargetId"/> index.</param>
internal sealed class VisibleObstacles(ISolids solids, IVisibleTargets targets, IReadOnlySet<int> removed, ShapeCatalog shapes)
{
    public bool IsInsideAny(FormKey spaceKey, Vector3 point, SpatialQueryScratch scratch) =>
        solids.Contains(spaceKey, point, scratch) || targets.AnyContains(spaceKey, point, Remains, scratch);

    /// <summary>
    /// The boxes of the obstacles that may come within <paramref name="radius"/> of
    /// <paramref name="point"/>, and possibly a few farther ones; none whose world AABB lies
    /// horizontally farther away.
    /// </summary>
    public List<OrientedBox> FindBoxesNear(FormKey spaceKey, Vector3 point, float radius, SpatialQueryScratch scratch) =>
        FindSolidBoxesNear(spaceKey, point, radius, scratch)
            .Concat(targets.BoxesNear(spaceKey, point, radius, Remains))
            .Where(box => IsHorizontallyWithin(box.WorldAabb(0f), point, radius))
            .ToList();

    private bool Remains(TargetId target) => !removed.Contains(target.Index);

    private List<OrientedBox> FindSolidBoxesNear(FormKey spaceKey, Vector3 point, float radius, SpatialQueryScratch scratch)
    {
        solids.Overlapping(spaceKey, new Box(point, point).Grown(radius), scratch, scratch.Others);
        return scratch.Others
            .Select(solids.Get)
            .Select(solid => OrientedBox.FromLocal(shapes.GetLocalBox(solid.Base), solid.Transform))
            .ToList();
    }

    private static bool IsHorizontallyWithin(Box box, Vector3 point, float radius)
    {
        var belowMin = new Vector2(box.Min.X - point.X, box.Min.Y - point.Y);
        var aboveMax = new Vector2(point.X - box.Max.X, point.Y - box.Max.Y);
        return Vector2.Max(Vector2.Zero, Vector2.Max(belowMin, aboveMax)).Length() <= radius;
    }
}
