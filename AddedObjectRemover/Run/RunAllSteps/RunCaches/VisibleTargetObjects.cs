using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Run.RunAllSteps.RunCaches;

/// <summary>
/// The visible target objects of each space as oriented boxes, indexed per space on first use.
/// Whether a target is visible is asked of the base object shapes. Thread-safe.
/// </summary>
internal sealed class VisibleTargetObjects : IVisibleTargetObjects
{
    /// <param name="Targets">Ascending, so ascending entries list the targets in id order.</param>
    private sealed record SpaceEntries(TargetId[] Targets, OrientedBox[] Boxes, GridCandidates Grid);

    private readonly IReadOnlyList<TargetObject> _targets;
    private readonly IBaseObjectShapes _shapes;
    private readonly ObjectContainment _containment;
    private readonly Dictionary<RecordKey, TargetId[]> _visibleBySpace;
    private readonly ComputedOncePerKey<RecordKey, SpaceEntries> _entries = new(Publication.BuiltOnce, EqualityComparer<RecordKey>.Default);

    public VisibleTargetObjects(
        IReadOnlyList<TargetObject> targets, IBaseObjectShapes shapes, ObjectContainment containment, Execution execution)
    {
        _targets = targets;
        _shapes = shapes;
        _containment = containment;
        var isVisible = ParallelMap.Run(
            execution, targets.Count, index => shapes.VisibilityOf(targets[index]).IsVisible,
            ParallelMap.AutomaticRangeSize);
        var visible = targets.Where((_, index) => isVisible[index]).ToList();
        var bySpace = visible.GroupBy(target => target.SpaceKey).ToList();
        Spaces = [.. bySpace.Select(space => space.Key)];
        _visibleBySpace = bySpace.ToDictionary(space => space.Key, space => space.Select(target => target.Id).ToArray());
    }

    public IReadOnlyList<RecordKey> Spaces { get; }

    public void Around(RecordKey space, Vector3 point, float radius, List<VisibleTargetNear> into)
    {
        into.Clear();
        if (EntriesOf(space) is not { } entries) return;

        var near = new List<int>();
        entries.Grid.CollectWithinRadius(point, radius, entry => !IsFarther(entries.Boxes[entry], point, radius), near);
        foreach (var entry in near)
        {
            var box = entries.Boxes[entry];
            into.Add(new VisibleTargetNear(entries.Targets[entry], FindSectors(box, point), box.GroundArea));
        }
    }

    /// <remarks>A NaN distance is not farther, so a broken box still counts as near.</remarks>
    private static bool IsFarther(OrientedBox box, Vector3 point, float radius) => Vector3.Distance(box.ClosestPoint(point), point) > radius;

    /// <summary>
    /// Every direction when the box contains the point. Otherwise exactly one: towards the box
    /// centre's horizontal offset when the box lies straight above or below the point (however
    /// small that offset), else towards the box's closest point.
    /// </summary>
    internal static IReadOnlyList<DirectionSector> FindSectors(OrientedBox box, Vector3 point)
    {
        return box.Contains(point) ? CompassDirections.All : [CompassDirections.Towards(point, box)];
    }

    public bool AnyContains(RecordKey space, Vector3 point, Func<TargetId, bool> include, ObjectQueryScratch scratch)
    {
        if (EntriesOf(space) is not { } entries) return false;
        return FindCandidates(entries, point, radius: 0f)
            .Select(entry => entries.Targets[entry])
            .Where(include)
            .Select(id => _targets[id.Index])
            .Any(target => _containment.Contains(target.Base, target.Transform, point, scratch));
    }

    public IEnumerable<OrientedBox> BoxesNear(RecordKey space, Vector3 point, float radius, Func<TargetId, bool> include) =>
        EntriesOf(space) is { } entries
            ? FindCandidates(entries, point, radius).Where(entry => include(entries.Targets[entry])).Select(entry => entries.Boxes[entry])
            : [];

    private SpaceEntries? EntriesOf(RecordKey space) =>
        _visibleBySpace.TryGetValue(space, out var visible) ? _entries.Get(space, () => CreateEntries(visible)) : null;

    private SpaceEntries CreateEntries(TargetId[] visible)
    {
        var boxes = visible
            .Select(id => OrientedBox.FromLocal(_shapes.Of(_targets[id.Index].Base).Box, _targets[id.Index].Transform))
            .ToArray();
        return new SpaceEntries(visible, boxes, GridCandidates.OfBoxes(boxes.Select(box => box.WorldAabb(0f)).ToArray()));
    }

    /// <summary>The entries whose box may come within <paramref name="radius"/> of <paramref name="point"/>, ascending and distinct.</summary>
    private static List<int> FindCandidates(SpaceEntries entries, Vector3 point, float radius)
    {
        var candidates = new List<int>();
        entries.Grid.CollectNear(point, radius, candidates);
        return candidates;
    }
}
