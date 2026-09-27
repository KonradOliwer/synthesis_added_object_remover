using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Noggog;

namespace AddedObjectRemover.Tests.Geometry;

public class OrientedBoxTests
{
    private static readonly Vector3 UnitHalf = Vector3.One;

    [Theory]
    [InlineData(2.0f, 0f, true)]
    [InlineData(2.001f, 0f, false)]
    [InlineData(2.001f, 0.01f, true)]
    [InlineData(1.0f, 0f, true)]
    public void AxisAlignedBoxesSeparateOnlyPastTheirFaces(float distance, float padding, bool expected)
    {
        var a = new OrientedBox(Vector3.Zero, Mat3.Identity, UnitHalf);
        var b = new OrientedBox(new Vector3(distance, 0, 0), Mat3.Identity, UnitHalf);
        Assert.Equal(expected, a.Intersects(b, padding));
    }

    [Fact]
    public void BoxContainsARotatedBoxOnlyWhenAllItsCornersAreInside()
    {
        var outer = new OrientedBox(Vector3.Zero, AddedObjectRemover.Geometry.RotationFromEuler(new P3Float(0, 0, 0.3f)), new Vector3(10));
        var rotation = AddedObjectRemover.Geometry.RotationFromEuler(new P3Float(0.5f, 0.2f, 1.1f));
        var inner = new OrientedBox(new Vector3(2, 1, 0), rotation, new Vector3(3, 2, 1));
        var pokingOut = inner with { Center = new Vector3(9.5f, 0, 0) };

        Assert.True(outer.Contains(inner));
        Assert.True(inner.Corners().All(outer.Contains));
        Assert.False(outer.Contains(pokingOut));
        Assert.False(pokingOut.Corners().All(outer.Contains));
        Assert.False(outer.Contains(inner with { HalfExtents = new Vector3(float.NaN) }));
    }

    [Fact]
    public void EdgeToEdgeBoxesAreSeparatedByACrossAxis()
    {
        var a = new OrientedBox(Vector3.Zero, Mat3.Identity, UnitHalf);
        var rotation = AddedObjectRemover.Geometry.RotationFromEuler(new P3Float(MathF.PI / 4, 0, MathF.PI / 4));
        var b = new OrientedBox(new Vector3(2.2f, 2.2f, 0), rotation, UnitHalf);
        Assert.False(a.Intersects(b, 0));
        Assert.False(b.Intersects(a, 0));
    }

    [Fact]
    public void IntersectionIsSymmetricAndNeverMissesASharedPoint()
    {
        var random = new Random(5);
        for (var i = 0; i < 2000; i++)
        {
            var a = RandomBox(random);
            var b = RandomBox(random);
            var intersects = a.Intersects(b, 0);
            Assert.Equal(intersects, b.Intersects(a, 0));
            if (intersects) continue;
            for (var s = 0; s < 50; s++)
            {
                var point = a.Center + a.Rotation.Transform(TestMeshes.RandomVector(random, 1) * a.HalfExtents);
                Assert.False(b.Contains(point), $"Case {i}: shared point {point} but no intersection.");
            }
        }
    }

    [Theory]
    [InlineData(10000f)]
    [InlineData(0f)]
    public void NaNRotationIntersectsNothing(float distance)
    {
        var a = new OrientedBox(Vector3.Zero, Mat3.Identity, UnitHalf);
        var broken = new OrientedBox(
            new Vector3(distance, 0, 0),
            AddedObjectRemover.Geometry.RotationFromEuler(new P3Float(float.NaN, 0, 0)),
            UnitHalf);
        Assert.False(a.Intersects(broken, 0));
        Assert.False(broken.Intersects(a, 0));
        Assert.False(broken.Contains(Vector3.Zero));
    }

    [Fact]
    public void QuarterTurnSwapsWorldAabbExtents()
    {
        var box = new OrientedBox(Vector3.Zero, AddedObjectRemover.Geometry.RotationFromEuler(new P3Float(0, 0, MathF.PI / 2)), new Vector3(1, 2, 3));
        var aabb = box.WorldAabb(0);
        VectorAssert.Near(new Vector3(2, 1, 3), aabb.Max, 1e-5f);
        VectorAssert.Near(new Vector3(-2, -1, -3), aabb.Min, 1e-5f);
    }

    [Fact]
    public void FootprintIsTheProjectedArea()
    {
        var upright = new OrientedBox(Vector3.Zero, Mat3.Identity, new Vector3(1, 2, 3));
        Assert.Equal(8f, upright.FootprintArea, 1e-4f);

        var tipped = upright with { Rotation = AddedObjectRemover.Geometry.RotationFromEuler(new P3Float(MathF.PI / 2, 0, 0)) };
        Assert.Equal(12f, tipped.FootprintArea, 1e-4f);
    }

    [Fact]
    public void FromLocalScalesAboutTheOrigin()
    {
        var local = new Box(new Vector3(1, 1, 1), new Vector3(3, 3, 3));
        var box = OrientedBox.FromLocal(local, TestTargets.At(new Vector3(100, 0, 0), scale: 2));
        VectorAssert.Near(new Vector3(104, 4, 4), box.Center);
        VectorAssert.Near(new Vector3(2, 2, 2), box.HalfExtents);
    }

    [Fact]
    public void ExpandedLocalBoxGrowsBySizeTimesMultiplier()
    {
        var grown = AddedObjectRemover.Geometry.ExpandedLocalBox(new Box(Vector3.Zero, new Vector3(2, 4, 6)), scale: 2, multiplier: 0.5f);
        VectorAssert.Near(new Vector3(-2, -4, -6), grown.Min);
        VectorAssert.Near(new Vector3(6, 12, 18), grown.Max);
    }

    private static OrientedBox RandomBox(Random random) => new(
        TestMeshes.RandomVector(random, 4),
        AddedObjectRemover.Geometry.RotationFromEuler(new P3Float(
            TestMeshes.RandomAngle(random), TestMeshes.RandomAngle(random), TestMeshes.RandomAngle(random))),
        Vector3.Abs(TestMeshes.RandomVector(random, 2)) + new Vector3(0.05f));
}
