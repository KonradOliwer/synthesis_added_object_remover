using System.Collections.Immutable;
using System.Numerics;

namespace AddedObjectRemover.Tests.PluginRecordFacts;

/// <summary>
/// Two square navmeshes side by side in one exterior cell, West and East, each two triangles. West's first triangle links over the
/// shared edge into East's second triangle and back; a door leads from West's second triangle. Both have map entries naming their
/// cell, and the preferred paths hold both. Each test seeds one fault and checks the whole fault: navmesh, rule, kind, index and
/// other record.
/// </summary>
public sealed class NavmeshConsistencyTests
{
    private const float Size = 100f;
    private const int LinkedTriangle = 0;
    private const int EastLinkedTriangle = 1;
    private const int DoorTriangle = 1;
    private const int TriangleOutOfRange = 5;

    private static readonly PluginName Base = new("Base.esm");
    private static readonly RecordKey Worldspace = new(Base, 0x800);
    private static readonly RecordKey West = new(Base, 0x801);
    private static readonly RecordKey East = new(Base, 0x802);
    private static readonly RecordKey Door = new(Base, 0x803);
    private static readonly RecordKey Elsewhere = new(Base, 0x804);
    private static readonly NavmeshPlace Cell = new(Worldspace, new CellGridPoint(0, 0));

    private readonly TestWorld _world = new();
    private readonly HashSet<RecordKey> _copiedFromEarlier = [];
    private readonly HashSet<RecordKey> _doorsThatDoNotStand = [];

    public NavmeshConsistencyTests()
    {
        _world.Navmeshes[West] = Square(0f, westEdgeLinked: false, eastEdgeLinked: true) with
        {
            EdgeLinks = [new NavmeshLinkTarget(East, EastLinkedTriangle)],
            DoorTriangles = [new NavmeshDoorTriangleFact(DoorTriangle, Door)],
        };
        _world.Navmeshes[East] = Square(Size, westEdgeLinked: true, eastEdgeLinked: false) with { EdgeLinks = [new NavmeshLinkTarget(West, LinkedTriangle)] };
        _world.Entries[West] = new NavmeshEntryView(Cell, [East], [], [Door]);
        _world.Entries[East] = new NavmeshEntryView(Cell, [], [West], []);
        _world.Doors[Door] = new DoorView(IsDeleted: false, new NavDoorLinkFact(West, DoorTriangle));
        _world.PathSets.Add([West, East]);
    }

    [Fact]
    public void TwoLinkedNavmeshesWithADoorPass()
    {
        Assert.Null(FirstFault(West));
        Assert.Null(FirstFault(East));
    }

    [Fact]
    public void ANavmeshTheWorldDoesNotHoldBreaksIdentity() =>
        AssertFault(new NavmeshFault(Elsewhere, NavmeshRule.Identity, NavmeshFaultKind.Missing, null, null), named: [Elsewhere]);

    [Fact]
    public void ADeletedNavmeshBreaksIdentity()
    {
        _world.Navmeshes[West] = _world.Navmeshes[West] with { IsDeleted = true };

        AssertFault(new NavmeshFault(West, NavmeshRule.Identity, NavmeshFaultKind.Deleted, null, null));
    }

    [Fact]
    public void ANavmeshInADeletedCellBreaksIdentity()
    {
        _world.Navmeshes[West] = _world.Navmeshes[West] with { HeldInDeletedCell = true };

        AssertFault(new NavmeshFault(West, NavmeshRule.Identity, NavmeshFaultKind.CellDeleted, null, null));
    }

    [Fact]
    public void ANavmeshWithoutNavmeshDataBreaksIdentity()
    {
        _world.Navmeshes[West] = _world.Navmeshes[West] with { Parent = null };

        AssertFault(new NavmeshFault(West, NavmeshRule.Identity, NavmeshFaultKind.NoData, null, null));
    }

    [Fact]
    public void AParentOtherThanTheCellHoldingItBreaksIdentity()
    {
        _world.Navmeshes[West] = _world.Navmeshes[West] with { Parent = new NavmeshPlace(Worldspace, new CellGridPoint(0, 1)) };

        AssertFault(new NavmeshFault(West, NavmeshRule.Identity, NavmeshFaultKind.ParentIsAnotherCell, null, null));
    }

    [Fact]
    public void AMapEntryNamingAnotherParentBreaksIdentity()
    {
        _world.Entries[West] = _world.Entries[West] with { Parent = new NavmeshPlace(Elsewhere, null) };

        AssertFault(new NavmeshFault(West, NavmeshRule.Identity, NavmeshFaultKind.MapEntryNamesAnotherParent, null, null));
    }

