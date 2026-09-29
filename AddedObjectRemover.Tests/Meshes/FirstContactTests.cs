using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Meshes;

public class FirstContactTests
{
    private const float Tolerance = 1f;

    private static readonly Box Big = new(new Vector3(-20), new Vector3(20));
    private static readonly Box Small = new(new Vector3(-2), new Vector3(2));

    [Fact]
    public void EnclosedObjectIsInContactOnlyWhenEnclosureCounts()
    {
        var tester = CreateTester(("big.nif", Vector3.Zero), ("small.nif", new Vector3(3, 2, 1)));
        TargetPair[] pairs = [new(0, 1)];

        Assert.Empty(tester.FindFirstInContact(pairs, ContactRule.Touch, new ParallelOptions()).Found);
        Assert.Equal(pairs, tester.FindFirstInContact(pairs, ContactRule.TouchOrEnclose, new ParallelOptions()).Found);
    }

    [Fact]
    public void EachCandidateStopsAtItsFirstContactInPairOrder()
    {
        // Candidates 2 and 3 each touch both frontier objects 0 and 1; candidate 4 touches nothing.
        var tester = CreateTester(
            ("small.nif", new Vector3(0, 0, 0)),
            ("small.nif", new Vector3(8, 0, 0)),
            ("small.nif", new Vector3(4, 0, 0)),
            ("small.nif", new Vector3(4, 0, 3)),
            ("small.nif", new Vector3(100, 0, 0)));
        TargetPair[] pairs = [new(1, 3), new(0, 2), new(0, 4), new(1, 2), new(0, 3)];

        var (found, work) = tester.FindFirstInContact(pairs, ContactRule.Touch, new ParallelOptions { MaxDegreeOfParallelism = 4 });

        Assert.Equal(new[] { new TargetPair(1, 3), new TargetPair(0, 2) }, found);
        Assert.Equal((PairsTested: 3, TouchingPairs: 2, PairsWithoutGeometry: 0), (work.PairsTested, work.TouchingPairs, work.PairsWithoutGeometry));
        Assert.True(work.TrianglePairsTested > 0);
    }

    private static TouchPairTester CreateTester(params (string Mesh, Vector3 Position)[] objects)
    {
        var targets = objects.Select((entry, i) => TestTargets.Create(i, TestTargets.At(entry.Position))).ToList();
        var cache = new TriangleStore(path => BoxMesh.CreateGeometry(path == "big.nif" ? Big : Small));
        return new TouchPairTester(targets, new TargetMeshPaths(objects.Select(entry => (string?)entry.Mesh).ToArray()), cache, Tolerance);
    }
}
