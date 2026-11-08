using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;
using static AddedObjectRemover.Tests.Fixtures.TestOneRoundSteps;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// The step for whole groups: the cleaned mod's objects are grouped by the gap between their boxes, seen from above, and every group
/// that holds an object an earlier step took from its place, or lies within the gap of the old place of a changed object an earlier
/// step took away, is removed whole. Over in-memory target objects.
/// </summary>
public sealed class WholeGroupsTests
{
    private const float Gap = 300f;

    /// <summary>Half the side of a block, so blocks whose centres are 340 apart have boxes exactly 300 apart.</summary>
    private const float HalfBlock = 20f;

    private const float JoinedSpacing = 2 * HalfBlock + Gap;

    private static readonly ModKey ShapeMod = ModKey.FromNameAndExtension("WholeGroupShapes.esp");
    private static readonly TestStatic Block = new(new FormKey(ShapeMod, 0x801), @"test\wholegroups\block.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-HalfBlock), new Vector3(HalfBlock))));
    private static readonly TestStatic Mist = new(new FormKey(ShapeMod, 0x802), @"test\wholegroups\mist.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-HalfBlock), new Vector3(HalfBlock))), EffectOnly: true);
    private static readonly TestStatic HugeMist = new(new FormKey(ShapeMod, 0x803), @"test\wholegroups\hugemist.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-1000), new Vector3(1000))), EffectOnly: true);
    private static readonly IBaseObjectShapes Shapes = TestVisibility.Over(TestShapes.Create(ShapeMod, "WholeGroupsData", Block, Mist, HugeMist));
    private static readonly BaseKey XMarker = TestVisibility.TaggedBase(ObjectVisibility.Invisible(InvisibleObjectKind.XMarkers));
    private static readonly RecordKey OtherSpace = TestTargets.SpaceKey(0x200);

    // ---- grouping

    [Fact]
    public void ObjectsChainIntoOneGroupThroughTheirNeighbours()
    {
        ImmutableArray<TargetObject> members = [Own(0, X(0)), Own(1, X(JoinedSpacing)), Own(2, X(2 * JoinedSpacing)), Own(3, X(10000))];

        Assert.Equal(new[] { new[] { 0, 1, 2 }, new[] { 3 } }, Grouping(members).Groups);
    }

    [Fact]
    public void BoxesExactlyTheGapApartJoinAndBoxesFartherApartDoNot()
    {
        ImmutableArray<TargetObject> atTheGap = [Own(0, X(0)), Own(1, X(JoinedSpacing))];
        ImmutableArray<TargetObject> beyondIt = [Own(0, X(0)), Own(1, X(JoinedSpacing + 1))];

        Assert.Single(Grouping(atTheGap).Groups);
        Assert.Equal(2, Grouping(beyondIt).Groups.Count);
    }

    [Fact]
    public void TheGapIsMeasuredFromAboveSoHeightDoesNotSeparate()
    {
        ImmutableArray<TargetObject> members = [Own(0, X(0)), Own(1, new Vector3(JoinedSpacing, 0, 5000))];

        Assert.Single(Grouping(members).Groups);
    }

    [Fact]
    public void EffectOnlyAndInvisibleObjectsJoinTheNearestDrawnBoxWithinTheGapOfTheirPosition()
    {
        ImmutableArray<TargetObject> members =
        [
            Own(0, X(0)),
            Own(1, X(HalfBlock + Gap), Mist.Base),
            Marker(2, X(-HalfBlock - Gap)),
            Own(3, X(1000)),
            Marker(4, X(1000 - HalfBlock - Gap + 10)),
        ];

        Assert.Equal(new[] { new[] { 0, 1, 2 }, new[] { 3, 4 } }, Grouping(members).Groups);
    }

    [Fact]
    public void AnEffectOnlyOrInvisibleObjectJoinsTheNearerOfTwoDrawnBoxes()
    {
        ImmutableArray<TargetObject> members = [Own(0, X(0)), Own(1, X(500)), Marker(2, X(300)), Own(3, X(10000)), Own(4, X(10500)), Own(5, X(10200), Mist.Base)];

        Assert.Equal(new[] { new[] { 0 }, new[] { 1, 2 }, new[] { 3, 5 }, new[] { 4 } }, Grouping(members).Groups);
    }

    [Fact]
    public void AnEffectOnlyOrInvisibleObjectEquallyNearTwoDrawnBoxesJoinsTheLowerMember()
    {
        ImmutableArray<TargetObject> members = [Own(0, X(0)), Own(1, X(600)), Marker(2, X(300))];

        Assert.Equal(new[] { new[] { 0, 2 }, new[] { 1 } }, Grouping(members).Groups);
    }

    [Fact]
    public void AnEffectOnlyOrInvisibleObjectChainsNothingSoTwoOfThemNeverJoinEachOther()
    {
        ImmutableArray<TargetObject> members = [Own(0, X(0)), Marker(1, X(HalfBlock + Gap)), Marker(2, X(HalfBlock + 2 * Gap)), Own(3, X(2 * HalfBlock + 3 * Gap + HalfBlock))];

        Assert.Equal(new[] { new[] { 0, 1 }, new[] { 2 }, new[] { 3 } }, Grouping(members).Groups);
    }

    [Fact]
    public void AnInvisibleObjectWithNoDrawnBoxWithinTheGapIsAGroupOfItsOwn()
    {
        ImmutableArray<TargetObject> members = [Own(0, X(0)), Marker(1, X(HalfBlock + Gap + 1))];

        Assert.Equal(new[] { new[] { 0 }, new[] { 1 } }, Grouping(members).Groups);
    }

    [Fact]
    public void AnEffectOnlyObjectWithAHugeBoxDoesNotBridgeTwoGroups()
    {
        ImmutableArray<TargetObject> members = [Own(0, X(0)), Own(1, X(300), HugeMist.Base), Own(2, X(1500))];

        Assert.Equal(new[] { new[] { 0, 1 }, new[] { 2 } }, Grouping(members).Groups);
    }

    [Fact]
    public void ObjectsOfDifferentSpacesNeverJoin()
    {
        ImmutableArray<TargetObject> members = [Own(0, X(0)), Own(1, X(0), space: OtherSpace)];

        Assert.Equal(2, Grouping(members).Groups.Count);
    }

    [Fact]
    public void TheGroupsDoNotDependOnTheOrderOfTheObjectsOrTheNumberOfWorkers()
    {
        var members = RandomScene(seed: 7);
        var expected = GroupsByKey(members, Grouping(members, workers: 1));

        var shuffled = members.Reverse().ToImmutableArray();
        Assert.Equal(expected, GroupsByKey(shuffled, Grouping(shuffled, workers: 1)));
        Assert.Equal(expected, GroupsByKey(members, Grouping(members, workers: 8)));
    }

    // ---- seeding

    [Fact]
    public void AGroupWithAClashRemovalIsRemovedWholeAndNamedByItsSeedAndSize()
    {
        ImmutableArray<TargetObject> targets = [Own(0, X(0)), Own(1, X(JoinedSpacing)), Own(2, X(2 * JoinedSpacing)), Own(3, X(10000))];

        var result = Find(targets, DecisionsRemoving(targets, [1]));

        var group = Assert.Single(result.Result.Groups);
        Assert.Equal((new TargetId(1), 3), (group.Seed, group.Members.Length));
        Assert.Equal([0, 2], result.Result.Evaluations.Select(check => check.TargetIndex));
        Assert.All(result.Result.Evaluations, check => Assert.Equal(LeftBehindOutcome.RemovedInGroupWithClash, check.Decision));
        Assert.Equal(new InGroupWithClash(new TargetId(1), 3), result.Decisions.Of(new TargetId(0))!.Reason);
        Assert.Equal(RoundKind.WholeGroup, result.Decisions.Of(new TargetId(2))!.Round.Kind);
    }

    [Fact]
    public void AGroupWithoutASeedStays()
    {
        ImmutableArray<TargetObject> targets = [Own(0, X(0)), Own(1, X(10000)), Own(2, X(10000 + JoinedSpacing))];

        var result = Find(targets, DecisionsRemoving(targets, [0]));

        Assert.Single(result.Result.Groups);
        Assert.Empty(result.Result.Evaluations);
        Assert.False(result.Decisions.IsDecided(new TargetId(1)));
        Assert.False(result.Decisions.IsDecided(new TargetId(2)));
    }

    [Fact]
    public void ARestoredChangedObjectSeedsTheGroupAtItsOldPlaceNotWhereTheCleanedModPutIt()
    {
        var oldPlace = X(10000 + JoinedSpacing);
        var moved = Own(4, X(-JoinedSpacing)) with
        {
            Origin = TargetOrigin.Moved,
            EarlierVersion = TestTargets.EarlierVersionAt(new PluginName("Skyrim.esm"), new EulerPlacement(oldPlace, Vector3.Zero, 1f), Block.Base),
        };
        ImmutableArray<TargetObject> targets = [Own(0, X(0)), Own(1, X(JoinedSpacing)), Own(2, X(10000)), Own(3, X(10000 - JoinedSpacing)), moved];
        var decisions = StartDecisions(targets, restored: 4).Apply(RoundKind.TooClose, [TooClose(4)]);

        var result = Find(targets, decisions);

        Assert.IsType<Decision.Restored>(decisions.Of(new TargetId(4)));
        var group = Assert.Single(result.Result.Groups);
        Assert.Equal(new TargetId(4), group.Seed);
        Assert.Equal(new[] { new TargetId(2), new TargetId(3) }, group.Members.ToArray());
        Assert.Equal([2, 3], result.Result.Evaluations.Select(check => check.TargetIndex));
        Assert.False(result.Decisions.IsDecided(new TargetId(0)));
    }

    [Fact]
    public void ADisabledChangedObjectWhosePositionCannotGoBackSeedsAtItsOriginalPlace()
    {
        var oldPlace = X(10000 + JoinedSpacing);
        var earlier = TestTargets.EarlierVersionAt(new PluginName("Skyrim.esm"), new EulerPlacement(oldPlace, Vector3.Zero, 1f), Block.Base) with { Undo = PlacedParts.None };
        var moved = Own(4, X(-JoinedSpacing)) with { Origin = TargetOrigin.Moved, EarlierVersion = earlier };
        ImmutableArray<TargetObject> targets = [Own(0, X(0)), Own(1, X(JoinedSpacing)), Own(2, X(10000)), Own(3, X(10000 - JoinedSpacing)), moved];
        var decisions = DecisionsRemoving(targets, [4]);

        var result = Find(targets, decisions);

        Assert.IsType<Decision.Removed>(decisions.Of(new TargetId(4)));
        Assert.Equal([2, 3], result.Result.Evaluations.Select(check => check.TargetIndex));
        Assert.False(result.Decisions.IsDecided(new TargetId(0)));
    }

    [Fact]
    public void AGroupWithTwoSeedsIsNamedByTheLowestOne()
    {
        ImmutableArray<TargetObject> targets = [Own(0, X(0)), Own(1, X(JoinedSpacing)), Own(2, X(2 * JoinedSpacing)), Own(3, X(3 * JoinedSpacing))];

        var result = Find(targets, DecisionsRemoving(targets, [2, 1]));

        var group = Assert.Single(result.Result.Groups);
        Assert.Equal((new TargetId(1), 4), (group.Seed, group.Members.Length));
        Assert.Equal([0, 3], result.Result.Evaluations.Select(check => check.TargetIndex));
    }

    [Fact]
    public void AFittedMemberJoinsAtItsNewPlaceAndTheRoundReplacesItsFit()
    {
        ImmutableArray<TargetObject> targets = [Own(0, X(0)), Own(1, X(JoinedSpacing)), Own(2, X(10000))];
        var decisions = TestSeededDecisions.StartUnprotected(targets)
            .ApplyFits([new ProposedFit(new TargetId(2), TooCloseReason, new EulerPlacement(X(2 * JoinedSpacing), Vector3.Zero, 1f))])
            .Apply(RoundKind.TooClose, [TooClose(0)]);

        var result = Find(targets, decisions);

        Assert.Equal([1, 2], result.Result.Evaluations.Select(check => check.TargetIndex));
        Assert.Equal(RoundKind.WholeGroup, result.Decisions.Of(new TargetId(2))!.Round.Kind);
        Assert.True(result.Decisions.IsRemoved(new TargetId(2)));
    }

    [Fact]
    public void ADrawnPrimitiveJoinsByItsOwnBounds()
    {
        const float PrimitiveSide = 600f;
        var primitive = Own(0, X(1000)) with { IsPrimitive = true, PrimitiveBounds = new Vector3(PrimitiveSide) };
        var block = Own(1, X(1000 + PrimitiveSide / 2 + Gap + HalfBlock));

        Assert.Single(Grouping([primitive, block]).Groups);
        Assert.Equal(2, Grouping([primitive with { IsPrimitive = false, PrimitiveBounds = null }, block]).Groups.Count);
    }

    [Fact]
    public void TheRoundIsFollowedUpFromWhatItTookAndTheFollowUpRoundsComeAfterIt()
    {
        ImmutableArray<TargetObject> targets = [Own(0, X(0)), Own(1, X(JoinedSpacing)), Own(2, X(2 * JoinedSpacing)), Own(3, X(10000))];
        var followUp = new RemovesTargetThree();

        var result = WholeGroups.Find(new(targets, DecisionsRemoving(targets, [0]), Shapes, [], LeftBehindRules.CreateLeftBehindRoundJoin(), followUp, new Execution(1), new WholeGroupsSettings(Gap)));

        Assert.Equal(new[] { new[] { 1, 2 } }, followUp.Seeds);
        Assert.Equal([RoundKind.WholeGroup, RoundKind.AlsoRemove], result.Result.ProposedRounds.Select(round => round.Opening.Kind));
        Assert.Equal(RoundKind.AlsoRemove, result.Decisions.Of(new TargetId(3))!.Round.Kind);
    }

    // ---- kept objects

    [Fact]
    public void AKindAlwaysKeptStaysKeptAndAnObjectToKeepIsNoCandidate()
    {
        ImmutableArray<TargetObject> targets = [Own(0, X(0)), Own(1, X(JoinedSpacing)), Own(2, X(2 * JoinedSpacing)), Own(3, X(3 * JoinedSpacing))];
        var protection = ObjectsToKeep.Build(targets, [], TestTargets.References(targets.Length, new Dictionary<int, KeepReason> { [3] = TestKeepReasons.Quest }), TestBaseFacts.Unresolved);
        var decisions = TestSeededDecisions.StartDisablingAll(protection, targets.Length).Apply(RoundKind.TooClose, [TooClose(0)]);

        var result = Find(targets, decisions, kinds: [new KeepsTargets(2)]);

        Assert.Equal(
            [(1, LeftBehindOutcome.RemovedInGroupWithClash), (2, LeftBehindOutcome.KeptProtectedType)],
            result.Result.Evaluations.Select(check => (check.TargetIndex, check.Decision)));
        Assert.False(result.Decisions.IsDecided(new TargetId(2)));
        Assert.False(result.Decisions.IsDecided(new TargetId(3)));
    }

    [Fact]
    public void AKindAlwaysKeptGoesWhenItIsLinkedToARemovedMember()
    {
        ImmutableArray<TargetObject> targets = [Own(0, X(0)), Own(1, X(JoinedSpacing)), Own(2, X(2 * JoinedSpacing))];
        var protection = ObjectsToKeep.Build(targets, [TestTargets.Link(1, 2)], TestTargets.References(targets.Length), TestBaseFacts.Unresolved);
        var decisions = TestSeededDecisions.StartDisablingAll(protection, targets.Length).Apply(RoundKind.TooClose, [TooClose(0)]);

        var result = Find(targets, decisions, kinds: [new KeepsTargets(2)]);

        Assert.Equal(
            [(1, LeftBehindOutcome.RemovedInGroupWithClash), (2, LeftBehindOutcome.RemovedLinkedToRemoval)],
            result.Result.Evaluations.Select(check => (check.TargetIndex, check.Decision)));
        Assert.True(result.Decisions.IsRemoved(new TargetId(2)));
    }

    [Fact]
    public void AMemberKeptAsReferencedInAnEarlierRoundStaysKept()
    {
        ImmutableArray<TargetObject> targets = [Own(0, X(0)), Own(1, X(JoinedSpacing)), Own(2, X(2 * JoinedSpacing))];
        var protection = ObjectsToKeep.Build(targets, [], TestTargets.References(targets.Length, new Dictionary<int, KeepReason> { [1] = TestKeepReasons.Quest }), TestBaseFacts.Unresolved);
        var decisions = TestSeededDecisions.StartDisablingAll(protection, targets.Length).Apply(RoundKind.TooClose, [TooClose(0), TooClose(1)]);

        var result = Find(targets, decisions);

        Assert.IsType<Decision.Kept>(decisions.Of(new TargetId(1)));
        Assert.Equal([2], result.Result.Evaluations.Select(check => check.TargetIndex));
        Assert.Equal(decisions.Of(new TargetId(1)), result.Decisions.Of(new TargetId(1)));
    }

    [Fact]
    public void ASeedInOneSpaceNeverRemovesObjectsOfAnotherSpaceAtTheSamePlace()
    {
        ImmutableArray<TargetObject> targets = [Own(0, X(0)), Own(1, X(JoinedSpacing)), Own(2, X(0), space: OtherSpace), Own(3, X(JoinedSpacing), space: OtherSpace)];

        var result = Find(targets, DecisionsRemoving(targets, [0]));

        Assert.Equal([1], result.Result.Evaluations.Select(check => check.TargetIndex));
        Assert.False(result.Decisions.IsDecided(new TargetId(2)));
        Assert.False(result.Decisions.IsDecided(new TargetId(3)));
    }

    [Fact]
    public void AChangedObjectWhoseOriginalPlaceLiesBetweenTwoGroupsSeedsBoth()
    {
        var earlier = TestTargets.EarlierVersionAt(new PluginName("Skyrim.esm"), new EulerPlacement(X(10000), Vector3.Zero, 1f), Block.Base);
        var moved = Own(2, X(-10000)) with { Origin = TargetOrigin.Moved, EarlierVersion = earlier };
        ImmutableArray<TargetObject> targets = [Own(0, X(10000 - JoinedSpacing)), Own(1, X(10000 + JoinedSpacing)), moved];

        var result = Find(targets, DecisionsRemoving(targets, [2]));

        Assert.Equal([new TargetId(2), new TargetId(2)], result.Result.Groups.Select(group => group.Seed));
        Assert.Equal([0, 1], result.Result.Evaluations.Select(check => check.TargetIndex));
    }

    [Fact]
    public void AnInvisibleClashRemovalSeedsTheGroupItJoinedByItsPosition()
    {
        ImmutableArray<TargetObject> targets = [Own(0, X(0)), Own(1, X(JoinedSpacing)), Marker(2, X(-HalfBlock - Gap)), Own(3, X(10000))];

        var result = Find(targets, DecisionsRemoving(targets, [2]));

        var group = Assert.Single(result.Result.Groups);
        Assert.Equal((new TargetId(2), 3), (group.Seed, group.Members.Length));
        Assert.Equal([0, 1], result.Result.Evaluations.Select(check => check.TargetIndex));
    }

    [Fact]
    public void AChangedObjectsOriginalPlaceSeedsNoGroupThroughAnEffectOnlyObjectAlone()
    {
        var earlier = TestTargets.EarlierVersionAt(new PluginName("Skyrim.esm"), new EulerPlacement(X(10000), Vector3.Zero, 1f), Block.Base);
        var moved = Own(1, X(-10000)) with { Origin = TargetOrigin.Moved, EarlierVersion = earlier };
        ImmutableArray<TargetObject> targets = [Own(0, X(10000 + 200), HugeMist.Base), moved];

        var result = Find(targets, DecisionsRemoving(targets, [1]));

        Assert.Empty(result.Result.Groups);
        Assert.False(result.Decisions.IsDecided(new TargetId(0)));
    }

    [Theory]
    [InlineData(JoinedSpacing, true)]
    [InlineData(JoinedSpacing + 1, false)]
    public void AChangedObjectsOriginalBoxSeedsAGroupExactlyAtTheGapButNotPastIt(float originalX, bool seeds)
    {
        var earlier = TestTargets.EarlierVersionAt(new PluginName("Skyrim.esm"), new EulerPlacement(X(originalX), Vector3.Zero, 1f), Block.Base);
        var moved = Own(1, X(-10000)) with { Origin = TargetOrigin.Moved, EarlierVersion = earlier };
        ImmutableArray<TargetObject> targets = [Own(0, X(0)), moved];

        var result = Find(targets, DecisionsRemoving(targets, [1]));

        Assert.Equal(seeds, result.Decisions.IsRemoved(new TargetId(0)));
    }

    [Fact]
    public void AFollowUpRemovalSeedsItsGroup()
    {
        ImmutableArray<TargetObject> targets = [Own(0, X(-10000)), Own(1, X(0)), Own(2, X(JoinedSpacing))];
        var decisions = DecisionsRemoving(targets, [0])
            .Apply(RoundKind.AlsoRemove, [new ProposedRemoval(new TargetId(1), new TouchingTargetThatLeft(new TargetId(0)))]);

        var result = Find(targets, decisions);

        Assert.Equal(new TargetId(1), Assert.Single(result.Result.Groups, group => group.Members.Contains(new TargetId(2))).Seed);
        Assert.Equal([2], result.Result.Evaluations.Select(check => check.TargetIndex));
    }

    [Fact]
    public void ALinkedMemberTakenAlongSeedsItsGroup()
    {
        ImmutableArray<TargetObject> targets = [Own(0, X(-10000)), Own(1, X(0)), Own(2, X(JoinedSpacing))];
        var protection = ObjectsToKeep.Build(targets, [TestTargets.Link(0, 1)], TestTargets.References(targets.Length), TestBaseFacts.Unresolved);
        var decisions = TestSeededDecisions.StartDisablingAll(protection, targets.Length).Apply(RoundKind.TooClose, [TooClose(0)]);

        var result = Find(targets, decisions);

        Assert.IsType<LinkedTo>(decisions.Of(new TargetId(1))!.Reason);
        Assert.Equal(new TargetId(1), Assert.Single(result.Result.Groups, group => group.Members.Contains(new TargetId(2))).Seed);
        Assert.Equal([2], result.Result.Evaluations.Select(check => check.TargetIndex));
    }

    [Fact]
    public void AFailureOfTheUsersDataStopsTheStepWithoutARound()
    {
        ImmutableArray<TargetObject> targets = [Own(0, X(0)), Own(1, X(JoinedSpacing))];
        var decisions = DecisionsRemoving(targets, [0]);

        var result = WholeGroups.Find(InputOf(targets, decisions, new WholeGroupsSettings(Gap)) with { Shapes = new UnreadableShapes() });

        Assert.Equal(StepOutcome.StoppedByUserDataFailure, result.Result.Outcome);
        Assert.Equal(WholeGroupsParts.WholeGroups, result.Result.Failure!.Part);
        Assert.Empty(result.Result.ProposedRounds);
        Assert.Equal(decisions.Rounds.ToArray(), result.Decisions.Rounds.ToArray());
    }

    // ---- the step as a whole

    [Fact]
    public void SwitchedOffTheStepAddsNoRound()
    {
        ImmutableArray<TargetObject> targets = [Own(0, X(0)), Own(1, X(JoinedSpacing))];
        var decisions = DecisionsRemoving(targets, [0]);

        var result = WholeGroups.Find(InputOf(targets, decisions, settings: null));

        Assert.Equal(StepOutcome.SwitchedOff, result.Result.Outcome);
        Assert.Empty(result.Result.ProposedRounds);
        Assert.Equal(decisions.Rounds.ToArray(), result.Decisions.Rounds.ToArray());
    }

    [Fact]
    public void TheDecisionsDoNotDependOnTheNumberOfWorkers()
    {
        var targets = RandomScene(seed: 11);
        int[] removed = [3, 40, 170];

        var one = Find(targets, DecisionsRemoving(targets, removed), workers: 1);
        var many = Find(targets, DecisionsRemoving(targets, removed), workers: 8);

        Assert.Equal(one.Result.Evaluations, many.Result.Evaluations);
        Assert.Equal(one.Decisions.All().ToList(), many.Decisions.All().ToList());
    }

    private static Vector3 X(float x) => new(x, 0, 0);

    private static TargetObject Own(int index, Vector3 position, BaseKey? model = null, RecordKey? space = null) =>
        TestTargets.Create(index, TestTargets.At(position), model ?? Block.Base, space ?? TestTargets.Space);

    private static TargetObject Marker(int index, Vector3 position) => TestTargets.Create(index, TestTargets.At(position), XMarker, TestTargets.Space);

    /// <summary>200 blocks, effect-only and invisible objects scattered over a square, close enough that many chain.</summary>
    private static ImmutableArray<TargetObject> RandomScene(int seed)
    {
        var random = new Random(seed);
        return
        [
            .. Enumerable.Range(0, 200).Select(index =>
            {
                var at = new Vector3((random.NextSingle() * 2 - 1) * 6000, (random.NextSingle() * 2 - 1) * 6000, random.NextSingle() * 500);
                return index % 10 == 0 ? TestTargets.Create(index, TestTargets.At(at), XMarker, TestTargets.Space) : Own(index, at, index % 7 == 0 ? Mist.Base : Block.Base);
            }),
        ];
    }

    private static ObjectGrouping Grouping(ImmutableArray<TargetObject> members, int workers = 1) => ObjectGrouping.Of(members, Shapes, Gap, new Execution(workers));

    /// <summary>Each group as the sorted keys of its members, the groups sorted by their first key.</summary>
    private static List<string> GroupsByKey(ImmutableArray<TargetObject> members, ObjectGrouping grouping) =>
        [.. grouping.Groups.Select(group => string.Join(",", group.Select(position => members[position].Key.ToString()).Order(StringComparer.Ordinal))).Order(StringComparer.Ordinal)];

    private static RemovalDecisions StartDecisions(ImmutableArray<TargetObject> targets, int restored) =>
        RemovalDecisions.Start(
            ObjectsToKeep.Build(targets, [], TestTargets.References(targets.Length), TestBaseFacts.Unresolved),
            [.. targets.Select(target => target.Id.Index == restored ? new WhenRemoved.Restore(target.EarlierVersion!) : (WhenRemoved)new WhenRemoved.Disable())]);

    private static IRunDecisions DecisionsRemoving(ImmutableArray<TargetObject> targets, IReadOnlyList<int> removed) =>
        TestSeededDecisions.StartUnprotected(targets).Apply(RoundKind.TooClose, [.. removed.Select(TooClose)]);

    private static Decided<WholeGroupsResult> Find(ImmutableArray<TargetObject> targets, IRunDecisions decisions, IReadOnlyList<IKindsAlwaysKept>? kinds = null, int workers = 1) =>
        WholeGroups.Find(InputOf(targets, decisions, new WholeGroupsSettings(Gap), kinds, workers));

    private static WholeGroups.Input InputOf(
        ImmutableArray<TargetObject> targets, IRunDecisions decisions, WholeGroupsSettings? settings, IReadOnlyList<IKindsAlwaysKept>? kinds = null, int workers = 1) =>
        new(targets, decisions, Shapes, kinds ?? [], LeftBehindRules.CreateLeftBehindRoundJoin(), new NoFollowUp(), new Execution(workers), settings);

    /// <summary>A follow-up that, on its first pass, removes target 3 as resting on the first seed, and records the seeds of each pass.</summary>
    private sealed class RemovesTargetThree : IAlsoRemoveFollowUp
    {
        public List<int[]> Seeds { get; } = [];

        public FollowUpPass FollowUp(IRemovalDecisions decided, ImmutableArray<TargetId> seeds)
        {
            Seeds.Add([.. seeds.Select(seed => seed.Index)]);
            return Seeds.Count == 1 ? new FollowUpPass([[new ProposedRemoval(new TargetId(3), new TouchingTargetThatLeft(seeds[0]))]], Failure: null) : new FollowUpPass([], Failure: null);
        }

        public RestingObjectsResult Summarize(IRemovalDecisions final) => TestRestingObjects.SwitchedOff;
    }

    /// <summary>Shapes whose mesh files cannot be read, as a failure of the user's data.</summary>
    private sealed class UnreadableShapes : IBaseObjectShapes
    {
        public BaseShape Of(BaseKey? baseKey) => throw new IOException("The mesh file cannot be read.");

        public ObjectVisibility VisibilityOf(BaseKey? baseKey, bool isPrimitive, bool hasMapMarker) => Shapes.VisibilityOf(baseKey, isPrimitive, hasMapMarker);

        public bool LayerBlocksActors(uint collisionLayer) => Shapes.LayerBlocksActors(collisionLayer);

        public IReadOnlyList<MeasuredBase> Computed() => [];

        public BoundsStats CountBounds() => throw new NotSupportedException();
    }
}
