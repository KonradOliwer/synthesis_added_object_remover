using System.Numerics;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>
/// The target plugin's visible objects of each space, indexed by the X/Y of their world bounds
/// centre, for counting the visible scenery around a point. Read-only after construction and
/// therefore safe to query from many threads at once.
/// </summary>
internal sealed class VisibleTargetIndex
{
    private sealed record SpaceEntries(int[] TargetIndices, Vector3[] Centers, SpatialGrid Grid);

    private readonly Dictionary<FormKey, SpaceEntries> _bySpace;

    private VisibleTargetIndex(Dictionary<FormKey, SpaceEntries> bySpace)
    {
        _bySpace = bySpace;
    }

    /// <param name="isVisible">Parallel to <paramref name="targets"/>.</param>
    public static VisibleTargetIndex Build(IReadOnlyList<TargetObject> targets, IReadOnlyList<bool> isVisible, BaseObjectShapeProvider shapes)
    {
        var bySpace = Enumerable.Range(0, targets.Count)
            .Where(index => isVisible[index])
            .GroupBy(index => targets[index].SpaceKey)
            .ToDictionary(group => group.Key, group => CreateSpaceEntries(group.ToArray(), targets, shapes));
        return new VisibleTargetIndex(bySpace);
    }

    private static SpaceEntries CreateSpaceEntries(int[] targetIndices, IReadOnlyList<TargetObject> targets, BaseObjectShapeProvider shapes)
    {
        var centers = targetIndices.Select(index => GetBoundsCenter(targets[index], shapes)).ToArray();
        return new SpaceEntries(targetIndices, centers, SpatialGrid.FromPoints(centers));
    }

    private static Vector3 GetBoundsCenter(TargetObject target, BaseObjectShapeProvider shapes)
    {
        var center = Geometry.WorldBoundsCenter(shapes.GetLocalBox(target.Base), target.Transform);
        return Geometry.IsFinite(center) ? center : target.Transform.Position;
    }

    /// <summary>Visible target objects of the same space whose bounds centre lies within <paramref name="radius"/> horizontally.</summary>
    public QuadrantCounts CountAround(TargetObject invisible, float radius, IReadOnlySet<int> removedTargets)
    {
        var counts = new QuadrantCounts();
        if (!_bySpace.TryGetValue(invisible.SpaceKey, out var space)) return counts;

        var origin = new Vector2(invisible.Transform.Position.X, invisible.Transform.Position.Y);
        foreach (var entry in FindCandidates(space, origin, radius))
        {
            var center = space.Centers[entry];
            var offset = new Vector2(center.X, center.Y) - origin;
            if (offset.LengthSquared() > radius * radius) continue;
            counts.Add(QuadrantCounts.QuadrantOf(offset), removedTargets.Contains(space.TargetIndices[entry]));
        }
        return counts;
    }

    /// <summary>Each point is indexed in exactly one grid cell, so no entry is returned twice.</summary>
    private static List<int> FindCandidates(SpaceEntries space, Vector2 origin, float radius)
    {
        var area = new Box(
            new Vector3(origin.X - radius, origin.Y - radius, 0f),
            new Vector3(origin.X + radius, origin.Y + radius, 0f));
        var candidates = new List<int>();
        space.Grid.Collect(area, candidates);
        return candidates;
    }
}
