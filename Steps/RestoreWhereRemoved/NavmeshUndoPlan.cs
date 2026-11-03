using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsInClearedAreas.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>An object that stays in the final world, as a box.</summary>
internal sealed record StandingBox(RecordKey Key, RecordKey SpaceKey, OrientedBox Box)
{
    /// <summary>The box's world bounds, computed once.</summary>
    public AddedObjectRemover.Box Bounds { get; } = Box.WorldAabb(0f);
}

/// <summary>
/// Which navmeshes the cleaned mod edited, deleted or added go back, in groups that go back whole or not at all: an edited one gets
/// its earlier geometry or map entry, a deleted one comes back, an added one is sunk. Candidates linked to each other in either
/// world, an added navmesh and the deleted one it replaces, and navmeshes named by one door form a group. Each group is checked
/// once against the guards on the facts read and against the consistency check of its own writes; the guards on the ground,
/// objects and water refuse more against the current plans of the other kinds. Immutable: each refusal gives a new plan.
/// </summary>
/// <remarks>Everything runs in ascending key order on one thread, so the plan does not depend on the thread count.</remarks>
internal sealed class NavmeshUndoPlan
{
    private readonly ImmutableArray<NavmeshChangeJudged> _judged;
    private readonly NavmeshGroupGuards _guards;
    private readonly ImmutableArray<NavmeshGroup> _groups;

    /// <param name="judged">Every navmesh the cleaned mod changed or added, with its area judged, in key order.</param>
    /// <param name="createGuards">The guards on the facts read, given the candidates that are sunk.</param>
    /// <param name="findFaultIfWritten">The consistency check's first fault over the world the trial's writes alone would leave; null when none.</param>
    public NavmeshUndoPlan(
        IReadOnlyList<NavmeshChangeJudged> judged,
        Func<IReadOnlySet<RecordKey>, NavmeshGroupGuards> createGuards,
        Func<NavmeshWriteTrial, NavmeshFault?> findFaultIfWritten)
    {
        _judged = [.. judged.Select(WithUndo)];
        _guards = createGuards(_judged.Where(navmesh => navmesh.Undo == NavmeshUndo.Sunk).Select(navmesh => navmesh.Change.Navmesh).ToHashSet());
        _groups = [.. FormGroups(_judged).Select(group => _guards.Check(group, findFaultIfWritten))];
    }

    private NavmeshUndoPlan(ImmutableArray<NavmeshChangeJudged> judged, NavmeshGroupGuards guards, ImmutableArray<NavmeshGroup> groups)
    {
        _judged = judged;
        _guards = guards;
        _groups = groups;
    }

    /// <summary>
    /// The navmeshes whose triangles the cleaned mod changed or added that are not in a group going back, in key order. A navmesh
    /// whose only change is its map entry keeps the triangles it had before the cleaned mod, so it is none of them.
    /// </summary>
    public ImmutableArray<RecordKey> NavmeshesThatStayChanged()
    {
        var goingBack = GroupsGoingBack().SelectMany(group => group.Members).Select(member => member.Key).ToHashSet();
        return [.. _judged.Where(navmesh => navmesh.Change.ChangesTriangles).Select(navmesh => navmesh.Change.Navmesh).Where(navmesh => !goingBack.Contains(navmesh))];
    }

    /// <summary>How many groups are refused.</summary>
    public int RefusedCount => _groups.Count(group => group.Refused is not null);

    /// <summary>
    /// The navmeshes as they will stand: the winning ones but the members of a group going back that get their earlier triangles or
    /// are sunk, which leave the walkable world, and those getting their earlier triangles at those.
    /// </summary>
    /// <param name="winning">The winning navmeshes.</param>
    /// <param name="trianglesGoingBack">This plan's <see cref="IndexOfTrianglesGoingBack"/>.</param>
    public NavmeshesAsTheyWillStand AsTheyWillStand(INavmeshes winning, NavmeshIndex trianglesGoingBack)
    {
        var moved = GroupsGoingBack().SelectMany(group => group.Members).Where(member => member.Undo != NavmeshUndo.MapEntry).Select(member => member.Key).ToHashSet();
        return new NavmeshesAsTheyWillStand(winning, navmesh => !moved.Contains(navmesh), trianglesGoingBack);
    }

