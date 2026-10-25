using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.TriangleMeshTests;

public class MeshTriangleTreeTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(1000)]
    public void CoincidentCentroidsBuildAndListEveryTriangleOnce(int count)
    {
        var triangles = Enumerable.Range(0, count)
            .Select(i => new MeshTriangle(new Vector3(-1 - i, 0, 0), new Vector3(1 + i, 0, 0), Vector3.Zero))
            .ToList();
        var tree = TestMeshes.Tree(triangles);

        var found = new List<int>();
        tree.CollectLeafTriangles(tree.Bounds, found);
        Assert.Equal(Enumerable.Range(0, count), found.Order());
    }

    [Fact]
    public void QueryReturnsEveryOverlappingTriangleExactlyOnce()
    {
        var random = new Random(8);
        for (var mesh = 0; mesh < 20; mesh++)
        {
            var triangles = Enumerable.Range(0, random.Next(1, 800)).Select(_ => TestMeshes.RandomTriangle(random, 100, 10)).ToList();
            var tree = TestMeshes.Tree(triangles);
            var found = new List<int>();
            for (var query = 0; query < 50; query++)
            {
                var area = Box.FromCorners(TestMeshes.RandomVector(random, 120), TestMeshes.RandomVector(random, 120));
                tree.CollectLeafTriangles(area, found);

                Assert.Equal(found.Count, found.Distinct().Count());
                var expected = Enumerable.Range(0, triangles.Count).Where(t => triangles[t].Bounds.Overlaps(area));
                Assert.Subset(found.ToHashSet(), expected.ToHashSet());
            }
        }
    }

    /// <summary>The walk stops at the first, a middle or the last listed triangle, or at none.</summary>
    [Fact]
    public void TheWalkVisitsTheListedTrianglesInOrderAndStopsAtTheFirstItAccepts()
    {
        var random = new Random(11);
        for (var mesh = 0; mesh < 20; mesh++)
        {
            var triangles = Enumerable.Range(0, random.Next(1, 800)).Select(_ => TestMeshes.RandomTriangle(random, 100, 10)).ToList();
            var tree = TestMeshes.Tree(triangles);
            var listed = new List<int>();
            for (var query = 0; query < 20; query++)
            {
                var area = Box.FromCorners(TestMeshes.RandomVector(random, 120), TestMeshes.RandomVector(random, 120));
                tree.CollectLeafTriangles(area, listed);
                foreach (var stopAt in new[] { 0, listed.Count / 2, listed.Count - 1, -1 }.Where(stop => stop < listed.Count))
                {
                    var visitor = new StoppingVisitor(stopAt < 0 ? null : listed[stopAt], []);

                    var accepted = tree.AnyLeafTriangle(area, ref visitor);

                    Assert.Equal(stopAt >= 0, accepted);
                    Assert.Equal(stopAt < 0 ? listed : listed.Take(stopAt + 1), visitor.Visited);
                }
            }
        }
    }

    [Fact]
    public void RayQueryReturnsEveryTriangleTheRayHits()
    {
        var random = new Random(10);
        for (var mesh = 0; mesh < 20; mesh++)
        {
            var triangles = Enumerable.Range(0, random.Next(1, 800)).Select(_ => TestMeshes.RandomTriangle(random, 100, 10)).ToList();
            var tree = TestMeshes.Tree(triangles);
            var found = new List<int>();
            for (var query = 0; query < 50; query++)
            {
                var origin = TestMeshes.RandomVector(random, 100);
                var direction = Vector3.Normalize(TestMeshes.RandomVector(random, 1));
                tree.CollectLeafTrianglesAlongRay(origin, direction, found);

                Assert.Equal(found.Count, found.Distinct().Count());
                var hit = Enumerable.Range(0, triangles.Count).Where(t => SurroundingRayTest.RayHitsTriangle(origin, direction, triangles[t]));
                Assert.Subset(found.ToHashSet(), hit.ToHashSet());
            }
        }
    }

    [Fact]
    public void DiagonalRayVisitsOnlyLeavesAlongIt()
    {
        var row = Enumerable.Range(0, 100)
            .Select(i => new MeshTriangle(new Vector3(i, i, 0), new Vector3(i + 0.5f, i, 0), new Vector3(i, i + 0.5f, 0)))
            .ToList();
        var tree = TestMeshes.Tree(row);
        var found = new List<int>();

        tree.CollectLeafTrianglesAlongRay(new Vector3(0.2f, 100, 0), Vector3.Normalize(new Vector3(1, -1, 0)), found);

        Assert.Contains(50, found);
        Assert.True(found.Count <= 8, $"{found.Count} triangles collected.");
    }

    [Fact]
    public void AxisAlignedRayAlongAFlatLeafFindsIt()
    {
        var floor = new MeshTriangle(Vector3.Zero, new Vector3(10, 0, 0), new Vector3(0, 10, 0));
        var tree = TestMeshes.Tree([floor]);
        var found = new List<int>();

        tree.CollectLeafTrianglesAlongRay(new Vector3(1, 1, -5), Vector3.UnitZ, found);
        Assert.Equal(new[] { 0 }, found);

        tree.CollectLeafTrianglesAlongRay(new Vector3(1, 1, 5), Vector3.UnitZ, found);
        Assert.Empty(found);
    }

    [Fact]
    public void QueryOrderIsFixed()
    {
        var random = new Random(9);
        var triangles = Enumerable.Range(0, 500).Select(_ => TestMeshes.RandomTriangle(random, 100, 10)).ToList();
        var area = new Box(new Vector3(-50), new Vector3(50));
        var first = new List<int>();
        var second = new List<int>();
        TestMeshes.Tree(triangles).CollectLeafTriangles(area, first);
        TestMeshes.Tree(triangles).CollectLeafTriangles(area, second);
        Assert.Equal(first, second);
    }

    [Fact]
    public void EmptyMeshIsNotIndexed() =>
        Assert.Null(MeshTriangleTree.Build(new MeshTriangles([], [], []), MeshLimits.Tree));

    [Fact]
    public void BoxMeshIsClosed() => Assert.True(BoxMesh.CreateTree(TestMeshes.UnitCube).IsClosed);

    [Fact]
    public void BoxWithOneTriangleMissingIsOpen() =>
        Assert.False(TestMeshes.Tree(TestMeshes.BoxTriangles(TestMeshes.UnitCube).Skip(1).ToList()).IsClosed);

    [Fact]
    public void DuplicatedSeamVerticesStillCountAsClosed()
    {
        // TestMeshes.Tree gives every triangle its own three vertices, as texture seams do.
        Assert.True(TestMeshes.Tree(TestMeshes.BoxTriangles(TestMeshes.UnitCube)).IsClosed);
    }

    /// <summary>Records every triangle it is handed and accepts only <paramref name="stopAt"/>.</summary>
    private readonly struct StoppingVisitor(int? stopAt, List<int> visited) : ILeafTriangleVisitor
    {
        public List<int> Visited => visited;

        public bool Accepts(int triangle)
        {
            visited.Add(triangle);
            return triangle == stopAt;
        }
    }
}
