using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Geometry;

public class TriangleDistanceTests
{
    private static readonly Vector3 A = Vector3.Zero;
    private static readonly Vector3 B = Vector3.UnitX;
    private static readonly Vector3 C = Vector3.UnitY;

    public static TheoryData<Vector3, Vector3> VoronoiRegions => new()
    {
        { new Vector3(-1, -1, 0), A },
        { new Vector3(2, -0.5f, 0), B },
        { new Vector3(-0.5f, 2, 0), C },
        { new Vector3(0.5f, -1, 0), new Vector3(0.5f, 0, 0) },
        { new Vector3(-1, 0.5f, 0), new Vector3(0, 0.5f, 0) },
        { new Vector3(1, 1, 0), new Vector3(0.5f, 0.5f, 0) },
        { new Vector3(0.25f, 0.25f, 5), new Vector3(0.25f, 0.25f, 0) },
    };

    [Theory]
    [MemberData(nameof(VoronoiRegions))]
    public void ClosestPointInEachRegion(Vector3 point, Vector3 expected) =>
        VectorAssert.Near(expected, AddedObjectRemover.Geometry.ClosestPointOnTriangle(point, A, B, C));

    [Fact]
    public void CollinearTriangleUsesItsSegment() =>
        VectorAssert.Near(
            new Vector3(1.5f, 0, 0),
            AddedObjectRemover.Geometry.ClosestPointOnTriangle(new Vector3(1.5f, 1, 0), Vector3.Zero, new Vector3(2, 0, 0), Vector3.UnitX));

    [Fact]
    public void PointTriangleIsItsPoint() =>
        VectorAssert.Near(Vector3.One, AddedObjectRemover.Geometry.ClosestPointOnTriangle(Vector3.Zero, Vector3.One, Vector3.One, Vector3.One));

    [Fact]
    public void ClosestPointMatchesDenseSampling()
    {
        var random = new Random(6);
        for (var i = 0; i < 200; i++)
        {
            var t = TestMeshes.RandomTriangle(random, 10, 10);
            var p = TestMeshes.RandomVector(random, 20);
            var exact = MathF.Sqrt(AddedObjectRemover.Geometry.DistanceSquaredToTriangle(p, t.A, t.B, t.C));
            var sampled = SampledPoints(t, 100).Min(q => Vector3.Distance(p, q));
            var resolution = 2 * MaxEdge(t) / 100 + 1e-3f;
            Assert.InRange(sampled - exact, -1e-3f, resolution);
        }
    }

    [Fact]
    public void ParallelTrianglesAreTheirOffsetApart()
    {
        var p = new MeshTriangle(A, B, C);
        var q = new MeshTriangle(A + new Vector3(0, 0, 5), B + new Vector3(0, 0, 5), C + new Vector3(0, 0, 5));
        Assert.Equal(25f, TriangleProximity.MinDistanceSquared(p, q), 1e-4f);
        Assert.True(TriangleProximity.AreWithin(p, q, 25f));
        Assert.False(TriangleProximity.AreWithin(p, q, 24.99f));
    }

    [Fact]
    public void CrossingTrianglesHaveZeroDistance()
    {
        var horizontal = new MeshTriangle(Vector3.Zero, new Vector3(4, 0, 0), new Vector3(0, 4, 0));
        var vertical = new MeshTriangle(new Vector3(1, 1, -1), new Vector3(1, 1, 1), new Vector3(1.5f, 1.5f, 0));
        Assert.Equal(0f, TriangleProximity.MinDistanceSquared(horizontal, vertical));
    }

    [Fact]
    public void CoplanarContainedTriangleHasZeroDistance()
    {
        var outer = new MeshTriangle(Vector3.Zero, new Vector3(10, 0, 0), new Vector3(0, 10, 0));
        var inner = new MeshTriangle(new Vector3(1, 1, 0), new Vector3(2, 1, 0), new Vector3(1, 2, 0));
        Assert.Equal(0f, TriangleProximity.MinDistanceSquared(outer, inner), 1e-6f);
    }

