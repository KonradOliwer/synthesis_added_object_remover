using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// The touch search's broad phase on random worlds of overlapping blocks in several spaces: a target's neighbours are exactly the
/// other targets taking part in its space whose boxes come within the touch distance, whichever targets were asked about before
/// and however many threads ask.
/// </summary>
public class TouchCandidateFinderTests
{
    private const int Seed = 4711;
    private const int Trials = 10;
    private const int FewWorkers = 1;
    private const int ManyWorkers = 8;

    [Fact]
    public void TheNeighboursOfATargetAreExactlyTheTargetsOfItsSpaceWhoseBoxesComeWithinTheTouchDistance()
    {
        var foundNeighbours = 0;
        foreach (var (world, trial) in TestBlockWorlds.Create(Seed, Trials).Select((world, trial) => (world, trial)))
        {
            var finder = TouchCandidateFinder.Create(world.Targets, world.Excluded, TestBlockWorlds.Shapes, TestBlockWorlds.TouchDistance, new Execution(ManyWorkers));

            foreach (var target in Shuffled(Enumerable.Range(0, TestBlockWorlds.TargetCount), new Random(Seed + trial)))
            {
                var neighbours = finder.FindNeighbors(target);
                Assert.Equal(FindNeighboursOneByOne(world, target), neighbours);
                foundNeighbours += neighbours.Count;
            }
        }
        Assert.True(foundNeighbours > 0, "No world has a target near another.");
    }

    [Fact]
    public void TheBoxOfATargetTakingPartIsItsMeshBoxWhereItStandsAndAskingForAnExcludedTargetThrows()
    {
        var excludedTargets = 0;
        foreach (var world in TestBlockWorlds.Create(Seed, Trials))
        {
            var finder = TouchCandidateFinder.Create(world.Targets, world.Excluded, TestBlockWorlds.Shapes, TestBlockWorlds.TouchDistance, new Execution(ManyWorkers));

            foreach (var target in Enumerable.Range(0, TestBlockWorlds.TargetCount))
            {
                if (world.Excluded[target])
                {
                    Assert.Throws<ArgumentException>(() => finder.BoxOf(target));
                    excludedTargets++;
                }
                else
                {
                    Assert.Equal(TestBlockWorlds.MeasureBox(world.Targets[target]), finder.BoxOf(target));
                }
            }
        }
        Assert.True(excludedTargets > 0, "No world excludes a target.");
    }

    [Fact]
    public void OneOrEightThreadsAskingInAnyOrderFindTheSameNeighbours()
    {
        foreach (var (world, trial) in TestBlockWorlds.Create(Seed, Trials).Select((world, trial) => (world, trial)))
        {
            var order = Shuffled(Enumerable.Range(0, TestBlockWorlds.TargetCount), new Random(Seed + trial));
            var excluded = Enumerable.Range(0, TestBlockWorlds.TargetCount).Where(target => world.Excluded[target]).ToList();

            var few = CreateSearch(world, excluded, FewWorkers).FindNeighborsOfAll(order);
            var many = CreateSearch(world, excluded, ManyWorkers).FindNeighborsOfAll(order);

            Assert.Equal(few, many);
            Assert.Equal(order.Select(target => FindNeighboursOneByOne(world, target)), many);
        }
    }

    [Fact]
    public void TheTouchRoundsDecideAlikeWithOneOrEightWorkers()
    {
        var removedFollowUps = 0;
        foreach (var world in TestBlockWorlds.Create(Seed, Trials))
        {
            var few = ExecuteTouch(world, FewWorkers);
            var many = ExecuteTouch(world, ManyWorkers);

            Assert.Equal(AllDecisions(few.RemovalDecisions), AllDecisions(many.RemovalDecisions));
            Assert.Equal(few.Result.Work, many.Result.Work);
            Assert.Equal(few.TouchChains.Stats, many.TouchChains.Stats);
            removedFollowUps += many.AlsoRemoveRounds.Sum(round => many.RemovalDecisions.RemovedIn(round).Length);
        }
        Assert.True(removedFollowUps > 0, "No world removed anything in its touch rounds.");
    }

    [Fact]
    public void TheSupportRoundsDecideAlikeWithOneOrEightWorkers()
    {
        foreach (var world in TestBlockWorlds.Create(Seed, Trials))
        {
            var objectsOfAnyPlugin = TestScenes.CreateWithSupportOnlyObjects(world.Targets, [], TestBlockWorlds.Shapes).VisibleObjectsOfAnyPlugin();

            var few = ExecuteSupport(world, objectsOfAnyPlugin, FewWorkers);
            var many = ExecuteSupport(world, objectsOfAnyPlugin, ManyWorkers);

            Assert.Equal(AllDecisions(few.RemovalDecisions), AllDecisions(many.RemovalDecisions));
            Assert.Equal(few.Result.Work, many.Result.Work);
        }
    }

    /// <summary>Every other target of the same space taking part, tested pair by pair with the exact box test, the lower index's box grown.</summary>
    private static List<int> FindNeighboursOneByOne(TestBlockWorlds.World world, int target)
    {
        if (world.Excluded[target]) return [];

        return
        [
            .. Enumerable.Range(0, TestBlockWorlds.TargetCount).Where(other =>
                other != target
                && !world.Excluded[other]
                && world.Targets[other].SpaceKey == world.Targets[target].SpaceKey
                && TestBlockWorlds.MeasureBox(world.Targets[Math.Min(target, other)])
                    .Intersects(TestBlockWorlds.MeasureBox(world.Targets[Math.Max(target, other)]), TestBlockWorlds.TouchDistance)),
        ];
    }

    private static TestTouchCascade.Run ExecuteTouch(TestBlockWorlds.World world, int workers) =>
        TestTouchCascade.Execute(
            world.Targets, TestBlockWorlds.Shapes, world.Protection, world.Seeds, TestBlockWorlds.TouchDistance, workers, collectDiagnostics: true);

    private static TestSupportCascade.Run ExecuteSupport(TestBlockWorlds.World world, IVisibleObjectsOfAnyPlugin objectsOfAnyPlugin, int workers) =>
        TestSupportCascade.Execute(
            world.Targets,
            TestBlockWorlds.Shapes,
            world.Protection,
            world.Seeds,
            objectsOfAnyPlugin,
            TestGround.NoTerrain(),
            TestBlockWorlds.TouchDistance,
            TestBlockWorlds.SupportThreshold,
            workers);

    private static TouchSearch CreateSearch(TestBlockWorlds.World world, IReadOnlyList<int> excluded, int workers) =>
        TouchSearch.Create(
            world.Targets, excluded, TestBlockWorlds.Shapes, new TriangleStore(TestBlockWorlds.Shapes.ReadTriangles), TestBlockWorlds.TouchDistance, new Execution(workers));

    private static List<Decision?> AllDecisions(IRemovalDecisions decisions) =>
        [.. Enumerable.Range(0, TestBlockWorlds.TargetCount).Select(target => decisions.Of(new TargetId(target)))];

    private static List<int> Shuffled(IEnumerable<int> items, Random random) => [.. items.OrderBy(_ => random.Next())];
}
