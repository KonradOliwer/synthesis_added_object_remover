using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Rules;

public class SupportDecisionTests
{
    [Theory]
    [InlineData(0.5f, 0.5f, true)]
    [InlineData(0.499995f, 0.5f, true)]
    [InlineData(0.4999f, 0.5f, false)]
    [InlineData(1f, 1f, true)]
    [InlineData(0.999995f, 1f, true)]
    [InlineData(0f, 0.01f, false)]
    public void RemovedWhenRemovedShareReachesThreshold(float removedShare, float threshold, bool expected) =>
        Assert.Equal(expected, SupportRule.ReachesThreshold(removedShare, threshold));

    [Fact]
    public void PointTouchingSeveralSupportersSplitsItsWeight()
    {
        var first = Supporter.Target(3);
        var second = Supporter.Placed(new OtherId(0));
        var contacts = SupportContactFinder.SplitWeightsAmongSupporters(
            [
                new SupportContactFinder.SupporterHits(first, [true, true, false]),
                new SupportContactFinder.SupporterHits(second, [false, true, false]),
                new SupportContactFinder.SupporterHits(Supporter.Terrain, [false, false, false]),
            ],
            [1f, 2f, 4f]);

        Assert.Equal(2, contacts.ContactPoints);
        Assert.Equal(3f, contacts.TotalWeight, 1e-6f);
        Assert.Equal(new[] { new SupporterWeight(first, 2f), new SupporterWeight(second, 1f) }, contacts.Supporters);
    }

    [Fact]
    public void ContactPointsWithoutWeightKeepNoSupportersAndAreDescribed()
    {
        var first = Supporter.Target(3);
        SupportContactFinder.SupporterHits[] hits =
        [
            new(first, [true, true, false]),
            new(Supporter.Terrain, [false, true, false]),
        ];
        float[] weights = [float.NaN, 0f, 2f];
        var samples = new WeightedSurfaceSamples([Vector3.Zero, Vector3.Zero, Vector3.Zero], weights, new Box(Vector3.Zero, new Vector3(3, 4, 0)), Falloff: 0.75f);

        var contacts = SupportContactFinder.SplitWeightsAmongSupporters(hits, weights);
        var diagnostic = ZeroWeightDiagnosis.Of(7, "m.nif", samples, hits, contacts);

        Assert.Equal(2, contacts.ContactPoints);
        Assert.Equal(0f, contacts.TotalWeight);
        Assert.Empty(contacts.Supporters);
        Assert.NotNull(diagnostic);
        Assert.Equal(7, diagnostic.TargetIndex);
        Assert.Equal("m.nif", diagnostic.MeshPath);
        Assert.Equal(0.75f, diagnostic.Falloff);
        Assert.Equal(1.25f, diagnostic.MeshSize);
        Assert.Equal(new SampleWeightCounts(Positive: 1, Zero: 1, NotFinite: 1), diagnostic.Weights);
        Assert.True(double.IsNaN(diagnostic.WeightSum));
        Assert.True(double.IsNaN(diagnostic.TouchingWeightSum));
        Assert.Equal(2, diagnostic.Supporters[0].TouchingSamples);
        Assert.True(float.IsNaN(diagnostic.Supporters[0].RawWeightSum));
        Assert.Equal(new SupporterSamples(Supporter.Terrain, 1, 0f), diagnostic.Supporters[1]);
    }

    [Fact]
    public void TheSamplesKeepTheMeshSizeValueTheirWeightsWereComputedWith()
    {
        var tree = MeshTriangleTree.Build(BoxMesh.CreateTriangles(TestMeshes.UnitCube), MeshLimits.Tree)!;

        var samples = WeightedSurfaceSamples.Create(new SampledSurface([.. SurfaceSampler.Sample(tree, WeightedSurfaceSamples.SamplesPerMesh)], tree.Bounds));

        Assert.True(samples.Falloff > 0);
        Assert.Equal(samples.MeshSize, samples.Falloff);
        Assert.All(
            samples.Points.Zip(samples.Weights),
            sample => Assert.Equal(1f / (1f + MathF.Pow(sample.First.Length() / samples.Falloff, 2)), sample.Second, 1e-6f));
    }

    [Fact]
    public void ContactsWithWeightAreNotDescribed()
    {
        SupportContactFinder.SupporterHits[] hits = [new(Supporter.Terrain, [true, false])];
        float[] weights = [1f, 1f];
        var samples = new WeightedSurfaceSamples([Vector3.Zero, Vector3.Zero], weights, Box.Zero, Falloff: 0f);

        var contacts = SupportContactFinder.SplitWeightsAmongSupporters(hits, weights);

        Assert.Null(ZeroWeightDiagnosis.Of(0, "m.nif", samples, hits, contacts));
    }

    [Fact]
    public void NoContactsGiveNoWeight()
    {
        var contacts = SupportContactFinder.SplitWeightsAmongSupporters(
            [new SupportContactFinder.SupporterHits(Supporter.Terrain, [false, false])],
            [1f, 1f]);
        Assert.Equal(0, contacts.ContactPoints);
        Assert.Empty(contacts.Supporters);
    }
}