    /// <summary>
    /// The plan with every group refused under whose members going back, footprint grown by one grid step, a grid point ends more
    /// than the height tolerance from its height before the cleaned mod.
    /// </summary>
    /// <param name="findLandWorldspace">A space → the worldspace whose land it uses; null for a space without land.</param>
    /// <param name="heightsOf">A cell's heights before the cleaned mod and as they end.</param>
    public NavmeshUndoPlan RefuseOnGround(Func<RecordKey, RecordKey?> findLandWorldspace, Func<ExteriorCell, LandHeights> heightsOf, float heightTolerance) =>
        RefuseEach(group => group.Members
            .Where(member => member.StandsAsBefore)
            .Select(member => (Member: member, Land: findLandWorldspace(member.Change.SpaceKey)))
            .Where(onLand => onLand.Land is { } land && GroundUnder(onLand.Member, land)
                .Any(point => heightsOf(point.Cell) is var heights && HeightGridComparison.DiffersAt(heights.Earlier, heights.Current, point.Point, heightTolerance)))
            .Select(onLand => new NavmeshRefusal(NavmeshGuard.Ground, onLand.Member.Key, Fault: null))
            .FirstOrDefault());

    /// <summary>
    /// The plan with every group refused that a member going back reaches into one of the objects' boxes; the first object in key
    /// order is named. An object is asked once whether it reaches any group still open, and only one that does is asked per group.
    /// </summary>
    /// <param name="objects">In key order.</param>
    /// <param name="trianglesGoingBack">The <see cref="IndexOfTrianglesGoingBack"/> of this plan or of one it came from by refusing more.</param>
    public NavmeshUndoPlan RefuseOnObjects(IReadOnlyList<StandingBox> objects, NavmeshIndex trianglesGoingBack)
    {
        var groups = GroupsGoingBack().Select(FirstKeyOf).ToList();
        var groupOf = GroupsGoingBack().SelectMany(group => group.Members.Select(member => (member.Key, Group: FirstKeyOf(group)))).ToDictionary(entry => entry.Key, entry => entry.Group);
        var refusals = new Dictionary<RecordKey, NavmeshRefusal>();
        foreach (var standing in objects)
        {
            if (!trianglesGoingBack.AnyTriangleIn(standing.SpaceKey, standing.Box, navmesh => groupOf.TryGetValue(navmesh, out var group) && !refusals.ContainsKey(group))) continue;
            foreach (var group in groups.Where(group => !refusals.ContainsKey(group) && trianglesGoingBack.AnyTriangleIn(standing.SpaceKey, standing.Box, navmesh => groupOf.GetValueOrDefault(navmesh) == group)))
            {
                refusals.Add(group, new NavmeshRefusal(NavmeshGuard.Object, standing.Key, Fault: null));
            }
        }
        return RefuseEach(group => refusals.GetValueOrDefault(FirstKeyOf(group)));
    }

    /// <summary>
    /// The door guard against the placed records that go back: the plan with every group refused that a member going back names a door the final
    /// world leaves removed, or whose doors and members would not name each other with the doors' links as they end.
    /// </summary>
    /// <param name="doors">Whether each door stands and where its link points, given which placed records go back.</param>
    public NavmeshUndoPlan RefuseOnDoors(DoorsAsTheyEnd doors) => RefuseEach(group => _guards.Doors(group, doors));

    /// <summary>The members of the groups going back.</summary>
    public IReadOnlySet<RecordKey> MembersGoingBack() => GroupsGoingBack().SelectMany(group => group.Members).Select(member => member.Key).ToHashSet();

    /// <summary>The plan with every group refused that has a member going back in a cell whose water level does not end as before the cleaned mod.</summary>
    /// <param name="waterEndsAsBefore">Whether a cell's water level ends as it was before the cleaned mod.</param>
    public NavmeshUndoPlan RefuseOnWater(Func<RecordKey, bool> waterEndsAsBefore) =>
        RefuseEach(group => group.Members
            .Where(member => member.StandsAsBefore && !waterEndsAsBefore(member.Change.Cell))
            .Select(member => new NavmeshRefusal(NavmeshGuard.Water, member.Change.Cell, Fault: null))
            .FirstOrDefault());

    public ImmutableArray<NavmeshChangeJudged> Navmeshes() => _judged;

    public ImmutableArray<NavmeshGroupJudged> Groups() =>
        [.. _groups.Select(group => new NavmeshGroupJudged([.. group.Members.Select(member => member.Key)], group.Write, group.Refused))];

    private static RecordKey FirstKeyOf(NavmeshGroup group) => group.Members[0].Key;

    private IEnumerable<NavmeshGroup> GroupsGoingBack() => _groups.Where(group => group.Refused is null);

