using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Meshes;

public class WaistBandSizingTests
{
    private const float ArmsBesideTorsoFactor = 1.6f;

    [Fact]
    public void TPoseBodyIsSizedByItsWaistNotItsSpreadArms()
    {
        var size = WaistBandSizing.Measure([TestBodies.Vertices(TestBodies.TPose(height: 120f))]);

        Assert.NotNull(size);
        Assert.Equal(TestBodies.ArmSpan, size.Value.Raw.Size.X, 3);
        Assert.Equal(2 * TestBodies.TorsoHalfWidth * ArmsBesideTorsoFactor, size.Value.Body.Size.X, 3);
        Assert.Equal(2 * TestBodies.TorsoHalfDepth * ArmsBesideTorsoFactor, size.Value.Body.Size.Y, 3);
        Assert.Equal(0f, size.Value.Body.Min.Z);
        Assert.Equal(120f, size.Value.Body.Max.Z);
    }

    [Fact]
    public void WaistIsMeasuredOverEveryMeshOfTheBody()
    {
        var torso = TestBodies.Vertices(TestBodies.TPose(height: 120f));
        var wideHips = TestBodies.Vertices(TestMeshes.BoxTriangles(new Box(new Vector3(-30, -5, 50), new Vector3(30, 5, 60))));

        var size = WaistBandSizing.Measure([torso, wideHips]);

        Assert.Equal(60f * ArmsBesideTorsoFactor, size!.Value.Body.Size.X, 3);
    }

    [Fact]
    public void MeshWithoutVerticesInTheWaistBandKeepsItsFullWidth()
    {
        var feetAndHead = TestBodies.Vertices(TestMeshes.BoxTriangles(new Box(new Vector3(-10, -10, 0), new Vector3(10, 10, 5))))
            .Concat(TestBodies.Vertices(TestMeshes.BoxTriangles(new Box(new Vector3(-8, -8, 110), new Vector3(8, 8, 120)))))
            .ToArray();

        var size = WaistBandSizing.Measure([feetAndHead]);

        Assert.Equal(20f * ArmsBesideTorsoFactor, size!.Value.Body.Size.X, 3);
    }

    [Fact]
    public void FlatOrEmptyMeshHasNoSize()
    {
        Assert.Null(WaistBandSizing.Measure([]));
        Assert.Null(WaistBandSizing.Measure([[new Vector3(0, 0, 5), new Vector3(10, 0, 5)]]));
    }
}
