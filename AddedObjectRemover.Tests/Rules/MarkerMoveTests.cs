using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>Kept markers inside another mod's object move to the nearest free spot, preferably in their own cell.</summary>
public class MarkerMoveTests
{
    private static readonly ModKey OtherMod = ModKey.FromNameAndExtension("Other.esp");
    private static readonly CellArea HomeCell = CellArea.Single(0, 0);
    private static readonly Vector3 Marker = new(4000, 100, 0);

    [Fact]
    public void SpotInTheOwnCellWinsOverACloserSpotInANeighboringCell()
    {
        var moved = Move(
            HomeCell,
            new FakeSurface(RelocationSurface.Navmesh, new Vector3(4200, 100, 0)),
            new FakeSurface(RelocationSurface.Terrain, new Vector3(3000, 100, 0)));

        Assert.Equal(new Vector3(3000, 100, 0), moved.To);
        Assert.Equal(RelocationSurface.Terrain, moved.Surface);
        Assert.False(moved.LeftHomeCell);
    }

    [Fact]
    public void NeighboringCellIsUsedWhenTheOwnCellHasNoFreeSpot()
    {
        var moved = Move(
            HomeCell,
            new FakeSurface(RelocationSurface.Navmesh, new Vector3(4200, 100, 0)),
            new FakeSurface(RelocationSurface.Terrain));

        Assert.Equal(new Vector3(4200, 100, 0), moved.To);
        Assert.Equal(RelocationSurface.Navmesh, moved.Surface);
        Assert.True(moved.LeftHomeCell);
    }

    [Fact]
    public void InteriorMarkerMovesToTheNearestSpot()
    {
        var moved = Move(
            homeCell: null,
            new FakeSurface(RelocationSurface.Navmesh, new Vector3(4200, 100, 0), new Vector3(3000, 100, 0)));

        Assert.Equal(new Vector3(4200, 100, 0), moved.To);
        Assert.False(moved.LeftHomeCell);
    }

    [Fact]
    public void MarkerWithoutAFreeSpotStaysInPlace()
    {
        var result = CreateMover(HomeCell, new FakeSurface(RelocationSurface.Navmesh, new Vector3(9000, 100, 0)))
            .Move([KeptMarkerInsideOtherObject()], new Execution(Environment.ProcessorCount));

        Assert.Empty(result.Moved);
        Assert.Single(result.LeftInPlace);
    }

    private static KeptMarkerMove Move(CellArea? homeCell, params IFreeSpotSearch[] surfaces) =>
        Assert.Single(CreateMover(homeCell, surfaces).Move([KeptMarkerInsideOtherObject()], new Execution(Environment.ProcessorCount)).Moved);

    private static KeptMarkerMover CreateMover(CellArea? homeCell, params IFreeSpotSearch[] surfaces) =>
        new([TestTargets.Create(0, TestTargets.At(Marker))], _ => homeCell, surfaces, MarkerMoveLimits.MaxDistance);

    private static LeftBehindCheck KeptMarkerInsideOtherObject() => new(
        TargetIndex: 0,
        InvisibleObjectKind.XMarkers,
        Radius: 1024,
        ContainingObject: TestShapes.Placed(OtherMod, 0, new BaseKey(new FormKey(OtherMod, 0x800).ToRecordKey(), BaseLinkKind.PlaceableObject), Marker),
        new SectorAreaTally(50).Build(),
        LeftBehindOutcome.KeptProtectedType,
        KeepReason: null);

    /// <summary>Offers fixed spots: the nearest one within the distance and the allowed cells.</summary>
    private sealed class FakeSurface(RelocationSurface surface, params Vector3[] spots) : IFreeSpotSearch
    {
        public RelocationSurface Surface => surface;

        public bool TryFindNearestFreePoint(
            RecordKey spaceKey, Vector3 point, float maxDistance, CellArea? allowedCells, ObjectQueryScratch scratch, out Vector3 found)
        {
            var allowed = spots
                .Where(spot => Vector3.Distance(spot, point) <= maxDistance && (allowedCells is not { } cells || cells.Contains(spot)))
                .OrderBy(spot => Vector3.Distance(spot, point))
                .ToList();
            found = allowed.FirstOrDefault();
            return allowed.Count > 0;
        }
    }
}

