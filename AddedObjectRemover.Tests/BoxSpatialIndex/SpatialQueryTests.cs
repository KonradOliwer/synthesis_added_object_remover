using System.Numerics;

namespace AddedObjectRemover.Tests.BoxSpatialIndex;

public class SpatialQueryTests
{
    private static readonly RecordKey Space = new(new PluginName("A.esp"), 1);
    private static readonly RecordKey OtherSpace = new(new PluginName("A.esp"), 2);

    private static Box Around(float x, float half = 10f) => new(new Vector3(x - half, -half, -half), new Vector3(x + half, half, half));

    private static OrientedBox Upright(float x, float half = 10f) => new(new Vector3(x, 0, 0), Mat3.Identity, new Vector3(half));

    private static ItemsInSpace<Box> SpaceOf(params Box[] boxes) =>
        ItemsBySpace<Box>.Create(boxes, _ => Space, box => box, _ => true, EqualityComparer<RecordKey>.Default, new Execution(1)).In(Space);

    [Fact]
    public void GridCandidatesIncludeEveryItemInATouchedCellEvenWhenItsBoxIsNotReached()
    {
        var grid = GridCandidates.OfBoxes([Around(0), Around(100), Around(5000)]);
        var found = new List<int>();

        grid.CollectNear(new Box(new Vector3(200, 0, 0), new Vector3(200, 0, 0)), found);

        Assert.Equal(new[] { 0, 1 }, found);
    }

    [Fact]
    public void GridCandidatesAreAscendingAndDistinctForAnItemInSeveralCells()
    {
        var grid = GridCandidates.OfBoxes([new Box(new Vector3(0), new Vector3(1500, 1500, 10)), Around(600)]);
        var found = new List<int> { 99 };

        grid.CollectNear(new Box(new Vector3(0), new Vector3(2000, 2000, 0)), found);

        Assert.Equal(new[] { 0, 1 }, found);
    }

    [Fact]
    public void RadiusQueryKeepsOnlyTheCandidatesTheExactTestAccepts()
    {
        var grid = GridCandidates.OfPoints([new Vector3(0, 0, 0), new Vector3(100, 0, 0), new Vector3(100, 100, 0), new Vector3(2000, 0, 0)]);
        var points = new[] { new Vector3(0, 0, 0), new Vector3(100, 0, 0), new Vector3(100, 100, 0), new Vector3(2000, 0, 0) };
        var found = new List<int>();

        grid.CollectWithinRadius(Vector3.Zero, 120f, item => points[item].Length() <= 120f, found);

        Assert.Equal(new[] { 0, 1 }, found);
    }

    [Fact]
    public void RadiusQueryRunsTheExactTestOnlyForGridCandidates()
    {
        var grid = GridCandidates.OfPoints(
            [Vector3.Zero, .. Enumerable.Range(1, 8).Select(i => new Vector3(i * 1000f, 0, 0))]);
        var tested = new List<int>();

        grid.CollectWithinRadius(Vector3.Zero, 10f, item =>
        {
            tested.Add(item);
            return true;
        }, []);

        Assert.Equal(new[] { 0 }, tested);
    }

    [Fact]
    public void OverlappingKeepsTheSlotsTheExactTestAcceptsAndCountsEveryOverlappingBox()
    {
        var space = SpaceOf(Around(0), Around(5), Around(8), Around(5000));
        var kept = new List<int>();

        var overlapping = space.Overlapping(Around(5, half: 2), slot => slot != 1, new SpatialQueryScratch(), kept);

        Assert.Equal(3, overlapping);
        Assert.Equal(new[] { 0, 2 }, kept);
    }

    [Fact]
    public void OverlappingReplacesTheListItIsGiven()
    {
        var space = SpaceOf(Around(0));
        var kept = new List<int> { 7, 8 };

        space.Overlapping(Around(5000), _ => true, new SpatialQueryScratch(), kept);

        Assert.Empty(kept);
    }

    [Fact]
    public void FirstOverlappingReturnsTheLowestIncludedSlotThatPassesTheExactTest()
    {
        var space = SpaceOf(Around(0), Around(0), Around(0), Around(0));
        var points = new[] { 500f, 1f, 2f, 3f };

        var found = space.FirstOverlapping(Around(0), slot => slot != 1, slot => points[slot] <= 10f, new SpatialQueryScratch());

        Assert.Equal(2, found);
    }

    [Fact]
    public void FirstOverlappingIgnoresItemsWhoseBoxIsFarFromTheArea()
    {
        var space = SpaceOf(Around(5000));
        var asked = new List<int>();

        var found = space.FirstOverlapping(Around(0), _ => true, slot =>
        {
            asked.Add(slot);
            return true;
        }, new SpatialQueryScratch());

        Assert.Equal(-1, found);
        Assert.Empty(asked);
    }

    [Fact]
    public void NearPairFinderListsSortedNeighboursWithinTheToleranceOfTheSameGroup()
    {
        OrientedBox[] boxes = [Upright(0), Upright(25), Upright(21), Upright(1000), Upright(25)];
        RecordKey[] groups = [Space, Space, Space, Space, OtherSpace];

        var finder = NearPairFinder.Create(boxes, item => groups[item], tolerance: 2f, new bool[5]);

        Assert.Equal(new[] { 2 }, finder.NeighboursOf(0));
        Assert.Equal(new[] { 2 }, finder.NeighboursOf(1));
        Assert.Equal(new[] { 0, 1 }, finder.NeighboursOf(2));
        Assert.Empty(finder.NeighboursOf(3));
        Assert.Empty(finder.NeighboursOf(4));
    }

    [Fact]
    public void NearPairFinderIgnoresExcludedBoxesInBothDirections()
    {
        OrientedBox[] boxes = [Upright(0), Upright(5), Upright(10)];

        var finder = NearPairFinder.Create(boxes, _ => Space, tolerance: 0f, [false, true, false]);

        Assert.Equal(new[] { 2 }, finder.NeighboursOf(0));
        Assert.Empty(finder.NeighboursOf(1));
        Assert.Equal(new[] { 0 }, finder.NeighboursOf(2));
    }

    [Fact]
    public void LocalityOrderSortsBySpaceThenExteriorCellThenIndex()
    {
        var cell = ExteriorGrid.CellSize;
        (RecordKey Space, Vector3 Position)[] items =
        [
            (OtherSpace, new Vector3(0, 0, 0)),
            (Space, new Vector3(cell, 0, 0)),
            (Space, new Vector3(0, cell, 0)),
            (Space, new Vector3(-cell, 0, 0)),
            (Space, new Vector3(cell / 2, cell / 2, 0)),
            (Space, new Vector3(0, 0, 0)),
        ];

        var order = LocalityOrder.Of(items, item => item.Space, item => item.Position);

        Assert.Equal(new[] { 3, 4, 5, 2, 1, 0 }, order.ToArray());
    }

    [Fact]
    public void LocalityOrderOfNothingIsEmpty()
    {
        Assert.Empty(LocalityOrder.Of(Array.Empty<Vector3>(), _ => Space, position => position));
    }
}