    [Fact]
    public void ANavmeshWithoutMapEntryBreaksIdentity()
    {
        _world.Entries.Remove(West);

        AssertFault(new NavmeshFault(West, NavmeshRule.Identity, NavmeshFaultKind.NoMapEntry, null, null));
    }

    [Fact]
    public void AVertexIndexOutOfRangeBreaksTheTriangles()
    {
        SetTriangle(West, 1, triangle => triangle with { Corners = [0, 2, 4] });

        AssertFault(new NavmeshFault(West, NavmeshRule.Triangles, NavmeshFaultKind.VertexOutOfRange, 1, null));
    }

    [Fact]
    public void AnInnerEdgeOutOfRangeBreaksTheTriangles()
    {
        SetTriangle(West, 1, triangle => triangle with { Edges = triangle.Edges.SetItem(1, new NavmeshEdgeView(IsEdgeLink: false, 2)) });

        AssertFault(new NavmeshFault(West, NavmeshRule.Triangles, NavmeshFaultKind.InnerEdgeOutOfRange, 1, null));
    }

    [Fact]
    public void AnEdgeLinkFlagPastTheEdgeLinkTableBreaksTheTriangles()
    {
        SetTriangle(West, LinkedTriangle, triangle => triangle with { Edges = triangle.Edges.SetItem(1, new NavmeshEdgeView(IsEdgeLink: true, 1)) });

        AssertFault(new NavmeshFault(West, NavmeshRule.Triangles, NavmeshFaultKind.EdgeLinkEntryOutOfRange, LinkedTriangle, null));
    }

    [Fact]
    public void ALinkWithoutALinkBackBreaksTheLinks()
    {
        SetTriangle(East, EastLinkedTriangle, triangle => triangle with { Edges = triangle.Edges.SetItem(2, new NavmeshEdgeView(IsEdgeLink: false, null)) });

        AssertFault(new NavmeshFault(West, NavmeshRule.Links, NavmeshFaultKind.NoLinkBack, LinkedTriangle, East));
    }

    [Fact]
    public void ALinkIntoATriangleOutOfRangeBreaksTheLinks()
    {
        _world.Navmeshes[West] = _world.Navmeshes[West] with { EdgeLinks = [new NavmeshLinkTarget(East, TriangleOutOfRange)] };

        AssertFault(new NavmeshFault(West, NavmeshRule.Links, NavmeshFaultKind.LinkedTriangleOutOfRange, LinkedTriangle, East));
    }

    [Fact]
    public void ASharedEdgeWhoseVerticesLieApartBreaksTheLinks()
    {
        var east = _world.Navmeshes[East];
        _world.Navmeshes[East] = east with { Vertices = east.Vertices.SetItem(3, new Vector3(Size, Size, 1f)) };

        AssertFault(new NavmeshFault(West, NavmeshRule.Links, NavmeshFaultKind.SharedEdgeApart, LinkedTriangle, East));
    }

    [Fact]
    public void ALinkIntoADeletedNavmeshIsTheFaultOfTheLinkingNavmesh()
    {
        _world.Navmeshes[East] = _world.Navmeshes[East] with { IsDeleted = true };

        AssertFault(new NavmeshFault(West, NavmeshRule.Links, NavmeshFaultKind.LinkedNavmeshMissing, LinkedTriangle, East));
    }

    [Fact]
    public void ADoorTriangleOutOfRangeBreaksTheDoors()
    {
        _world.Navmeshes[West] = _world.Navmeshes[West] with { DoorTriangles = [new NavmeshDoorTriangleFact(TriangleOutOfRange, Door)] };

        AssertFault(new NavmeshFault(West, NavmeshRule.Doors, NavmeshFaultKind.DoorTriangleOutOfRange, TriangleOutOfRange, Door));
    }

    [Fact]
    public void ADoorTriangleNamingADoorTheWorldDoesNotHoldBreaksTheDoors()
    {
        _world.Navmeshes[West] = _world.Navmeshes[West] with { DoorTriangles = [new NavmeshDoorTriangleFact(DoorTriangle, Elsewhere)] };
        _world.Doors.Remove(Door);

        AssertFault(new NavmeshFault(West, NavmeshRule.Doors, NavmeshFaultKind.DoorMissing, DoorTriangle, Elsewhere));
    }

