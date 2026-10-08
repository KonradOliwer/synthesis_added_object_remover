using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Geometry;

public class TransformTests
{
    private const float HalfTurn = MathF.PI / 2;

    [Fact]
    public void HeadingOfQuarterTurnFacesEast()
    {
        var rotation = Mat3.FromEuler(new Vector3(0, 0, HalfTurn));
        VectorAssert.Near(Vector3.UnitX, rotation.Transform(Vector3.UnitY));
        VectorAssert.Near(-Vector3.UnitY, rotation.Transform(Vector3.UnitX));
    }

    [Fact]
    public void QuarterTurnAboutXTiltsForwardDown()
    {
        var rotation = Mat3.FromEuler(new Vector3(HalfTurn, 0, 0));
        VectorAssert.Near(-Vector3.UnitZ, rotation.Transform(Vector3.UnitY));
    }

    [Fact]
    public void RotationIsOrthonormalWithCreationEngineFirstRow()
    {
        var random = new Random(1);
        for (var i = 0; i < 100; i++)
        {
            var (x, y, z) = (TestMeshes.RandomAngle(random), TestMeshes.RandomAngle(random), TestMeshes.RandomAngle(random));
            var r = Mat3.FromEuler(new Vector3(x, y, z));
            var product = r * r.Transposed();
            AssertIdentity(product);
            var determinant =
                r.M11 * (r.M22 * r.M33 - r.M23 * r.M32)
                - r.M12 * (r.M21 * r.M33 - r.M23 * r.M31)
                + r.M13 * (r.M21 * r.M32 - r.M22 * r.M31);
            Assert.Equal(1f, determinant, 1e-4f);
            Assert.Equal(MathF.Cos(y) * MathF.Cos(z), r.M11, 1e-5f);
            Assert.Equal(MathF.Cos(y) * MathF.Sin(z), r.M12, 1e-5f);
            Assert.Equal(-MathF.Sin(y), r.M13, 1e-5f);
        }
    }

    [Fact]
    public void ToLocalInvertsToWorld()
    {
        var random = new Random(2);
        for (var i = 0; i < 100; i++)
        {
            var transform = RandomTransform(random, scale: 2.5f);
            var v = TestMeshes.RandomVector(random, 100);
            VectorAssert.Near(v, transform.ToLocal(transform.ToWorld(v)), 1e-3f);
        }
    }

    [Fact]
    public void RelativeTransformMatchesWorldRoundTrip()
    {
        var random = new Random(3);
        for (var i = 0; i < 100; i++)
        {
            var from = RandomTransform(random, scale: 0.5f + (float)random.NextDouble() * 3);
            var to = RandomTransform(random, scale: 0.5f + (float)random.NextDouble() * 3);
            var v = TestMeshes.RandomVector(random, 50);
            VectorAssert.Near(to.ToLocal(from.ToWorld(v)), RelativeTransform.Create(from, to).Apply(v), 1e-2f);
        }
    }

    [Fact]
    public void RelativeTransformBoxEnclosesTransformedCorners()
    {
        var random = new Random(4);
        var box = new Box(new Vector3(-1, -2, -3), new Vector3(4, 5, 6));
        for (var i = 0; i < 50; i++)
        {
            var relative = RelativeTransform.Create(RandomTransform(random, 1.5f), RandomTransform(random, 0.7f));
            var aabb = relative.ApplyToBox(box).Grown(1e-3f);
            foreach (var corner in OrientedBox.FromLocal(box, new PlacedTransform(Vector3.Zero, Mat3.Identity, 1)).Corners())
            {
                Assert.True(aabb.Contains(relative.Apply(corner)));
            }
        }
    }

    [Theory]
    [InlineData(-0.001f, -1)]
    [InlineData(0f, 0)]
    [InlineData(4095.999f, 0)]
    [InlineData(4096f, 1)]
    [InlineData(-4096f, -1)]
    [InlineData(-4096.001f, -2)]
    public void ExteriorCellIndexFloors(float coordinate, int expected) =>
        Assert.Equal(expected, ExteriorGrid.CellIndex(coordinate));

    [Theory]
    [InlineData(null, 1f)]
    [InlineData(0f, 1f)]
    [InlineData(-2f, 1f)]
    [InlineData(float.NaN, 1f)]
    [InlineData(float.PositiveInfinity, 1f)]
    [InlineData(2.5f, 2.5f)]
    public void ScaleIsNormalized(float? scale, float expected) =>
        Assert.Equal(expected, ReferenceScale.Normalize(scale));

    internal static PlacedTransform RandomTransform(Random random, float scale) => new(
        TestMeshes.RandomVector(random, 1000),
        Mat3.FromEuler(new Vector3(TestMeshes.RandomAngle(random), TestMeshes.RandomAngle(random), TestMeshes.RandomAngle(random))),
        scale);

    private static void AssertIdentity(Mat3 m)
    {
        const float tolerance = 1e-5f;
        Assert.Equal(1f, m.M11, tolerance);
        Assert.Equal(1f, m.M22, tolerance);
        Assert.Equal(1f, m.M33, tolerance);
        Assert.Equal(0f, m.M12, tolerance);
        Assert.Equal(0f, m.M13, tolerance);
        Assert.Equal(0f, m.M21, tolerance);
        Assert.Equal(0f, m.M23, tolerance);
        Assert.Equal(0f, m.M31, tolerance);
        Assert.Equal(0f, m.M32, tolerance);
    }
}