/// <summary>The navmesh and terrain spot searches against real objects around the marker.</summary>
public class FreeSpotSearchTests
{
    private static readonly ModKey OtherMod = ModKey.FromNameAndExtension("ObjectsAroundMarker.esp");
    private const float TerrainHeight = 5 * 8f;

    private static readonly TestStatic Wall = new(
        new FormKey(OtherMod, 0x801), @"test\wall.nif", TestMeshes.BoxTriangles(new Box(new Vector3(380, -100, -50), new Vector3(620, 1100, 200))));

    private static readonly TestStatic Block = new(
        new FormKey(OtherMod, 0x802), @"test\block.nif", TestMeshes.BoxTriangles(new Box(new Vector3(3500, 1500, -100), new Vector3(4600, 2600, 300))));

    private static readonly IBaseObjectShapes Shapes = TestShapes.Create(OtherMod, "FreeSpotSearchData", Wall, Block);

    [Fact]
    public void NavmeshSpotKeepsTheClearanceFromObjectsAroundTheMarker()
    {
        var marker = new Vector3(500, 500, 0);
        var search = new NavmeshSpotSearch(
            TestGround.Navmeshes(new Dictionary<RecordKey, List<CellNavmesh>> { [TestTargets.Space] = [new CellNavmesh((0, 0), SquareNavmesh(1000))] }),
            CreateObjectsAroundMarker(Wall));

        Assert.True(search.TryFindNearestFreePoint(TestTargets.Space, marker, MarkerMoveLimits.MaxDistance, HomeCell, new ObjectQueryScratch(), out var found));

        // The navmesh offers points every 125 units; those at x = 375 and 625 lie outside the wall but within the clearance.
        Assert.True(found.X <= 380 - IFreeSpotSearch.Clearance || found.X >= 620 + IFreeSpotSearch.Clearance);
        Assert.Equal(250f, Vector3.Distance(marker, found), 1e-3f);
    }

    [Fact]
    public void TerrainSpotSkipsCellsWithoutLand()
    {
        var landscapes = new Dictionary<ExteriorCell, ILandscapeGetter> { [new ExteriorCell(TestTargets.Space, 0, 0)] = FlatLandscape() };
        var terrain = TestGround.Terrain(landscapes, new Dictionary<RecordKey, RecordKey> { [TestTargets.Space] = TestTargets.Space });
        var search = new TerrainSpotSearch(terrain, CreateObjectsAroundMarker(Block));

        // The marker stands in cell (1, 0), which has no LAND, inside the block; the nearest free
        // spot with terrain is just below the block and west of cell (1, 0), both less the clearance.
        Assert.True(search.TryFindNearestFreePoint(TestTargets.Space, new Vector3(4200, 2000, 0), MarkerMoveLimits.MaxDistance, allowedCells: null, new ObjectQueryScratch(), out var found));

        Assert.Equal(ExteriorGrid.CellSize - IFreeSpotSearch.Clearance, found.X, 0.5f);
        Assert.Equal(1500 - IFreeSpotSearch.Clearance, found.Y, 0.5f);
        Assert.Equal(TerrainHeight, found.Z, 1e-3f);
    }

    private static CellArea HomeCell => CellArea.Single(0, 0);

    private static OtherObjectsAroundMarker CreateObjectsAroundMarker(TestStatic model)
    {
        var scene = TestScenes.CreateWithSupportOnlyObjects([], [TestShapes.Placed(OtherMod, 0, model.Base, Vector3.Zero)], Shapes);
        return new OtherObjectsAroundMarker(scene.VisibleObjectsOfAnyPlugin(),scene.VisibleTargets, new HashSet<int>(), Shapes);
    }

    /// <summary>Two triangles covering [0, size] x [0, size] at height 0.</summary>
    private static NavigationMeshData SquareNavmesh(float size) => new()
    {
        Vertices = new ExtendedList<P3Float> { new(0, 0, 0), new(size, 0, 0), new(size, size, 0), new(0, size, 0) },
        Triangles = new ExtendedList<NavmeshTriangle>
        {
            new() { Vertices = new P3Int16(0, 1, 3) },
            new() { Vertices = new P3Int16(2, 3, 1) },
        },
    };

    /// <summary>Every vertex at the offset: 5 height steps of 8 units.</summary>
    private static Landscape FlatLandscape() =>
        new(new FormKey(TestTargets.TargetMod, 0x300), SkyrimRelease.SkyrimSE) { VertexHeightMap = new LandscapeVertexHeightMap { Offset = 5 } };
}
