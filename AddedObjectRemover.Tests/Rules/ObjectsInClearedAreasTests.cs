using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;
using static AddedObjectRemover.Tests.Fixtures.TestOneRoundSteps;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// The area method of "Objects left behind": each of the cleaned mod's objects that still stands is removed when, in the column
/// around it, almost nothing of the cleaned mod's drawn objects still stands, by number and by size; every object is judged once,
/// on the earlier steps' decisions. Over in-memory target objects.
/// </summary>
public sealed class ObjectsInClearedAreasTests
{
    private const float Radius = 1000f;
    private const float Spacing = 100f;

    private static readonly ModKey ShapeMod = ModKey.FromNameAndExtension("ClearedAreaShapes.esp");
    private static readonly TestStatic Block = new(new FormKey(ShapeMod, 0x801), @"test\clearedarea\block.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20), new Vector3(20))));
    private static readonly TestStatic BigBlock = new(new FormKey(ShapeMod, 0x802), @"test\clearedarea\bigblock.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-100), new Vector3(100))));
    private static readonly TestStatic Mist = new(new FormKey(ShapeMod, 0x803), @"test\clearedarea\mist.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20), new Vector3(20))), EffectOnly: true);
    private static readonly IBaseObjectShapes Shapes = TestVisibility.Over(TestShapes.Create(ShapeMod, "ClearedAreasData", Block, BigBlock, Mist));

    private static readonly BaseKey XMarker = TestVisibility.TaggedBase(ObjectVisibility.Invisible(InvisibleObjectKind.XMarkers));

    /// <summary>The defaults of the method, with a radius that fits the small scenes.</summary>
    private static readonly ClearedArea AtTheDefaults = new(Radius, MaxStandingSizePercent: 1, MaxStandingCountPercent: 5);

    /// <summary>Limits under which equal pieces clear an area once 1 in 20 still stands.</summary>
    private static readonly ClearedArea FivePercentBoth = new(Radius, MaxStandingSizePercent: 5, MaxStandingCountPercent: 5);

    /// <summary>Limits under which one standing block among a few removed ones, one of them large, clears the area.</summary>
    private static readonly ClearedArea OneInFiveStanding = new(Radius, MaxStandingSizePercent: 1, MaxStandingCountPercent: 20);

    private static readonly RecordKey Interior = TestTargets.SpaceKey(0x200);
    private static readonly IReadOnlyDictionary<RecordKey, SpaceFact> InteriorSpace = new Dictionary<RecordKey, SpaceFact>
    {
        [Interior] = new SpaceFact(Interior, "AnInterior", SpaceKind.Interior, Name: null),
    };

    // ---- edges and spreading

    [Fact]
    public void TheStandingEdgeOfARowNextToAnEquallyDenseRemovedRowStays()
    {
        var targets = Row(count: 40, firstX: -2000);
        var decisions = DecisionsRemoving(targets, [.. Enumerable.Range(0, 20)]);

        var result = Find(targets, decisions, AtTheDefaults);

        Assert.Equal(20, result.Evaluations.Count);
        Assert.All(result.Evaluations, evaluation => Assert.Equal(LeftBehindOutcome.KeptAreaStillStands, evaluation.Decision));
        Assert.Empty(result.ProposedRounds);
    }

    [Fact]
    public void TheStandingEdgeOfAGridNextToAnEquallyDenseRemovedGridStays()
    {
        const int Side = 20;
        ImmutableArray<TargetObject> targets =
        [
            .. Enumerable.Range(0, Side * Side).Select(index => Own(index, new Vector3((index % Side - Side / 2) * Spacing, (index / Side) * Spacing, 0))),
        ];
        var decisions = DecisionsRemoving(targets, [.. Enumerable.Range(0, Side * Side).Where(index => index % Side < Side / 2)]);

        var result = Find(targets, decisions, AtTheDefaults);

        Assert.Equal(Side * Side / 2, result.Evaluations.Count);
        Assert.DoesNotContain(result.Evaluations, evaluation => evaluation.IsRemoved);
    }

    [Fact]
    public void EachObjectIsJudgedOnceOnTheEarlierDecisionsSoARemovalNeverClearsTheAreaOfAnother()
    {
        const int Edge = 0;
        const int Inner = 1;
        var removedLeft = Enumerable.Range(0, 30).Select(index => new Vector3(-970 + index * 30, 0, 0));
        var removedRight = Enumerable.Range(0, 40).Select(index => new Vector3(1500 + index * 10, 0, 0));
        ImmutableArray<TargetObject> targets =
        [
            Own(Edge, Vector3.Zero),
            Own(Inner, new Vector3(900, 0, 0)),
            .. removedLeft.Concat(removedRight).Select((at, index) => Own(index + 2, at)),
        ];
        var decisions = DecisionsRemoving(targets, [.. Enumerable.Range(2, targets.Length - 2)]);

        var result = Find(targets, decisions, FivePercentBoth);

        Assert.Equal(LeftBehindOutcome.RemovedInClearedArea, Evaluation(result, Inner).Decision);
        Assert.Equal((LeftBehindOutcome.KeptAreaStillStands, 2), (Evaluation(result, Edge).Decision, Evaluation(result, Edge).Area.Standing));
    }

    [Fact]
    public void AnInteriorIsOneAreaThatLosesItsRestWhen96PercentWereRemoved()
    {
        ImmutableArray<TargetObject> targets =
        [
            .. Enumerable.Range(0, 24).Select(index => Own(index, new Vector3(index * 3000, 0, 0), BigBlock, Interior)),
            Own(24, new Vector3(90000, 0, 0), Block, Interior),
        ];
        var decisions = DecisionsRemoving(targets, [.. Enumerable.Range(0, 24)]);

        var evaluation = Assert.Single(Find(targets, decisions, AtTheDefaults, InteriorSpace).Evaluations);

        Assert.Equal((25, 1, LeftBehindOutcome.RemovedInClearedArea), (evaluation.Area.Objects, evaluation.Area.Standing, evaluation.Decision));
    }

    [Fact]
    public void AnInteriorHalfOfWhichWasRemovedLosesNothing()
    {
        ImmutableArray<TargetObject> targets = [Own(0, Vector3.Zero, Block, Interior), Own(1, new Vector3(50000, 0, 0), Block, Interior)];

        var evaluation = Assert.Single(Find(targets, DecisionsRemoving(targets, [0]), AtTheDefaults, InteriorSpace).Evaluations);

        Assert.Equal(LeftBehindOutcome.KeptAreaStillStands, evaluation.Decision);
    }

    // ---- shares and candidates

    [Fact]
    public void AnAreaOfEffectOnlyObjectsIsJudgedByNumberAlone()
    {
        ImmutableArray<TargetObject> targets = [.. Enumerable.Range(0, 30).Select(index => Own(index, new Vector3(index * 10, 0, 0), Mist))];

        var evaluation = Assert.Single(Find(targets, DecisionsRemoving(targets, [.. Enumerable.Range(1, 29)]), AtTheDefaults).Evaluations);

        Assert.Equal((ClearedAreaObjectKind.EffectOnly, 30, 1, 0d), (evaluation.Kind, evaluation.Area.Objects, evaluation.Area.Standing, evaluation.Area.Size));
        Assert.Equal(LeftBehindOutcome.RemovedInClearedArea, evaluation.Decision);
    }

    [Fact]
    public void AnEffectOnlyObjectCountsByNumberAndAddsNoSize()
    {
        ImmutableArray<TargetObject> targets = [Own(0, Vector3.Zero), Own(1, new Vector3(10, 0, 0), Mist)];

        var evaluation = Evaluation(Find(targets, DecisionsRemoving(targets, [1]), AtTheDefaults), 0);

        Assert.Equal((2, 1), (evaluation.Area.Objects, evaluation.Area.Standing));
        Assert.Equal(evaluation.Area.StandingSize, evaluation.Area.Size);
    }

    [Fact]
    public void AnInvisibleObjectInAClearedAreaIsRemovedAndCountsInNoShare()
    {
        ImmutableArray<TargetObject> targets =
        [
            .. Enumerable.Range(0, 25).Select(index => Own(index, new Vector3(index * 10, 0, 0))),
            Invisible(25, new Vector3(100, 0, 0)),
        ];

        var evaluation = Assert.Single(Find(targets, DecisionsRemoving(targets, [.. Enumerable.Range(0, 25)]), AtTheDefaults).Evaluations);

        Assert.Equal((ClearedAreaObjectKind.Invisible, 25, 0), (evaluation.Kind, evaluation.Area.Objects, evaluation.Area.Standing));
        Assert.Equal(LeftBehindOutcome.RemovedInClearedArea, evaluation.Decision);
    }

    [Fact]
    public void AnInvisibleObjectWithNoDrawnObjectOfTheCleanedModAroundItIsKeptByTheMinimum()
    {
        ImmutableArray<TargetObject> targets = [Own(0, Vector3.Zero), Invisible(1, new Vector3(5000, 0, 0))];

        var evaluation = Evaluation(Find(targets, DecisionsRemoving(targets, [0]), AtTheDefaults), 1);

        Assert.Equal((0, AreaVerdict.NoObjectsAround, LeftBehindOutcome.KeptTooFewObjectsAround), (evaluation.ObjectsAround, evaluation.Area.Verdict, evaluation.Decision));
    }

    // ---- the minimum of objects around

    /// <summary>
    /// A large removed object and small removed ones around a standing block: both shares clear the area, so only their number keeps
    /// the block. The block counts in its own share but never towards the minimum.
    /// </summary>
    [Theory]
    [InlineData(4, nameof(LeftBehindOutcome.KeptTooFewObjectsAround))]
    [InlineData(5, nameof(LeftBehindOutcome.RemovedInClearedArea))]
    public void AStandingObjectIsRemovedOnlyFromTheMinimumOfObjectsAroundItOn(int around, string expected)
    {
        ImmutableArray<TargetObject> targets =
        [
            Own(0, Vector3.Zero),
            Own(1, new Vector3(300, 0, 0), BigBlock),
            .. Enumerable.Range(2, around - 1).Select(index => Own(index, new Vector3(index * 10, 0, 0))),
        ];

        var evaluation = Assert.Single(Find(targets, DecisionsRemoving(targets, [.. Enumerable.Range(1, around)]), OneInFiveStanding, minObjectsAround: 5).Evaluations);

        Assert.Equal((around, around + 1, AreaVerdict.Cleared), (evaluation.ObjectsAround, evaluation.Area.Objects, evaluation.Area.Verdict));
        Assert.Equal(expected, evaluation.Decision.ToString());
    }

    [Fact]
    public void AnObjectKeptShrunkOrTurnedIsJudgedAtItsFittedPlaceAndRemovedByTheRound()
    {
        var targets = Cluster(21);
        var fitted = new EulerPlacement(new Vector3(10, 0, 0), Vector3.Zero, 0.8f);
        var decisions = StartDecisions(targets)
            .ApplyFits([new ProposedFit(new TargetId(0), TooCloseReason, fitted)])
            .Apply(RoundKind.TooClose, [.. Enumerable.Range(1, 20).Select(TooClose)]);

        var found = FindDecided(targets, decisions, FivePercentBoth);

        var evaluation = Assert.Single(found.Result.Evaluations);
        Assert.Equal(LeftBehindOutcome.RemovedInClearedArea, evaluation.Decision);
        Assert.IsType<InClearedArea>(Assert.IsType<Decision.Removed>(found.Decisions.Of(new TargetId(0))).Reason);
    }

    // ---- never-rules

    [Fact]
    public void AVisibleObjectOfAKindAlwaysKeptIsKept()
    {
        var targets = Cluster(21);

        var evaluation = Assert.Single(Find(targets, DecisionsRemoving(targets, [.. Enumerable.Range(1, 20)]), FivePercentBoth, kinds: [new KeepsTargets(0)]).Evaluations);

        Assert.Equal(LeftBehindOutcome.KeptProtectedType, evaluation.Decision);
    }

    [Fact]
    public void AnInvisibleObjectOfAKindTheInvisibleStepAlwaysKeepsIsKeptAlsoWithThatStepOff()
    {
        ImmutableArray<TargetObject> targets = [.. Cluster(20), Invisible(20, Vector3.Zero)];
        var invisibleKinds = LeftBehindRules.CreateInvisibleKindsAlwaysKept(Shapes, new NeverRemovedInvisibleKinds(new HashSet<InvisibleObjectKind> { InvisibleObjectKind.XMarkers }));

        var evaluation = Assert.Single(Find(targets, DecisionsRemoving(targets, [.. Enumerable.Range(0, 20)]), AtTheDefaults, kinds: [invisibleKinds]).Evaluations);

        Assert.Equal(LeftBehindOutcome.KeptProtectedType, evaluation.Decision);
    }

    [Fact]
    public void AnObjectToKeepInAClearedAreaIsNotJudgedAndStaysUndecided()
    {
        var targets = Cluster(21);
        var protection = ObjectsToKeep.Build(targets, [], TestTargets.References(targets.Length, new Dictionary<int, KeepReason> { [0] = TestKeepReasons.Quest }), TestBaseFacts.Unresolved);
        var decisions = TestSeededDecisions.StartDisablingAll(protection, targets.Length).Apply(RoundKind.TooClose, [.. Enumerable.Range(1, 20).Select(TooClose)]);

        var found = FindDecided(targets, decisions, FivePercentBoth);

        Assert.Empty(found.Result.Evaluations);
        Assert.False(found.Decisions.IsDecided(new TargetId(0)));
    }

    [Fact]
    public void ARemovalTakesItsWholeLinkedGroup()
    {
        const int Partner = 21;
        ImmutableArray<TargetObject> targets = [.. Cluster(21), Own(Partner, new Vector3(50000, 0, 0))];
        var protection = ObjectsToKeep.Build(targets, [TestTargets.Link(0, Partner)], TestTargets.References(targets.Length), TestBaseFacts.Unresolved);
        var decisions = TestSeededDecisions.StartDisablingAll(protection, targets.Length).Apply(RoundKind.TooClose, [.. Enumerable.Range(1, 20).Select(TooClose)]);

        var found = FindDecided(targets, decisions, FivePercentBoth);

        Assert.Equal(LeftBehindOutcome.RemovedInClearedArea, Evaluation(found.Result, 0).Decision);
        Assert.Equal(LeftBehindOutcome.RemovedLinkedToRemoval, Evaluation(found.Result, Partner).Decision);
        Assert.True(found.Decisions.IsRemoved(new TargetId(Partner)));
    }

    [Fact]
    public void AMemberOfAGroupWithAnObjectToKeepIsAnObjectToKeepTooAndStays()
    {
        const int Kept = 21;
        ImmutableArray<TargetObject> targets = [.. Cluster(21), Own(Kept, new Vector3(50000, 0, 0))];
        var protection = ObjectsToKeep.Build(
            targets, [TestTargets.Link(0, Kept)], TestTargets.References(targets.Length, new Dictionary<int, KeepReason> { [Kept] = TestKeepReasons.Quest }), TestBaseFacts.Unresolved);
        var decisions = TestSeededDecisions.StartDisablingAll(protection, targets.Length).Apply(RoundKind.TooClose, [.. Enumerable.Range(1, 20).Select(TooClose)]);

        var found = FindDecided(targets, decisions, FivePercentBoth);

        Assert.Empty(found.Result.Evaluations);
        Assert.False(found.Decisions.HasLeftItsPlace(new TargetId(0)));
    }

    [Fact]
    public void AnObjectInADeletedCellIsAnObjectToKeepAndStays()
    {
        var cluster = Cluster(21);
        var targets = cluster.SetItem(0, cluster[0] with { CellDeleted = TestKeepReasons.DeletedByCityMod });

        var found = FindDecided(targets, DecisionsRemoving(targets, [.. Enumerable.Range(1, 20)]), FivePercentBoth);

        Assert.Empty(found.Result.Evaluations);
        Assert.False(found.Decisions.HasLeftItsPlace(new TargetId(0)));
    }

    [Fact]
    public void TheStepSwitchedOffJudgesAndProposesNothing()
    {
        var targets = Cluster(21);

        var result = ObjectsInClearedAreas.Find(InputOf(targets, DecisionsRemoving(targets, [.. Enumerable.Range(1, 20)]), settings: null)).Result;

        Assert.Equal((StepOutcome.SwitchedOff, 0, 0), (result.Outcome, result.Evaluations.Count, result.ProposedRounds.Length));
    }

    // ---- determinism

    [Fact]
    public void TheAreasAreJudgedTheSameWithOneAndWithManyWorkers()
    {
        var (targets, removed) = RandomScene(seed: 7);
        var decisions = DecisionsRemoving(targets, removed);
        var points = targets.Select(target => target.Transform.Position).ToList();

        AreaShare[] Judge(int workers)
        {
            var areas = TestClearedAreas.Of(targets, new Dictionary<RecordKey, SpaceFact>(), decisions, CachesOf(targets), AtTheDefaults);
            return ParallelMap.Run(new Execution(workers), points.Count, index => areas.Judge(TestTargets.Space, points[index]), rangeSize: 1);
        }

        Assert.Equal(Judge(1), Judge(Environment.ProcessorCount * 2));
    }

    [Fact]
    public void TheResultIsTheSameWithOneAndManyWorkersAndWithTheTargetsInAnotherOrder()
    {
        var (targets, removed) = RandomScene(seed: 11);
        var reversed = Reversed(targets);
        List<RecordKey> RemovedWith(ImmutableArray<TargetObject> inOrder, IReadOnlyList<int> removedIndexes, int workers) =>
            RemovedKeys(inOrder, FindDecided(inOrder, DecisionsRemoving(inOrder, removedIndexes), AtTheDefaults, workers: workers).Decisions);

        var one = RemovedWith(targets, removed, workers: 1);
        var many = RemovedWith(targets, removed, workers: Environment.ProcessorCount * 2);
        var otherOrder = RemovedWith(reversed, [.. removed.Select(index => targets.Length - 1 - index)], workers: Environment.ProcessorCount * 2);

        Assert.True(one.Count > removed.Count);
        Assert.Equal(one, many);
        Assert.Equal(one, otherOrder);
    }

    // ---- helpers

    private static ImmutableArray<TargetObject> Row(int count, float firstX) =>
        [.. Enumerable.Range(0, count).Select(index => Own(index, new Vector3(firstX + index * Spacing, 0, 0)))];

    /// <summary>Blocks side by side, all well within one another's area; the first is the one the tests judge.</summary>
    private static ImmutableArray<TargetObject> Cluster(int count) =>
        [.. Enumerable.Range(0, count).Select(index => Own(index, new Vector3(index * 10, 0, 0)))];

    private static TargetObject Own(int index, Vector3 position, TestStatic? model = null, RecordKey? space = null) =>
        TestTargets.Create(index, TestTargets.At(position), (model ?? Block).Base, space ?? TestTargets.Space);

    private static TargetObject Invisible(int index, Vector3 position) => TestTargets.Create(index, TestTargets.At(position), XMarker, TestTargets.Space);

    private static RemovalDecisions StartDecisions(ImmutableArray<TargetObject> targets) =>
        TestSeededDecisions.StartDisablingAll(ObjectsToKeep.Build(targets, [], TestTargets.References(targets.Length), TestBaseFacts.Unresolved), targets.Length);

    private static IRunDecisions DecisionsRemoving(ImmutableArray<TargetObject> targets, IReadOnlyList<int> removed) =>
        StartDecisions(targets).Apply(RoundKind.TooClose, [.. removed.Select(TooClose)]);

    /// <summary>600 blocks scattered over a square with almost all of them removed, some invisible objects among them.</summary>
    /// <returns>The targets and the indexes of the removed ones.</returns>
    private static (ImmutableArray<TargetObject> Targets, List<int> Removed) RandomScene(int seed)
    {
        var random = new Random(seed);
        ImmutableArray<TargetObject> targets =
        [
            .. Enumerable.Range(0, 600).Select(index =>
            {
                var at = new Vector3((random.NextSingle() * 2 - 1) * 3000, (random.NextSingle() * 2 - 1) * 3000, 0);
                return index % 10 == 0 ? Invisible(index, at) : Own(index, at, index % 7 == 0 ? BigBlock : Block);
            }),
        ];
        return (targets, [.. Enumerable.Range(0, targets.Length).Where(index => index % 10 != 0 && random.Next(30) > 0)]);
    }

    /// <summary>The same objects with the ids in the opposite order.</summary>
    private static ImmutableArray<TargetObject> Reversed(ImmutableArray<TargetObject> targets) =>
        [.. targets.Reverse().Select((target, index) => target with { Id = new TargetId(index) })];

    private static IEnumerable<int> RemovedIndexes(ImmutableArray<TargetObject> targets, IRemovalDecisions decisions) =>
        Enumerable.Range(0, targets.Length).Where(index => decisions.IsRemoved(new TargetId(index)));

    private static List<RecordKey> RemovedKeys(ImmutableArray<TargetObject> targets, IRemovalDecisions decisions) =>
        [.. RemovedIndexes(targets, decisions).Select(index => targets[index].Key).Order(RecordKeyOrder.Comparer)];

    private static ClearedAreaCheck Evaluation(ObjectsInClearedAreasResult result, int targetIndex) =>
        result.Evaluations.Single(evaluation => evaluation.TargetIndex == targetIndex);

    private static ObjectsInClearedAreasResult Find(
        ImmutableArray<TargetObject> targets,
        IRunDecisions decisions,
        ClearedArea area,
        IReadOnlyDictionary<RecordKey, SpaceFact>? spaces = null,
        IReadOnlyList<IKindsAlwaysKept>? kinds = null,
        int minObjectsAround = 1) =>
        FindDecided(targets, decisions, area, spaces, kinds, minObjectsAround: minObjectsAround).Result;

    /// <param name="minObjectsAround">By default one, so the shares alone decide an object with anything around it.</param>
    private static Decided<ObjectsInClearedAreasResult> FindDecided(
        ImmutableArray<TargetObject> targets,
        IRunDecisions decisions,
        ClearedArea area,
        IReadOnlyDictionary<RecordKey, SpaceFact>? spaces = null,
        IReadOnlyList<IKindsAlwaysKept>? kinds = null,
        int workers = 1,
        int minObjectsAround = 1) =>
        ObjectsInClearedAreas.Find(InputOf(targets, decisions, new ObjectsInClearedAreasSettings(area, minObjectsAround), spaces, kinds, workers));

    private static ObjectsInClearedAreas.Input InputOf(
        ImmutableArray<TargetObject> targets,
        IRunDecisions decisions,
        ObjectsInClearedAreasSettings? settings,
        IReadOnlyDictionary<RecordKey, SpaceFact>? spaces = null,
        IReadOnlyList<IKindsAlwaysKept>? kinds = null,
        int workers = 1)
    {
        var caches = CachesOf(targets);
        return new(
            targets,
            decisions,
            caches.Shapes,
            LeftBehindRules.CreateClearedAreasJudging(targets, overriddenLater: [], spaces ?? new Dictionary<RecordKey, SpaceFact>(), caches),
            kinds ?? [],
            LeftBehindRules.CreateLeftBehindRoundJoin(),
            new NoFollowUp(),
            () => TimeSpan.Zero,
            TargetWorkOrder.Of(targets),
            new Execution(workers),
            settings);
    }

    private static TestRunCaches CachesOf(ImmutableArray<TargetObject> targets) =>
        new(Shapes, shownTargets: TestScenes.Create(targets, [], Shapes).IndexShownTargets);
}
