using System.Numerics;

namespace AddedObjectRemover.Tests.TriangleMeshTests;

/// <summary>Two triangles overlap from above only when they share an area in X and Y; height plays no part.</summary>
public class TriangleFootprintsTests
{
    private static readonly MeshTriangle Lower = new(Vector3.Zero, new Vector3(10, 0, 0), new Vector3(0, 10, 0));

    [Fact]
    public void ATriangleCrossingAnotherAtAnotherHeightOverlapsIt()
    {
        var crossing = new MeshTriangle(new Vector3(2, 2, 500), new Vector3(20, 2, 500), new Vector3(2, 20, 500));

        Assert.True(TriangleFootprints.Overlap(Lower, crossing));
        Assert.True(TriangleFootprints.Overlap(crossing, Lower));
    }

    [Fact]
    public void ATriangleSharingOnlyAnEdgeDoesNotOverlap()
    {
        var beside = new MeshTriangle(new Vector3(10, 0, 0), new Vector3(10, 10, 0), new Vector3(0, 10, 0));

        Assert.False(TriangleFootprints.Overlap(Lower, beside));
    }

    [Fact]
    public void ATriangleApartDoesNotOverlap()
    {
        var apart = new MeshTriangle(new Vector3(20, 20, 0), new Vector3(30, 20, 0), new Vector3(20, 30, 0));

        Assert.False(TriangleFootprints.Overlap(Lower, apart));
    }

    [Fact]
    public void ATriangleInsideAnotherOverlapsIt()
    {
        var inside = new MeshTriangle(new Vector3(1, 1, 0), new Vector3(3, 1, 0), new Vector3(1, 3, 0));

        Assert.True(TriangleFootprints.Overlap(Lower, inside));
    }

    [Fact]
    public void AVerticalTriangleHasNoAreaFromAboveAndOverlapsNothing()
    {
        var vertical = new MeshTriangle(new Vector3(1, 1, 0), new Vector3(5, 1, 0), new Vector3(1, 1, 10));

        Assert.False(TriangleFootprints.Overlap(Lower, vertical));
    }

    /// <summary>A flat sliver whose three corners lie on one line crosses the other triangle; no edge normal separates them, yet no area is shared.</summary>
    [Fact]
    public void ASliverWithItsCornersOnOneLineCrossingATriangleOverlapsNothing()
    {
        var sliver = new MeshTriangle(Vector3.Zero, new Vector3(2, 0, 0), new Vector3(4, 0, 0));
        var crossed = new MeshTriangle(new Vector3(1, -1, 0), new Vector3(3, -1, 0), new Vector3(2, 1, 0));

        Assert.False(TriangleFootprints.Overlap(sliver, crossed));
        Assert.False(TriangleFootprints.Overlap(crossed, sliver));
    }
}
