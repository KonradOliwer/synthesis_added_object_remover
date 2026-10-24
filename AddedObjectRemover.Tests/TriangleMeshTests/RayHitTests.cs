using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.TriangleMeshTests;

public class RayHitTests
{
    private static readonly MeshTriangleTree Slab = BoxMesh.CreateTree(new Box(new Vector3(-10, -10, 0), new Vector3(10, 10, 2)));

    [Fact]
    public void TheNearestHitAlongTheRayIsFoundAsADistanceInUnitsOfTheDirection()
    {
        Assert.True(RayHit.TryFindNearest(Slab, new Vector3(1, 1, 12), new Vector3(0, 0, -2), (_, _) => true, [], out var distance));
        Assert.Equal(5f, distance, 1e-5f);
    }

    [Fact]
    public void AHitThatDoesNotCountIsPassedThrough()
    {
        Assert.True(RayHit.TryFindNearest(Slab, new Vector3(1, 1, 12), -Vector3.UnitZ, (triangle, _) => triangle.Normal.Z < 0f, [], out var distance));
        Assert.Equal(12f, distance, 1e-5f);
    }

    [Fact]
    public void ARayMissingTheMeshOrPointingAwayHitsNothing()
    {
        Assert.False(RayHit.TryFindNearest(Slab, new Vector3(50, 0, 12), -Vector3.UnitZ, (_, _) => true, [], out _));
        Assert.False(RayHit.TryFindNearest(Slab, new Vector3(1, 1, 12), Vector3.UnitZ, (_, _) => true, [], out _));
    }

    [Fact]
    public void TheBoxMeshTurnsItsTopFaceUp() =>
        Assert.All(TestMeshes.BoxTriangles(new Box(Vector3.Zero, Vector3.One)).Where(triangle => triangle.Bounds.Min.Z == 1f), triangle => Assert.True(triangle.Normal.Z > 0f));
}
