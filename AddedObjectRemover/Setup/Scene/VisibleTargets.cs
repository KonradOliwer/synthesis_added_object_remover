using System.Numerics;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>
/// The visible target objects of each space as oriented boxes, indexed per space on first use.
/// Thread-safe.
/// </summary>
internal sealed class VisibleTargets : IVisibleTargets
{
    /// <param name="Targets">Ascending, so ascending entries list the targets in id order.</param>
    private sealed record SpaceEntries(TargetId[] Targets, OrientedBox[] Boxes, SpatialGrid Grid);

    private readonly IReadOnlyList<TargetObject> _targets;
    private readonly ShapeCatalog _shapes;
    private readonly ObjectContainment _containment;
    private readonly Dictionary<FormKey, TargetId[]> _visibleBySpace;
    private readonly LazyCache<FormKey, SpaceEntries> _entries = new();

    /// <param name="looks">Parallel to <paramref name="targets"/>.</param>
    public VisibleTargets(
        IReadOnlyList<TargetObject> targets, TargetLooks looks, ShapeCatalog shapes, ObjectContainment containment)
    {
        _targets = targets;
        _shapes = shapes;
        _containment = containment;
        _visibleBySpace = targets
            .Where(target => looks.ByTarget[target.Id.Index].IsVisible)
            .GroupBy(target => target.SpaceKey)
            .ToDictionary(group => group.Key, group => group.Select(target => target.Id).ToArray());
    }

    public void Around(FormKey space, Vector3 point, float radius, List<VisibleNeighbour> into)
    {
        into.Clear();
        if (EntriesOf(space) is not { } entries) return;

        foreach (var entry in FindCandidates(entries, point, radius))
        {
            var box = entries.Boxes[entry];
            if (Vector3.Distance(box.ClosestPoint(point), point) > radius) continue;
            into.Add(new VisibleNeighbour(entries.Targets[entry], FindSectors(box, point), box.FootprintArea));
        }
    }

    /// <summary>
    /// Every direction when the box contains the point. Otherwise exactly one: towards the box
    /// centre's horizontal offset when the box lies straight above or below the point (however
    /// small that offset), else towards the box's closest point.
    /// </summary>
    internal static IReadOnlyList<DirectionSector> FindSectors(OrientedBox box, Vector3 point)
    {
        if (box.Contains(point)) return SectorAreas.All;
        var towards = box.IsCrossedByVerticalLine(point) ? box.Center : box.ClosestPoint(point);
        return [SectorAreas.SectorOf(new Vector2(towards.X - point.X, towards.Y - point.Y))];
    }

    public bool AnyContains(FormKey space, Vector3 point, Func<TargetId, bool> include, SpatialQueryScratch scratch)
    {
        if (EntriesOf(space) is not { } entries) return false;
        return FindCandidates(entries, point, radius: 0f)
            .Select(entry => entries.Targets[entry])
            .Where(include)
            .Select(id => _targets[id.Index])
            .Any(target => _containment.Contains(target.Base, target.Transform, point, scratch));
    }

    public IEnumerable<OrientedBox> BoxesNear(FormKey space, Vector3 point, float radius, Func<TargetId, bool> include) =>
        EntriesOf(space) is { } entries
            ? FindCandidates(entries, point, radius).Where(entry => include(entries.Targets[entry])).Select(entry => entries.Boxes[entry])
            : [];

    private SpaceEntries? EntriesOf(FormKey space) =>
        _visibleBySpace.TryGetValue(space, out var visible) ? _entries.GetOrCreate(space, () => CreateEntries(visible)) : null;

    private SpaceEntries CreateEntries(TargetId[] visible)
    {
        var boxes = visible
            .Select(id => OrientedBox.FromLocal(_shapes.GetLocalBox(_targets[id.Index].Base), _targets[id.Index].Transform))
            .ToArray();
        return new SpaceEntries(visible, boxes, SpatialGrid.FromBoxes(boxes.Select(box => box.WorldAabb(0f)).ToArray()));
    }

    /// <summary>The entries whose box may come within <paramref name="radius"/> of <paramref name="point"/>, ascending and distinct.</summary>
    private static List<int> FindCandidates(SpaceEntries entries, Vector3 point, float radius)
    {
        var candidates = new List<int>();
        entries.Grid.CollectDistinct(new Box(point, point).Grown(radius), candidates);
        return candidates;
    }
}
