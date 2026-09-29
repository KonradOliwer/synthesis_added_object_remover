using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Determinism;

/// <summary>Results must not depend on the number of threads.</summary>
public class ParallelDeterminismTests
{
    private const int TargetCount = 150;
    private const int MeshKinds = 5;
    private const float PairRange = 40f;
    private const float Tolerance = 2f;

    private sealed record PairResults(
        PairTouch[] Touches,
        List<TargetPair> FirstTouching,
        List<TargetPair> FirstInContact,
        float[] Distances,
        PairTestStats TouchesWork,
        PairTestStats FirstTouchingWork,
        PairTestStats FirstInContactWork);

    [Fact]
    public void TouchAndContactResultsMatchForOneAndEightThreads()
    {
        var sequential = RunTouchTests(maxDegreeOfParallelism: 1);
        var parallel = RunTouchTests(maxDegreeOfParallelism: 8);

        Assert.Contains(PairTouch.Touching, sequential.Touches);
        Assert.Contains(PairTouch.Apart, sequential.Touches);
        Assert.NotEmpty(sequential.FirstTouching);
        Assert.Equal(sequential.Touches, parallel.Touches);
        Assert.Equal(sequential.FirstTouching, parallel.FirstTouching);
        Assert.Equal(sequential.FirstInContact, parallel.FirstInContact);
        Assert.Equal(sequential.Distances, parallel.Distances);
        Assert.Equal(sequential.TouchesWork, parallel.TouchesWork);
        Assert.Equal(sequential.FirstTouchingWork, parallel.FirstTouchingWork);
        Assert.Equal(sequential.FirstInContactWork, parallel.FirstInContactWork);
    }

    private static PairResults RunTouchTests(int maxDegreeOfParallelism)
    {
        var random = new Random(12);
        var targets = new List<TargetObject>();
        var paths = new string?[TargetCount];
        for (var i = 0; i < TargetCount; i++)
        {
            var position = TestMeshes.RandomVector(random, 150) with { Z = (float)random.NextDouble() * 20 };
            var transform = TestTargets.At(position, TestMeshes.RandomAngle(random), 0.5f + (float)random.NextDouble() * 2);
            targets.Add(TestTargets.Create(i, transform));
            paths[i] = i % 11 == 0 ? null : $"mesh{i % MeshKinds}.nif";
        }

        var cache = new TriangleStore(path => BoxMesh.CreateGeometry(MeshBox(path)));
        var meshPaths = new TargetMeshPaths(paths);
        var tester = new TouchPairTester(targets, meshPaths, cache, Tolerance);
        var pairs = FindNearbyPairs(targets, meshPaths);
        var options = new Execution(maxDegreeOfParallelism);

        var (touches, touchesWork) = tester.TestPairs(pairs, options);
        var (firstTouching, firstTouchingWork) = tester.FindFirstInContact(pairs, ContactRule.Touch, options);
        var (firstInContact, firstInContactWork) = tester.FindFirstInContact(pairs, ContactRule.TouchOrEnclose, options);
        var scratch = new TouchScratch();
        var distances = pairs.Select((pair, k) => touches[k] == PairTouch.Touching ? tester.MeasureMinSurfaceDistance(pair, scratch) : float.NaN).ToArray();
        return new PairResults(touches, firstTouching, firstInContact, distances, touchesWork, firstTouchingWork, firstInContactWork);
    }

    /// <summary>Mesh kind k is a box of size 5 + 6k, so larger boxes can enclose smaller ones.</summary>
    private static Box MeshBox(string path)
    {
        var kind = path[4] - '0';
        var half = 2.5f + 3f * kind;
        return new Box(new Vector3(-half), new Vector3(half));
    }

    private static List<TargetPair> FindNearbyPairs(List<TargetObject> targets, TargetMeshPaths meshPaths)
    {
        var pairs = new List<TargetPair>();
        for (var i = 0; i < targets.Count; i++)
        {
            for (var j = 0; j < targets.Count; j++)
            {
                if (i == j || !meshPaths.HasMesh(i) || !meshPaths.HasMesh(j)) continue;
                if (Vector3.Distance(targets[i].Transform.Position, targets[j].Transform.Position) <= PairRange) pairs.Add(new TargetPair(i, j));
            }
        }
        return pairs;
    }
}