    /// <summary>The earlier triangles of the members of the groups going back that get them.</summary>
    public NavmeshIndex IndexOfTrianglesGoingBack()
    {
        var triangles = GroupsGoingBack().SelectMany(group => group.Members).Where(member => member.StandsAsBefore).SelectMany(TrianglesOf)
            .ToLookup(triangle => triangle.Space, triangle => triangle.Triangle);
        return new NavmeshIndex(bucket => bucket.Grid is null && triangles.Contains(bucket.SpaceKey) ? triangles[bucket.SpaceKey].ToArray() : null);
    }

    private NavmeshUndoPlan RefuseEach(Func<NavmeshGroup, NavmeshRefusal?> refusalOf) =>
        new(_judged, _guards, [.. _groups.Select(group => group.Refused is null && refusalOf(group) is { } refusal ? group.RefusedFor(refusal) : group)]);

    /// <summary>A navmesh whose area is cleared gets what the patch writes for its case; nothing when nothing in it goes back.</summary>
    private static NavmeshChangeJudged WithUndo(NavmeshChangeJudged navmesh)
    {
        if (navmesh.Area?.Verdict != AreaVerdict.Cleared) return navmesh;
        var undo = UndoOf(navmesh.Change);
        return navmesh with { Undo = undo, NothingGoesBack = undo is null };
    }

    /// <remarks>A part a later plugin changed again still makes a candidate, so its group is refused for the later change rather than split.</remarks>
    private static NavmeshUndo? UndoOf(NavmeshChangedByCleanedMod change) => change.Case switch
    {
        NavmeshCase.Added => NavmeshUndo.Sunk,
        NavmeshCase.Deleted => change.Presence is PartOutcome.Unchanged ? null : NavmeshUndo.BroughtBack,
        _ when change.Geometry is not PartOutcome.Unchanged => NavmeshUndo.Geometry,
        _ when change.MapEntry is not PartOutcome.Unchanged => NavmeshUndo.MapEntry,
        _ => null,
    };

    /// <summary>
    /// The candidates joined by an edge link in the world before the cleaned mod, in the cleaned mod's or in a neighbour's winning
    /// version, by a replacement or by a door they both name; each group in key order, the groups by their first member.
    /// </summary>
    /// <param name="judged">In key order.</param>
    private static IEnumerable<NavmeshGroup> FormGroups(ImmutableArray<NavmeshChangeJudged> judged)
    {
        var members = judged.Where(navmesh => navmesh.Undo is not null).Select(navmesh => new NavmeshMember(navmesh.Change, navmesh.Undo!.Value)).ToList();
        var indexOf = KeyedGroups.PositionByKey(members, member => member.Key);
        var linked = members.SelectMany((member, index) => RelatedNavmeshes(member.Change).Where(indexOf.ContainsKey).Select(other => (index, indexOf[other])));
        var byDoor = members
            .SelectMany((member, index) => member.Change.Doors.Select(door => (door.Door, index)))
            .GroupBy(entry => entry.Door)
            .SelectMany(door => door.Skip(1).Select(entry => (door.First().index, entry.index)));
        return Components.Of(members.Count, linked.Concat(byDoor)).Select(component =>
        {
            ImmutableArray<NavmeshMember> group = [.. component.Select(index => members[index])];
            ImmutableArray<DoorLinkChange> doors = [.. group.SelectMany(member => member.Change.Doors).DistinctBy(door => door.Door).OrderBy(door => door.Door, RecordKeyOrder.Comparer)];
            return new NavmeshGroup(group, doors, Write: null, Refused: null);
        });
    }

    private static IEnumerable<RecordKey> RelatedNavmeshes(NavmeshChangedByCleanedMod change) =>
        (change.Earlier?.EdgeLinks ?? []).Concat(change.Current.EdgeLinks).Select(link => link.Neighbour)
            .Concat(change.Neighbours.Select(neighbour => neighbour.Navmesh))
            .Concat(change.ReplacedBy);

    /// <summary>The grid points under the member's earlier triangles, each triangle's footprint grown by one grid step on every side.</summary>
    private static IEnumerable<LandGridPoint> GroundUnder(NavmeshMember member, RecordKey landWorldspace) =>
        TrianglesOf(member)
            .Select(triangle => triangle.Triangle.Triangle.Bounds.Grown(TerrainGrid.VertexSpacing))
            .SelectMany(area => TerrainGrid.VerticesUnder(area))
            .SelectMany(under => under.VertexIndices.Select(point => new LandGridPoint(new ExteriorCell(landWorldspace, under.CellX, under.CellY), point)))
            .Distinct();

    private static IEnumerable<(RecordKey Space, NavmeshWorldTriangle Triangle)> TrianglesOf(NavmeshMember member) =>
        member.Planned!.WorldTriangles().Select(triangle => (member.Change.SpaceKey, new NavmeshWorldTriangle(member.Key, triangle)));
}
