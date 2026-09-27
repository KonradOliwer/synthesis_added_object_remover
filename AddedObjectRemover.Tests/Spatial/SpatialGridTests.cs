using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Spatial;

public class SpatialGridTests
{
    [Fact]
    public void NegativePointIsFoundByQueriesAroundIt()
    {
        var grid = SpatialGrid.FromPoints([new Vector3(-0.1f, -0.1f, 0)]);
        Assert.Contains(0, Collect(grid, new Box(new Vector3(-1, -1, 0), Vector3.Zero)));
        Assert.Contains(0, Collect(grid, new Box(new Vector3(-512, -512, 0), new Vector3(-0.01f, -0.01f, 0))));
        Assert.DoesNotContain(0, Collect(grid, new Box(new Vector3(0.01f, 0.01f, 0), new Vector3(1, 1, 0))));
    }

    [Fact]
    public void OversizeItemIsAlwaysReturned()
    {
        var grid = SpatialGrid.FromBoxes([new Box(new Vector3(-5e4f), new Vector3(5e4f)), new Box(Vector3.Zero, Vector3.One)]);
        Assert.Contains(0, Collect(grid, new Box(new Vector3(9e5f), new Vector3(9e5f))));
    }

    [Fact]
    public void NonFiniteQueriesTerminate()
    {
        var grid = SpatialGrid.FromPoints([Vector3.Zero, new Vector3(1000, 1000, 0)]);
        Assert.Contains(0, Collect(grid, new Box(new Vector3(float.NaN), new Vector3(float.NaN))));
        Assert.Equal(new[] { 0, 1 }, Collect(grid, new Box(new Vector3(float.NegativeInfinity), new Vector3(float.PositiveInfinity))).Order());
    }

    [Fact]
    public void QueriesFindEveryOverlappingItem()
    {
        var random = new Random(10);
        for (var round = 0; round < 20; round++)
        {
            var boxes = Enumerable.Range(0, 300).Select(_ => RandomBox(random)).ToList();
            var grid = SpatialGrid.FromBoxes(boxes);
            for (var query = 0; query < 100; query++)
            {
                var area = RandomBox(random);
                var expected = Enumerable.Range(0, boxes.Count).Where(i => OverlapsXy(boxes[i], area)).ToHashSet();
                Assert.Subset(Collect(grid, area).ToHashSet(), expected);
            }
        }
    }

    [Fact]
    public void CellCountCoversTheXyRange()
    {
        Assert.Equal(1, SpatialGrid.CountCells(new Box(new Vector3(-0.5f), new Vector3(-0.1f))));
        Assert.Equal(4, SpatialGrid.CountCells(new Box(new Vector3(-0.5f), new Vector3(0.5f))));
        Assert.Equal(9, SpatialGrid.CountCells(new Box(new Vector3(-512), new Vector3(512, 512, 0))));
    }

    private static List<int> Collect(SpatialGrid grid, Box area)
    {
        var results = new List<int>();
        grid.Collect(area, results);
        return results;
    }

    private static bool OverlapsXy(Box a, Box b) =>
        a.Min.X <= b.Max.X && b.Min.X <= a.Max.X && a.Min.Y <= b.Max.Y && b.Min.Y <= a.Max.Y;

    /// <summary>Mostly small boxes around the origin (negative coordinates included), some huge.</summary>
    private static Box RandomBox(Random random)
    {
        var center = TestMeshes.RandomVector(random, 5000);
        var size = random.Next(10) == 0 ? 60000f : 700f;
        return Box.FromCorners(center, center + TestMeshes.RandomVector(random, size));
    }
}