    [Fact]
    public void ADoorTriangleWhoseDoorHasNoNavmeshLinkBreaksTheDoors()
    {
        _world.Doors[Door] = new DoorView(IsDeleted: false, Link: null);

        AssertFault(new NavmeshFault(West, NavmeshRule.Doors, NavmeshFaultKind.DoorLinksElsewhere, DoorTriangle, Door));
    }

    [Fact]
    public void ADoorLinkWithoutADoorTriangleBreaksTheDoors()
    {
        _world.Navmeshes[West] = _world.Navmeshes[West] with { DoorTriangles = [] };

        AssertFault(new NavmeshFault(West, NavmeshRule.Doors, NavmeshFaultKind.DoorLinkWithoutDoorTriangle, DoorTriangle, Door));
    }

    [Fact]
    public void ADoorTriangleBroughtBackNamingADoorThatDoesNotStandBreaksTheDoors()
    {
        _copiedFromEarlier.Add(West);
        _doorsThatDoNotStand.Add(Door);

        AssertFault(new NavmeshFault(West, NavmeshRule.Doors, NavmeshFaultKind.DoorDoesNotStand, DoorTriangle, Door));
    }

    /// <summary>The world already held the door triangle, as it holds those of doors a quest enables later.</summary>
    [Fact]
    public void ADoorTriangleNotBroughtBackNamingADoorThatDoesNotStandPasses()
    {
        _doorsThatDoNotStand.Add(Door);

        Assert.Null(FirstFault(West));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ADoorThatDoesNotStandOrIsDeletedWhoseLinkNamesANavmeshWithoutItsDoorTrianglePasses(bool doesNotStand)
    {
        _world.Navmeshes[East] = _world.Navmeshes[East] with { DoorTriangles = [] };
        _world.Doors[Elsewhere] = new DoorView(IsDeleted: !doesNotStand, new NavDoorLinkFact(East, DoorTriangle));
        if (doesNotStand) _doorsThatDoNotStand.Add(Elsewhere);

        Assert.Null(FirstFault(East));
    }

    [Fact]
    public void ADoorThatDoesNotStandWhoseLinkNamesAnotherTriangleThanItsDoorTrianglePasses()
    {
        _world.Doors[Door] = new DoorView(IsDeleted: false, new NavDoorLinkFact(West, LinkedTriangle));
        _doorsThatDoNotStand.Add(Door);

        Assert.Null(FirstFault(West));
    }

    [Fact]
    public void AVertexOutsideTheBoundsBreaksTheSpatialData()
    {
        _world.Navmeshes[West] = _world.Navmeshes[West] with { Max = new Vector3(Size, Size / 2, 0f) };

        AssertFault(new NavmeshFault(West, NavmeshRule.Spatial, NavmeshFaultKind.VertexOutsideBounds, 2, null));
    }

    [Fact]
    public void ATriangleCountUnlikeTheGridsSourceBreaksTheSpatialData()
    {
        _world.Navmeshes[West] = _world.Navmeshes[West] with { GridSourceTriangleCounts = [3] };

        AssertFault(new NavmeshFault(West, NavmeshRule.Spatial, NavmeshFaultKind.GridFromAnotherTriangleCount, null, null));
    }

    [Fact]
    public void PreferredPathsNamingADeletedNavmeshBreakThePathData()
    {
        _world.Navmeshes[Elsewhere] = Square(-Size, westEdgeLinked: false, eastEdgeLinked: false) with { IsDeleted = true };
        _world.PathSets[0] = [West, East, Elsewhere];

        AssertFault(new NavmeshFault(West, NavmeshRule.PathData, NavmeshFaultKind.PreferredPathNavmeshMissing, null, Elsewhere));
    }

    [Fact]
    public void AMapEntryMergingIntoAMissingNavmeshBreaksThePathData()
    {
        _world.Entries[West] = _world.Entries[West] with { MergedTo = [Elsewhere] };

        AssertFault(new NavmeshFault(West, NavmeshRule.PathData, NavmeshFaultKind.MergedNavmeshMissing, null, Elsewhere));
    }

    [Fact]
    public void AMapEntryNamingADeletedDoorBreaksThePathData()
    {
        _world.Entries[East] = _world.Entries[East] with { LinkedDoors = [Elsewhere] };
        _world.Doors[Elsewhere] = new DoorView(IsDeleted: true, Link: null);

        AssertFault(new NavmeshFault(East, NavmeshRule.PathData, NavmeshFaultKind.LinkedDoorMissing, null, Elsewhere));
    }

    [Fact]
    public void ANavmeshLinkedToTheOneNamedIsCheckedToo()
    {
        _world.Navmeshes[East] = _world.Navmeshes[East] with { GridSourceTriangleCounts = [] };

        AssertFault(new NavmeshFault(East, NavmeshRule.Spatial, NavmeshFaultKind.GridFromAnotherTriangleCount, null, null), named: [West]);
    }

    [Fact]
    public void TheFirstFaultInKeyOrderIsFoundWhateverTheOrderNamed()
    {
        _world.Navmeshes[West] = _world.Navmeshes[West] with { GridSourceTriangleCounts = [] };
        _world.Navmeshes[East] = _world.Navmeshes[East] with { IsDeleted = true };
        var expected = new NavmeshFault(West, NavmeshRule.Links, NavmeshFaultKind.LinkedNavmeshMissing, LinkedTriangle, East);

        AssertFault(expected, named: [West, East]);
        AssertFault(expected, named: [East, West]);
    }

    private NavmeshFault? FirstFault(params RecordKey[] named) => NavmeshConsistency.FirstFault(_world, named, _copiedFromEarlier, _doorsThatDoNotStand);

    private void AssertFault(NavmeshFault expected, params RecordKey[] named) => Assert.Equal(expected, FirstFault(named.Length == 0 ? [West] : named));

    private void SetTriangle(RecordKey navmesh, int index, Func<NavmeshTriangleView, NavmeshTriangleView> change)
    {
        var view = _world.Navmeshes[navmesh];
        _world.Navmeshes[navmesh] = view with { Triangles = view.Triangles.SetItem(index, change(view.Triangles[index])) };
    }

    /// <summary>
    /// A square from <paramref name="x"/> to <paramref name="x"/> + <see cref="Size"/>: triangle 0 runs 0-1-2 (its edge 1 is the east
    /// side), triangle 1 runs 0-2-3 (its edge 2 is the west side); the diagonal joins them.
    /// </summary>
    private static NavmeshView Square(float x, bool westEdgeLinked, bool eastEdgeLinked) => new(
        IsDeleted: false,
        Cell,
        HeldInDeletedCell: false,
        Cell,
        [new Vector3(x, 0, 0), new Vector3(x + Size, 0, 0), new Vector3(x + Size, Size, 0), new Vector3(x, Size, 0)],
        [
            new NavmeshTriangleView([0, 1, 2], [Inner(null), eastEdgeLinked ? LinkEntry : Inner(null), Inner(1)]),
            new NavmeshTriangleView([0, 2, 3], [Inner(0), Inner(null), westEdgeLinked ? LinkEntry : Inner(null)]),
        ],
        [],
        [],
        new Vector3(x, 0, 0),
        new Vector3(x + Size, Size, 0),
        [2]);

    private static NavmeshEdgeView LinkEntry => new(IsEdgeLink: true, 0);

    private static NavmeshEdgeView Inner(int? neighbour) => new(IsEdgeLink: false, neighbour);

    private sealed class TestWorld : INavmeshWorldView
    {
        public Dictionary<RecordKey, NavmeshView> Navmeshes { get; } = [];

        public Dictionary<RecordKey, NavmeshEntryView> Entries { get; } = [];

        public Dictionary<RecordKey, DoorView> Doors { get; } = [];

        public List<ImmutableArray<RecordKey>> PathSets { get; } = [];

        public IEnumerable<ImmutableArray<RecordKey>> PreferredPathSets => PathSets;

        public NavmeshView? NavmeshOf(RecordKey navmesh) => Navmeshes.GetValueOrDefault(navmesh);

        public bool Exists(RecordKey navmesh) => NavmeshOf(navmesh) is { IsDeleted: false };

        public NavmeshEntryView? EntryOf(RecordKey navmesh) => Entries.GetValueOrDefault(navmesh);

        public DoorView? DoorOf(RecordKey door) => Doors.GetValueOrDefault(door);

        public IEnumerable<RecordKey> NavmeshesLinkingTo(RecordKey navmesh) =>
            Navmeshes.Where(entry => entry.Value.EdgeLinks.Any(link => link.Navmesh == navmesh)).Select(entry => entry.Key);

        public IEnumerable<RecordKey> DoorsLinkingTo(RecordKey navmesh) =>
            Doors.Where(entry => entry.Value.Link?.Navmesh == navmesh).Select(entry => entry.Key);
    }
}
