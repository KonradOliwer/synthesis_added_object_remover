using System.Numerics;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>A visible target object near a point: the directions it counts in and its ground footprint area.</summary>
/// <param name="Sectors">The one direction it lies in, or all of them when its box contains the point.</param>
internal readonly record struct VisibleNeighbour(int TargetIndex, IReadOnlyList<DirectionSector> Sectors, float FootprintArea);

/// <summary>
/// Visible target objects of each space as oriented boxes, for finding the ones whose box comes
/// within a distance of a point or contains it. Read-only after construction and therefore safe
/// to query from many threads at once.
/// </summary>
internal sealed class VisibleTargetIndex
{
    private sealed record SpaceEntries(int[] TargetIndices, OrientedBox[] Boxes, SpatialGrid Grid);

    private readonly IReadOnlyList<TargetObject> _targets;
    private readonly Dictionary<FormKey, SpaceEntries> _bySpace;

    private VisibleTargetIndex(IReadOnlyList<TargetObject> targets, Dictionary<FormKey, SpaceEntries> bySpace)
    {
        _targets = targets;
        _bySpace = bySpace;
    }

    /// <param name="visibleIndices">The visible targets to index.</param>
    public static VisibleTargetIndex Build(
        IReadOnlyList<TargetObject> targets,
        IEnumerable<int> visibleIndices,
        BaseObjectShapeProvider shapes)
    {
        var bySpace = visibleIndices
            .GroupBy(index => targets[index].SpaceKey)
            .ToDictionary(group => group.Key, group => CreateSpaceEntries(group.ToArray(), targets, shapes));
        return new VisibleTargetIndex(targets, bySpace);
    }

    private static SpaceEntries CreateSpaceEntries(int[] targetIndices, IReadOnlyList<TargetObject> targets, BaseObjectShapeProvider shapes)
    {
        var boxes = targetIndices
            .Select(index => OrientedBox.FromLocal(shapes.GetLocalBox(targets[index].Base), targets[index].Transform))
            .ToArray();
        return new SpaceEntries(targetIndices, boxes, SpatialGrid.FromBoxes(boxes.Select(box => box.WorldAabb(0f)).ToArray()));
    }

    /// <summary>Visible target objects of the space whose box lies within <paramref name="radius"/> of <paramref name="point"/>.</summary>
    public List<VisibleNeighbour> FindAround(FormKey spaceKey, Vector3 point, float radius)
    {
        var neighbours = new List<VisibleNeighbour>();
        if (!_bySpace.TryGetValue(spaceKey, out var space)) return neighbours;

        foreach (var entry in FindCandidates(space, point, radius))
        {
            var box = space.Boxes[entry];
            var closest = box.ClosestPoint(point);
            if (Vector3.Distance(closest, point) > radius) continue;
            IReadOnlyList<DirectionSector> sectors = closest == point ? SectorAreas.All : [SectorAreas.SectorOf(HorizontalDirection(point, closest, box.Center))];
            neighbours.Add(new VisibleNeighbour(space.TargetIndices[entry], sectors, box.FootprintArea));
        }
        return neighbours;
    }

    /// <summary>True when some indexed object of the space contains <paramref name="point"/>.</summary>
    public bool AnyContains(FormKey spaceKey, Vector3 point, ObjectContainment containment)
    {
        if (!_bySpace.TryGetValue(spaceKey, out var space)) return false;
        return FindCandidates(space, point, radius: 0f)
            .Select(entry => _targets[space.TargetIndices[entry]])
            .Any(target => containment.Contains(target.Base, target.Transform, point));
    }

    /// <summary>The boxes of the space that may come within <paramref name="radius"/> of <paramref name="point"/>, and possibly a few farther ones.</summary>
    public IEnumerable<OrientedBox> FindBoxesNear(FormKey spaceKey, Vector3 point, float radius) =>
        _bySpace.TryGetValue(spaceKey, out var space)
            ? FindCandidates(space, point, radius).Select(entry => space.Boxes[entry])
            : [];

    /// <summary>Towards the closest point of the box, or towards its centre when the point is right above or below it.</summary>
    private static Vector2 HorizontalDirection(Vector3 origin, Vector3 closest, Vector3 center)
    {
        var toClosest = new Vector2(closest.X - origin.X, closest.Y - origin.Y);
        return toClosest != Vector2.Zero ? toClosest : new Vector2(center.X - origin.X, center.Y - origin.Y);
    }

    /// <summary>Boxes can be indexed in several grid cells, so duplicates are removed.</summary>
    private static IEnumerable<int> FindCandidates(SpaceEntries space, Vector3 point, float radius)
    {
        var candidates = new List<int>();
        space.Grid.Collect(new Box(point, point).Grown(radius), candidates);
        return candidates.Distinct();
    }
}
