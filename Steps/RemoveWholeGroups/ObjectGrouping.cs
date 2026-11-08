using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RemoveWholeGroups;

/// <summary>
/// The join rule of the groups, by the kinds the area method counts (<see cref="ClearedAreaKinds"/>):
/// <list type="bullet">
/// <item>Groups chain only over drawn objects: two drawn objects of one space join when their boxes come within the gap horizontally,
/// at any height.</item>
/// <item>Every other object (effect-only, invisible, or of a base that could not be read) joins the group of the nearest drawn box
/// within the gap of its position, the lowest member on a tie; with none, it is a group of its own. Effect boxes can be far larger
/// than the solid pieces around them, so by their boxes they would bridge separate sites.</item>
/// </list>
/// The groups depend only on the objects, not on the order of the work or the number of workers. Read-only once built.
/// </summary>
internal sealed class ObjectGrouping
{
    private readonly IBaseObjectShapes _shapes;
    private readonly float _gap;

    /// <summary>The box each member joins by: a drawn member's box, every other member's position.</summary>
    private readonly ImmutableArray<Box> _boxes;

    /// <summary>The drawn members' positions by space.</summary>
    private readonly ItemsBySpace<int> _drawnBySpace;

    private readonly int[] _groupOf;

    private ObjectGrouping(IReadOnlyList<TargetObject> members, IBaseObjectShapes shapes, float gap, Execution execution)
    {
        _shapes = shapes;
        _gap = gap;
        var drawn = members.Select(member => ClearedAreaKinds.Of(shapes, member) == ClearedAreaObjectKind.Drawn).ToArray();
        _boxes = [.. members.Select((member, position) => drawn[position] ? BoxOf(member) : PointAt(member))];
        _drawnBySpace = ItemsBySpace<int>.Create(
            [.. Enumerable.Range(0, members.Count)], position => members[position].SpaceKey, position => _boxes[position], position => drawn[position],
            EqualityComparer<RecordKey>.Default, execution);
        var joined = ParallelMap.Run(
            execution, members.Count, position => JoinedBy(members[position].SpaceKey, _boxes[position], drawn[position]), ParallelMap.AutomaticRangeSize);
        Groups = Components.Of(members.Count, joined.SelectMany((others, position) => others.Select(other => (position, other))));
        _groupOf = Components.ComponentOfEachNode(members.Count, Groups);
    }

    /// <summary>The groups, each as its members' positions, ascending, in the order of their lowest member.</summary>
    public IReadOnlyList<int[]> Groups { get; }

    /// <param name="members">The objects to group; a position in this list names a member.</param>
    public static ObjectGrouping Of(IReadOnlyList<TargetObject> members, IBaseObjectShapes shapes, float gap, Execution execution) =>
        new(members, shapes, gap, execution);

    /// <summary>The group of the member at <paramref name="position"/>, as an index into <see cref="Groups"/>.</summary>
    public int GroupOf(int position) => _groupOf[position];

    /// <summary>The box the member at <paramref name="position"/> joins by.</summary>
    public Box BoxOfMember(int position) => _boxes[position];

    /// <returns>The groups, ascending, with a drawn box within the gap of the object's box, the object standing as given.</returns>
    public IEnumerable<int> GroupsWithADrawnBoxNear(TargetObject placed) =>
        DrawnNear(placed.SpaceKey, BoxOf(placed)).Select(GroupOf).Distinct().Order();

    /// <summary>A primitive (a trigger or collision box) by its own bounds, every other object by the box of what its base shows.</summary>
    private Box BoxOf(TargetObject target)
    {
        var local = target.IsPrimitive && target.PrimitiveBounds is { } size ? Box.FromCorners(-size / 2f, size / 2f) : _shapes.Of(target.Base).ShownBox;
        return OrientedBox.FromLocal(local, target.Transform).WorldAabb(0f);
    }

    private static Box PointAt(TargetObject target) => new(target.Transform.Position, target.Transform.Position);

    /// <returns>For a drawn member, every drawn member within the gap; for any other, the nearest drawn member within the gap, if any.</returns>
    private List<int> JoinedBy(RecordKey space, Box box, bool isDrawn)
    {
        var near = DrawnNear(space, box);
        return isDrawn || near.Count == 0 ? near : [near.MinBy(other => Boxes.HorizontalGap(box, _boxes[other]))];
    }

    /// <returns>The drawn member positions, ascending, whose boxes come within the gap of <paramref name="box"/> horizontally.</returns>
    private List<int> DrawnNear(RecordKey space, Box box)
    {
        var inSpace = _drawnBySpace.In(space);
        var slots = new List<int>();
        inSpace.Overlapping(ColumnAround(box), slot => Boxes.HorizontalGap(box, _boxes[inSpace.Items[slot]]) <= _gap, new SpatialQueryScratch(), slots);
        return [.. slots.Select(slot => inSpace.Items[slot])];
    }

    /// <summary>The box grown by the gap horizontally and reaching every height, so the query finds what lies above or below it.</summary>
    private Box ColumnAround(Box box) =>
        new(new Vector3(box.Min.X - _gap, box.Min.Y - _gap, float.MinValue), new Vector3(box.Max.X + _gap, box.Max.Y + _gap, float.MaxValue));
}
