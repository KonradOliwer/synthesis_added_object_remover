using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.TriangleMeshTests;

public class MeshContactTests
{
    private static readonly PlacedTransform Identity = TestTargets.At(Vector3.Zero);
    private static readonly MeshTriangleTree Room = BoxMesh.CreateTree(new Box(new Vector3(-10), new Vector3(10)));
    private static readonly MeshTriangleTree Small = BoxMesh.CreateTree(new Box(new Vector3(-1), new Vector3(1)));

    [Theory]
    [InlineData(10.5f, true)]
    [InlineData(12f, false)]
    public void BoxOverlapsMeshWhenATriangleReachesIt(float boxCentreX, bool expected)
    {
        var box = new Box(new Vector3(-1), new Vector3(1));
        var boxAt = TestTargets.At(new Vector3(boxCentreX, 0, 0));

        Assert.Equal(expected, MeshContact.BoxOverlapsMesh(Room, Identity, box, boxAt, []));
    }

    [Fact]
    public void PointInsideIsTrueOnlyInsideAClosedMesh()
    {
        Assert.True(MeshContact.PointInside(Room, new Vector3(1, 2, 3), Mat3.Identity, []));
        Assert.False(MeshContact.PointInside(Room, new Vector3(11, 0, 0), Mat3.Identity, []));
    }

    [Theory]
    [InlineData(3f, 0f, true)]
    [InlineData(3f, 1f, true)]
    [InlineData(5f, 0f, false)]
    public void SamplesTouchingUseTheGapInWorldUnits(float sampleX, float gap, bool expected)
    {
        var samples = new[] { new Vector3(sampleX, 0, 0) };
        var samplesAt = TestTargets.At(new Vector3(7, 0, 0));

        // The sample is at world x = 7 + sampleX; Small's +X face is at world x = 10 when placed at x = 9 at scale 1.
        var touching = MeshContact.SamplesTouching(samples, samplesAt, Small, TestTargets.At(new Vector3(9, 0, 0)), gap, []);

        Assert.Equal(expected, touching[0]);
    }

    [Theory]
    [InlineData(11.5f, 1f, true)]
    [InlineData(12.5f, 1f, false)]
    [InlineData(12.5f, 2f, true)]
    public void SamplesTouchingMeasureTheGapInWorldUnitsAtAnyScale(float sampleWorldX, float gap, bool expected)
    {
        var samples = new[] { new Vector3(sampleWorldX, 0, 0) };
        var doubled = TestTargets.At(new Vector3(9, 0, 0), scale: 2f);

        // At scale 2 the mesh's +X face is at world x = 11, and a world gap of 1 is half a unit in the mesh's own frame.
        var touching = MeshContact.SamplesTouching(samples, Identity, Small, doubled, gap, []);

        Assert.Equal(expected, touching[0]);
    }

    [Fact]
    public void TouchesOrEnclosesPartAcceptsAPartInsideAClosedMeshOnly()
    {
        var inside = TestTargets.At(new Vector3(0, 0, 0));
        var far = TestTargets.At(new Vector3(100, 0, 0));
        var touching = TestTargets.At(new Vector3(11, 0, 0));

        Assert.True(MeshContact.TouchesOrEnclosesPart(Room, Identity, Small, inside, 0f, new TouchScratch()));
        Assert.False(MeshContact.TouchesOrEnclosesPart(Room, Identity, Small, far, 0f, new TouchScratch()));
        Assert.True(MeshContact.TouchesOrEnclosesPart(Room, Identity, Small, touching, 0f, new TouchScratch()));
        Assert.False(MeshContact.TouchesOrEnclosesPart(Small, inside, Room, far, 0f, new TouchScratch()));
    }

    [Fact]
    public void AnOpenMeshEnclosesNoPart()
    {
        var openRoom = TestMeshes.Tree(TestMeshes.BoxWithoutFace(new Box(new Vector3(-10), new Vector3(10)), v => v.Z >= 10));

        Assert.False(MeshContact.TouchesOrEnclosesPart(openRoom, Identity, Small, Identity, 0f, new TouchScratch()));
    }
}
