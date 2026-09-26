using System.Numerics;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>A visible target object near a point: its distance to the object's box and the side it lies on.</summary>
internal readonly record struct VisibleNeighbour(int TargetIndex, float Distance, Quadrant Quadrant);

/// <summary>
/// The target plugin's visible objects of each space as oriented boxes, for finding the visible
/// objects whose box comes within a distance of a point. Read-only after construction and
/// therefore safe to query from many threads at once.
/// </summary>
internal sealed class VisibleTargetIndex
{
    private sealed record SpaceEntries(int[] TargetIndices, OrientedBox[] Boxes, SpatialGrid Grid);

    private readonly Dictionary<FormKey, SpaceEntries> _bySpace;

    private VisibleTargetIndex(Dictionary<FormKey, SpaceEntries> bySpace)
    {
        _bySpace = bySpace;
    }

    /// <param name="visibility">Parallel to <paramref name="targets"/>.</param>
    public static VisibleTargetIndex Build(
        IReadOnlyList<TargetObject> targets,
        IReadOnlyList<ObjectVisibility> visibility,
        BaseObjectShapeProvider shapes)
    {
        var bySpace = Enumerable.Range(0, targets.Count)
            .Where(index => visibility[index].IsVisible)
            .GroupBy(index => targets[index].SpaceKey)
            .ToDictionary(group => group.Key, group => CreateSpaceEntries(group.ToArray(), targets, shapes));
        return new VisibleTargetIndex(bySpace);
    }

    private static SpaceEntries CreateSpaceEntries(int[] targetIndices, IReadOnlyList<TargetObject> targets, BaseObjectShapeProvider shapes)
    {
        var boxes = targetIndices
            .Select(index => OrientedBox.FromLocal(shapes.GetLocalBox(targets[index].Base), targets[index].Transform))
            .ToArray();
        return new SpaceEntries(targetIndices, boxes, SpatialGrid.FromBoxes(boxes.Select(box => box.WorldAabb(0f)).ToArray()));
    }

    /// <summary>Visible target objects of the same space whose box lies within <paramref name="radius"/> of <paramref name="invisible"/>'s position.</summary>
    public List<VisibleNeighbour> FindAround(TargetObject invisible, float radius)
    {
        var neighbours = new List<VisibleNeighbour>();
        if (!_bySpace.TryGetValue(invisible.SpaceKey, out var space)) return neighbours;

        var origin = invisible.Transform.Position;
        foreach (var entry in FindCandidates(space, origin, radius))
        {
            var box = space.Boxes[entry];
            var closest = box.ClosestPoint(origin);
            var distance = Vector3.Distance(closest, origin);
            if (distance > radius) continue;
            neighbours.Add(new VisibleNeighbour(space.TargetIndices[entry], distance, QuadrantCounts.QuadrantOf(HorizontalDirection(origin, closest, box.Center))));
        }
        return neighbours;
    }

    /// <summary>Towards the closest point of the box, or towards its centre when the point is inside the box or right above or below it.</summary>
    private static Vector2 HorizontalDirection(Vector3 origin, Vector3 closest, Vector3 center)
    {
        var toClosest = new Vector2(closest.X - origin.X, closest.Y - origin.Y);
        return toClosest != Vector2.Zero ? toClosest : new Vector2(center.X - origin.X, center.Y - origin.Y);
    }

    /// <summary>Boxes can be indexed in several grid cells, so duplicates are removed.</summary>
    private static IEnumerable<int> FindCandidates(SpaceEntries space, Vector3 origin, float radius)
    {
        var candidates = new List<int>();
        space.Grid.Collect(new Box(origin, origin).Grown(radius), candidates);
        return candidates.Distinct();
    }
}