    [Fact]
    public void CoplanarStarOfDavidHasZeroDistance()
    {
        var up = new MeshTriangle(new Vector3(0, 2, 0), new Vector3(-1.732f, -1, 0), new Vector3(1.732f, -1, 0));
        var down = new MeshTriangle(new Vector3(0, -2, 0), new Vector3(1.732f, 1, 0), new Vector3(-1.732f, 1, 0));
        Assert.Equal(0f, TriangleProximity.MinDistanceSquared(up, down), 1e-6f);
    }

    [Fact]
    public void CoplanarApartTrianglesMeasureTheGap()
    {
        var p = new MeshTriangle(A, B, C);
        var shift = new Vector3(4, 0, 0);
        var q = new MeshTriangle(A + shift, B + shift, C + shift);
        Assert.Equal(9f, TriangleProximity.MinDistanceSquared(p, q), 1e-4f);
    }

    [Fact]
    public void DegenerateTrianglesUseTheirPointsAndEdges()
    {
        var face = new MeshTriangle(A, B, C);
        var point = new MeshTriangle(new Vector3(0.2f, 0.2f, 3), new Vector3(0.2f, 0.2f, 3), new Vector3(0.2f, 0.2f, 3));
        var segment = new MeshTriangle(new Vector3(-1, -1, 2), new Vector3(3, -1, 2), new Vector3(1, -1, 2));
        Assert.Equal(9f, TriangleProximity.MinDistanceSquared(face, point), 1e-4f);
        Assert.Equal(5f, TriangleProximity.MinDistanceSquared(segment, face), 1e-4f);
    }

    [Fact]
    public void MinDistanceMatchesSamplingAndAreWithinAgrees()
    {
        var random = new Random(7);
        for (var i = 0; i < 300; i++)
        {
            var p = TestMeshes.RandomTriangle(random, 3, 4);
            var q = TestMeshes.RandomTriangle(random, 3, 4);
            var exact = MathF.Sqrt(TriangleProximity.MinDistanceSquared(p, q));
            var sampled = MathF.Min(SampledDistance(p, q), SampledDistance(q, p));
            var resolution = 2 * MathF.Max(MaxEdge(p), MaxEdge(q)) / 60 + 1e-3f;
            Assert.InRange(sampled - exact, -1e-3f, resolution);

            var squared = exact * exact;
            Assert.True(TriangleProximity.AreWithin(p, q, squared * 1.001f + 1e-6f), $"Case {i}");
            if (squared > 1e-3f) Assert.False(TriangleProximity.AreWithin(p, q, squared * 0.99f), $"Case {i}");
        }
    }

    [Fact]
    [Trait(KnownBug.Trait, "A3: an edge crossing a face at a sine below 1e-3 skips the crossing test, so intersecting triangles report a small non-zero distance")]
    public void ShallowCrossingIsReportedApart()
    {
        var p = new MeshTriangle(Vector3.Zero, new Vector3(1000, 0, 0), new Vector3(0, 1000, 0));
        var q = new MeshTriangle(new Vector3(-100, 100, -0.05f), new Vector3(900, 100, 0.05f), new Vector3(-100, 110, -0.05f));
        Assert.InRange(TriangleProximity.MinDistanceSquared(p, q), 1e-4f, 1e-2f);
        Assert.False(TriangleProximity.AreWithin(p, q, 0f));
    }

    private static float SampledDistance(MeshTriangle sampled, MeshTriangle face) =>
        SampledPoints(sampled, 60).Min(point => MathF.Sqrt(AddedObjectRemover.Geometry.DistanceSquaredToTriangle(point, face.A, face.B, face.C)));

    private static IEnumerable<Vector3> SampledPoints(MeshTriangle t, int steps)
    {
        for (var i = 0; i <= steps; i++)
        {
            for (var j = 0; j <= steps - i; j++)
            {
                var u = i / (float)steps;
                var v = j / (float)steps;
                yield return t.A + (t.B - t.A) * u + (t.C - t.A) * v;
            }
        }
    }

    private static float MaxEdge(MeshTriangle t) =>
        MathF.Max(Vector3.Distance(t.A, t.B), MathF.Max(Vector3.Distance(t.B, t.C), Vector3.Distance(t.C, t.A)));
}
