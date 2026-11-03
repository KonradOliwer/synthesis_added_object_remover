using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Run.RunAllSteps.RunCaches;

/// <summary>
/// The visible target objects of each space as oriented boxes of what the game draws of them, indexed per space on first use.
/// Which targets count is asked of their visibility from the base object shapes. Thread-safe.
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

    /// <param name="counts">Whether a target of this visibility is indexed.</param>
    public VisibleTargetObjects(
        IReadOnlyList<TargetObject> targets, IBaseObjectShapes shapes, ObjectContainment containment, Execution execution, Func<ObjectVisibility, bool> counts)
    {
        _targets = targets;
        _shapes = shapes;
        _containment = containment;
        var isVisible = ParallelMap.Run(
            execution, targets.Count, index => counts(shapes.VisibilityOf(targets[index])),
            ParallelMap.AutomaticRangeSize);
        var visible = targets.Where((_, index) => isVisible[index]).ToList();
        var bySpace = visible.GroupBy(target => target.SpaceKey).ToList();
        Spaces = [.. bySpace.Select(space => space.Key)];
        _visibleBySpace = bySpace.ToDictionary(space => space.Key, space => space.Select(target => target.Id).ToArray());
    }

    public IReadOnlyList<RecordKey> Spaces { get; }

    public void Around(RecordKey space, Vector3 point, float radius, List<VisibleTargetNear> into) =>
        CollectWithinRadius(
            space, point, radius, (point, radius), static (box, state) => !Vectors.IsFartherThan(box.ClosestPoint(state.point), state.point, state.radius), into);

    /// <remarks>The grid buckets X and Y only, so a query at height 0 finds the whole column.</remarks>
    public void InColumn(RecordKey space, Vector2 point, float radius, List<VisibleTargetNear> into) =>
        CollectWithinRadius(
            space, new Vector3(point, 0f), radius, (point, radius), static (box, state) => Vectors.IsWithinHorizontally(box.Center, state.point, state.radius), into);

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

    /// <summary>
    /// Replaces <paramref name="into"/> with the targets the grid finds within the radius whose box passes <paramref name="isNear"/>,
    /// in ascending order. The test takes the caller's state, so it can be a static lambda.
    /// </summary>
    private void CollectWithinRadius<TState>(
        RecordKey space, Vector3 point, float radius, TState state, Func<OrientedBox, TState, bool> isNear, List<VisibleTargetNear> into)
    {
        into.Clear();
        if (EntriesOf(space) is not { } entries) return;

        foreach (var entry in FindCandidates(entries, point, radius))
        {
            if (isNear(entries.Boxes[entry], state)) into.Add(new VisibleTargetNear(entries.Targets[entry], entries.Boxes[entry]));
        }
    }

    private SpaceEntries? EntriesOf(RecordKey space) =>
        _visibleBySpace.TryGetValue(space, out var visible) ? _entries.Get(space, () => CreateEntries(visible)) : null;

    private SpaceEntries CreateEntries(TargetId[] visible)
    {
        var boxes = visible
            .Select(id => OrientedBox.FromLocal(_shapes.Of(_targets[id.Index].Base).ShownBox, _targets[id.Index].Transform))
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
