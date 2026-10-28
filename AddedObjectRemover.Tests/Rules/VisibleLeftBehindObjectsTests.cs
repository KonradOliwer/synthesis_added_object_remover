using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// The visible left-behind step: a candidate stands at the origin with other visible objects of the cleaned mod around it,
/// one per direction, and is removed when enough of those directions were cleared by earlier steps.
/// </summary>
public class VisibleLeftBehindObjectsTests
{
    private const float RingDistance = 200f;

    private static readonly ModKey OtherMod = ModKey.FromNameAndExtension("Other.esp");
    private static readonly IReadOnlyList<MeshTriangle> SmallBlock = TestMeshes.BoxTriangles(new Box(new Vector3(-20), new Vector3(20)));
    private static readonly TestStatic Block = new(new FormKey(OtherMod, 0x801), @"test\block.nif", SmallBlock);
    private static readonly TestStatic DoorBlock = new(new FormKey(OtherMod, 0x802), @"test\doorblock.nif", SmallBlock);

    private static readonly IBaseObjectShapes Shapes = TestVisibility.Over(TestShapes.Create(OtherMod, "VisibleLeftBehindObjectsData", Block, DoorBlock));

    private static readonly VisibleLeftBehindSettings Settings = new(
        LookAround: 260,
        MinOccupiedDirections: 6,
        DirectionClearedPercent: 80,
        ClearedDirectionsPercent: 80,
        NeverRemove: new HashSet<VisibleObjectKind>());

    private static readonly DirectionSector[] SixDirections =
        [DirectionSector.East, DirectionSector.NorthEast, DirectionSector.North, DirectionSector.NorthWest, DirectionSector.West, DirectionSector.SouthWest];

    private static readonly IBaseFacts UnresolvedBases = TestBaseFacts.Unresolved;

    [Fact]
    public void SixClearedDirectionsRemoveTheObject()
    {
        var targets = Ring(SixDirections);

        var result = Find(targets, DecisionsRemoving(targets, 1, 2, 3, 4, 5, 6), Settings);

        var evaluation = Assert.Single(result.Evaluations);
        Assert.Equal(0, evaluation.TargetIndex);
        Assert.Equal(VisibleLeftBehindOutcome.RemovedSurroundingsRemoved, evaluation.Decision);
        Assert.Equal(6, evaluation.Surroundings.OccupiedCount);
        Assert.Equal(6, evaluation.Surroundings.RemovedCount);
        var proposal = Assert.Single(result.Proposals);
        Assert.Equal(new TargetId(0), proposal.Target);
        Assert.IsType<RemovalReason.VisibleObjectSurroundingsRemoved>(proposal.Reason);
    }

    [Fact]
    public void FiveOccupiedDirectionsKeepTheObjectEvenWhenAllOfThemWereCleared()
    {
        var targets = Ring(SixDirections.Take(5));

        var result = Find(targets, DecisionsRemoving(targets, 1, 2, 3, 4, 5), Settings);

        var evaluation = Assert.Single(result.Evaluations);
        Assert.Equal(VisibleLeftBehindOutcome.KeptTooFewSurroundingObjects, evaluation.Decision);
        Assert.Empty(result.Proposals);
    }

    [Fact]
    public void ObjectsWhoseSurroundingsAreMostlyKeptAreKeptWithTheirClearedDirectionsCounted()
    {
        var targets = Ring(SixDirections);

        var result = Find(targets, DecisionsRemoving(targets, 1, 2, 3, 4), Settings);

        var evaluation = result.Evaluations.Single(check => check.TargetIndex == 0);
        Assert.Equal(VisibleLeftBehindOutcome.KeptSurroundingsMostlyKept, evaluation.Decision);
        Assert.Equal(6, evaluation.Surroundings.OccupiedCount);
        Assert.Equal(4, evaluation.Surroundings.RemovedCount);
        Assert.DoesNotContain(result.Proposals, proposal => proposal.Target.Index == 0);
    }

    [Fact]
    public void RemovalsOfTheFirstAlsoRemovePassCountAsClearedLikeTheOtherRemovals()
    {
        var targets = Ring(SixDirections);
        var decisions = DecisionsRemoving(targets, 1, 2, 3)
            .Apply(RoundKind.AlsoRemove, [.. new[] { 4, 5, 6 }.Select(index => new ProposedRemoval(new TargetId(index), new RemovalReason.Touching(new TargetId(1))))]);

        var evaluation = Assert.Single(Find(targets, decisions, Settings).Evaluations);

        Assert.Equal(6, evaluation.Surroundings.RemovedCount);
        Assert.Equal(VisibleLeftBehindOutcome.RemovedSurroundingsRemoved, evaluation.Decision);
    }

    [Fact]
    public void ADirectionIsClearedOnlyWhenEnoughOfItsGroundAreaWasRemoved()
    {
        var targets = Ring(SixDirections).Add(Visible(7, Offset(DirectionSector.East, RingDistance + 30)));

        var result = Find(targets, DecisionsRemoving(targets, 1, 2, 3, 4, 5, 6), Settings);

        var evaluation = result.Evaluations.Single(check => check.TargetIndex == 0);
        Assert.Equal(6, evaluation.Surroundings.OccupiedCount);
        Assert.Equal(5, evaluation.Surroundings.RemovedCount);
        Assert.Equal(SectorState.Kept, evaluation.Surroundings.State(DirectionSector.East));
        Assert.Equal(VisibleLeftBehindOutcome.RemovedSurroundingsRemoved, evaluation.Decision);
    }

    [Fact]
    public void ObjectsAnEarlierStepDecidedAreNotEvaluated()
    {
        var targets = Ring(SixDirections);

        var result = Find(targets, DecisionsRemoving(targets, 0, 1, 2, 3, 4, 5, 6), Settings);

        Assert.Empty(result.Evaluations);
        Assert.Empty(result.Proposals);
    }

    [Fact]
    public void AnObjectAnEarlierStepKeptIsNotEvaluatedAgain()
    {
        var targets = Ring(SixDirections);
        var protection = ObjectsToKeep.Build(targets, [], References(targets.Length, [0]), UnresolvedBases);
        var decisions = RemovalDecisions.Start(protection, targets.Length)
            .Apply(RoundKind.TooClose, [.. new[] { 0, 1, 2, 3, 4, 5, 6 }.Select(TooClose)]);
        Assert.IsType<Decision.Kept>(decisions.Of(new TargetId(0)));

        var result = Find(targets, decisions, Settings);

        Assert.Empty(result.Evaluations);
    }

    [Fact]
    public void ARemovalOfAReferencedObjectIsKeptAsReferencedWithTheKeepReason()
    {
        var targets = Ring(SixDirections);
        var protection = ObjectsToKeep.Build(targets, [], References(targets.Length, [0]), UnresolvedBases);
        var decisions = RemovalDecisions.Start(protection, targets.Length).Apply(RoundKind.TooClose, [.. new[] { 1, 2, 3, 4, 5, 6 }.Select(TooClose)]);

        var result = Find(targets, decisions, Settings);

        var evaluation = Assert.Single(result.Evaluations);
        Assert.Equal(VisibleLeftBehindOutcome.KeptReferenced, evaluation.Decision);
        Assert.Equal(TestKeepReasons.Quest.Kind, evaluation.KeepReason?.Kind);
        Assert.False(evaluation.IsRemoved);
        Assert.Equal(new TargetId(0), Assert.Single(result.Proposals).Target);
    }

    [Fact]
    public void AMemberOfTheRemovedObjectsLinkedGroupIsRemovedWithItWhenTheRoundIsApplied()
    {
        var partner = 7;
        var targets = Ring(SixDirections).Add(Visible(partner, new Vector3(5000, 0, 0)));
        var protection = ObjectsToKeep.Build(targets, [TestTargets.Link(0, partner)], References(targets.Length, []), UnresolvedBases);
        var decisions = RemovalDecisions.Start(protection, targets.Length).Apply(RoundKind.TooClose, [.. new[] { 1, 2, 3, 4, 5, 6 }.Select(TooClose)]);

        var result = Find(targets, decisions, Settings);
        var applied = decisions.Apply(RoundKind.VisibleLeftBehind, result.Proposals);

        var linked = Assert.IsType<Decision.Removed>(applied.Of(new TargetId(partner)));
        Assert.Equal(new RemovalReason.LinkedTo(new TargetId(0)), linked.Reason);
    }

    [Fact]
    public void AKeptObjectThatTheRoundRemovesAsALinkedGroupMemberIsEvaluatedAsRemovedLinked()
    {
        var partner = 7;
        var targets = Ring(SixDirections).Add(Visible(partner, new Vector3(5000, 0, 0)));
        var protection = ObjectsToKeep.Build(targets, [TestTargets.Link(0, partner)], References(targets.Length, []), UnresolvedBases);
        var decisions = RemovalDecisions.Start(protection, targets.Length).Apply(RoundKind.TooClose, [.. new[] { 1, 2, 3, 4, 5, 6 }.Select(TooClose)]);

        var result = Find(targets, decisions, Settings);

        var member = result.Evaluations.Single(check => check.TargetIndex == partner);
        Assert.Equal(VisibleLeftBehindOutcome.RemovedLinkedToRemoval, member.Decision);
        Assert.True(member.IsRemoved);
        Assert.Equal(new TargetId(0), Assert.Single(result.Proposals).Target);
    }

    [Fact]
    public void ANeighbourKeptShrunkByAnEarlierStepIsMeasuredAtItsNewSize()
    {
        var targets = Ring(SixDirections);
        var neighbour = targets[6];
        var decisions = DecisionsRemoving(targets, 1, 2, 3, 4, 5)
            .ApplyFits([new ProposedFit(neighbour.Id, new RemovalReason.TooClose(new OtherId(0), new TooCloseCause.ObjectSize()), neighbour.Placement with { Scale = 0.5f })]);
        var shrunkSector = DirectionSector.SouthWest;

        var shrunk = Assert.Single(FindWhereTheyStand(targets, decisions).Evaluations).Surroundings;
        var unchanged = Assert.Single(Find(targets, decisions, Settings).Evaluations).Surroundings;

        Assert.True(shrunk.Total(shrunkSector) < unchanged.Total(shrunkSector));
        Assert.Equal(unchanged.Total(DirectionSector.East), shrunk.Total(DirectionSector.East));
    }

    /// <summary>
    /// Object 0 has objects in six directions and four of them were removed earlier: it would be cleared only if object 1 to its east
    /// counted as removed too. Object 1 has five removed objects of its own around it, so the step removes it; that does not count for object 0.
    /// </summary>
    [Fact]
    public void ARemovalOfThisStepDoesNotClearTheSurroundingsOfAnotherObjectOfTheStep()
    {
        var result = Find(CascadeTargets, CascadeDecisions(), Settings);

        Assert.Equal(VisibleLeftBehindOutcome.KeptSurroundingsMostlyKept, result.Evaluations.Single(check => check.TargetIndex == 0).Decision);
        Assert.Equal(VisibleLeftBehindOutcome.RemovedSurroundingsRemoved, result.Evaluations.Single(check => check.TargetIndex == 1).Decision);
        Assert.Equal(new TargetId(1), Assert.Single(result.Proposals).Target);
    }

    [Fact]
    public void AnObjectIsNotItsOwnSurroundings()
    {
        ImmutableArray<TargetObject> targets = [Visible(0, Vector3.Zero)];

        var evaluation = Assert.Single(Find(targets, DecisionsRemoving(targets), Settings).Evaluations);

        Assert.Equal(0, evaluation.Surroundings.OccupiedCount);
        Assert.Equal(VisibleLeftBehindOutcome.KeptTooFewSurroundingObjects, evaluation.Decision);
    }

    [Fact]
    public void OtherModsObjectsAreNotSurroundings()
    {
        ImmutableArray<TargetObject> targets = [Visible(0, Vector3.Zero)];
        OtherObject[] others = [TestShapes.Placed(OtherMod, 0, Block.Base, new Vector3(RingDistance, 0, 0))];

        var evaluation = Assert.Single(Find(targets, DecisionsRemoving(targets), Settings, others).Evaluations);

        Assert.Equal(0, evaluation.Surroundings.OccupiedCount);
    }

    [Fact]
    public void InvisibleTargetsAreNeitherEvaluatedNorSurroundings()
    {
        var marker = TestVisibility.TaggedBase(ObjectVisibility.Invisible(InvisibleObjectKind.XMarkers));
        ImmutableArray<TargetObject> targets =
        [
            Visible(0, Vector3.Zero),
            TestTargets.Create(1, TestTargets.At(new Vector3(RingDistance, 0, 0)), marker, TestTargets.Space),
        ];

        var result = Find(targets, DecisionsRemoving(targets), Settings);

        var evaluation = Assert.Single(result.Evaluations);
        Assert.Equal(0, evaluation.TargetIndex);
        Assert.Equal(0, evaluation.Surroundings.OccupiedCount);
    }

    [Theory]
    [InlineData(BaseRecordKind.Npc, VisibleObjectKind.NpcsAndCreatures)]
    [InlineData(BaseRecordKind.Container, VisibleObjectKind.Containers)]
    [InlineData(BaseRecordKind.Furniture, VisibleObjectKind.Furniture)]
    [InlineData(BaseRecordKind.Activator, VisibleObjectKind.Activators)]
    [InlineData(BaseRecordKind.Door, null)]
    [InlineData(BaseRecordKind.Light, null)]
    [InlineData(BaseRecordKind.TreeOrPlant, null)]
    [InlineData(BaseRecordKind.LooseItem, null)]
    [InlineData(BaseRecordKind.Static, null)]
    [InlineData(BaseRecordKind.Other, null)]
    public void AVisibleObjectsKindFollowsTheRecordTypeOfItsBase(BaseRecordKind recordKind, VisibleObjectKind? expected)
    {
        var target = Visible(0, Vector3.Zero);

        Assert.Equal(expected, VisibleObjectKinds.Of(target, new KindsOfBases(new Dictionary<RecordKey, BaseRecordKind> { [Block.Base.Record] = recordKind })));
    }

    [Fact]
    public void ATickedKindIsKeptAndTheOtherKindsAreStillRemoved()
    {
        ImmutableArray<TargetObject> targets = [.. Ring(SixDirections).Select(target => target.Id.Index == 0 ? target with { Base = DoorBlock.Base } : target)];
        var bases = new KindsOfBases(new Dictionary<RecordKey, BaseRecordKind> { [DoorBlock.Base.Record] = BaseRecordKind.Container });
        var decisions = DecisionsRemoving(targets, 1, 2, 3, 4, 5, 6);

        var ticked = Find(targets, decisions, Settings with { NeverRemove = new HashSet<VisibleObjectKind> { VisibleObjectKind.Containers } }, bases: bases);
        var unticked = Find(targets, decisions, Settings with { NeverRemove = new HashSet<VisibleObjectKind> { VisibleObjectKind.Furniture } }, bases: bases);

        var keptContainer = Assert.Single(ticked.Evaluations);
        Assert.Equal(VisibleLeftBehindOutcome.KeptProtectedType, keptContainer.Decision);
        Assert.Equal(VisibleObjectKind.Containers, keptContainer.Kind);
        Assert.Empty(ticked.Proposals);
        Assert.Equal(VisibleLeftBehindOutcome.RemovedSurroundingsRemoved, Assert.Single(unticked.Evaluations).Decision);
        Assert.Single(unticked.Proposals);
    }

    [Fact]
    public void ASwitchedOffStepGivesNoEvaluationsAndNoProposals()
    {
        var targets = Ring(SixDirections);

        var result = Find(targets, DecisionsRemoving(targets, 1, 2, 3, 4, 5, 6), settings: null);

        Assert.Equal(StepOutcome.SwitchedOff, result.Outcome);
        Assert.Empty(result.Evaluations);
        Assert.Empty(result.Proposals);
    }

    [Fact]
    public void TheStepDecidesItsRoundOnACopyAndLeavesTheGivenDecisionsAsTheyWere()
    {
        var targets = Ring(SixDirections);
        var decisions = DecisionsRemoving(targets, 1, 2, 3, 4, 5, 6);
        var roundsBefore = decisions.Rounds.Length;

        var result = Find(targets, decisions, Settings);

        Assert.NotEmpty(result.Proposals);
        Assert.Equal(roundsBefore, decisions.Rounds.Length);
        Assert.False(decisions.IsDecided(new TargetId(0)));
    }

    [Fact]
    public void AFailureInsideTheStepStopsItWithTheFailureAndNoProposals()
    {
        var targets = Ring(SixDirections);

        var result = VisibleLeftBehindObjects.Find(
            targets,
            DecisionsRemoving(targets, 1, 2, 3, 4, 5, 6),
            new FailingSurroundings(Scene(targets, []).VisibleTargets),
            UnresolvedBases,
            Shapes,
            TargetWorkOrder.Of(targets),
            new Execution(Environment.ProcessorCount),
            Settings);

        Assert.Equal(StepOutcome.StoppedByUnexpectedError, result.Outcome);
        Assert.Equal(
            new PartFailure(VisibleLeftBehindParts.VisibleLeftBehind, "InvalidOperationException: it broke", Subject: null), result.Failure! with { Details = null });
        Assert.StartsWith("System.InvalidOperationException: it broke.", result.Failure.Details, StringComparison.Ordinal);
        Assert.Empty(result.Evaluations);
        Assert.Empty(result.Proposals);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(8)]
    public void TheEvaluationsDoNotDependOnTheNumberOfWorkers(int workers)
    {
        var expected = Summarize(Find(CascadeTargets, CascadeDecisions(), Settings, workers: 1));

        var actual = Summarize(Find(CascadeTargets, CascadeDecisions(), Settings, workers: workers));

        Assert.Equal(expected, actual);
    }

    private static readonly ImmutableArray<TargetObject> CascadeTargets = CascadeLayout();

    /// <summary>
    /// Object 0 at the origin with six neighbours (1 to its east, 2 north, 3 west, 4 south, 5 north-west, 6 south-west), and five objects
    /// (7 to 11) around object 1 that are not within reach of object 0.
    /// </summary>
    private static ImmutableArray<TargetObject> CascadeLayout()
    {
        var east = Offset(DirectionSector.East, RingDistance);
        return
        [
            Visible(0, Vector3.Zero),
            Visible(1, east),
            Visible(2, Offset(DirectionSector.North, RingDistance)),
            Visible(3, Offset(DirectionSector.West, RingDistance)),
            Visible(4, Offset(DirectionSector.South, RingDistance)),
            Visible(5, Offset(DirectionSector.NorthWest, RingDistance)),
            Visible(6, Offset(DirectionSector.SouthWest, RingDistance)),
            Visible(7, east + Offset(DirectionSector.East, RingDistance)),
            Visible(8, east + Offset(DirectionSector.NorthEast, RingDistance)),
            Visible(9, east + Offset(DirectionSector.SouthEast, RingDistance)),
            Visible(10, east + Offset(DirectionSector.North, RingDistance)),
            Visible(11, east + Offset(DirectionSector.South, RingDistance)),
        ];
    }

    private static RemovalDecisions CascadeDecisions() => DecisionsRemoving(CascadeTargets, 2, 3, 4, 5, 7, 8, 9, 10, 11);

    private static ImmutableArray<TargetObject> Ring(IEnumerable<DirectionSector> occupied) =>
    [
        Visible(0, Vector3.Zero),
        .. occupied.Select((sector, index) => Visible(index + 1, Offset(sector, RingDistance))),
    ];

    private static TargetObject Visible(int index, Vector3 position) =>
        TestTargets.Create(index, TestTargets.At(position), Block.Base, TestTargets.Space);

    /// <summary>East is 0 degrees and the directions go counter-clockwise, as in <see cref="DirectionSector"/>.</summary>
    private static Vector3 Offset(DirectionSector sector, float distance)
    {
        var angle = (int)sector * MathF.PI / CompassDirections.SectorCount * 2;
        return new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0) * distance;
    }

    private static KeepReason?[] References(int count, int[] quest) =>
        TestTargets.References(count, quest.ToDictionary(index => index, _ => TestKeepReasons.Quest));

    private static ProposedRemoval TooClose(int index) =>
        new(new TargetId(index), new RemovalReason.TooClose(new OtherId(0), new TooCloseCause.ObjectSize()));

    private static RemovalDecisions DecisionsRemoving(ImmutableArray<TargetObject> targets, params int[] removed) =>
        RemovalDecisions.Start(ObjectsToKeep.Build(targets, [], TestTargets.References(targets.Length), UnresolvedBases), targets.Length)
            .Apply(RoundKind.TooClose, [.. removed.Select(TooClose)]);

    private static ObjectCaches Scene(ImmutableArray<TargetObject> targets, IReadOnlyList<OtherObject> others) => TestScenes.Create(targets, others, Shapes);

    private static VisibleLeftBehindResult Find(
        ImmutableArray<TargetObject> targets,
        IRemovalDecisions decisions,
        VisibleLeftBehindSettings? settings,
        IReadOnlyList<OtherObject>? others = null,
        int workers = 4,
        IBaseFacts? bases = null) =>
        VisibleLeftBehindObjects.Find(
            targets,
            decisions,
            Scene(targets, others ?? []).VisibleTargets,
            bases ?? UnresolvedBases,
            Shapes,
            TargetWorkOrder.Of(targets),
            new Execution(workers),
            settings);

    private static VisibleLeftBehindResult FindWhereTheyStand(ImmutableArray<TargetObject> targets, IRemovalDecisions decisions) =>
        VisibleLeftBehindObjects.Find(
            targets,
            decisions,
            ClashingObjectFitter.IndexAsTheyStand(targets, decisions, Scene(targets, [])),
            UnresolvedBases,
            Shapes,
            TargetWorkOrder.Of(targets),
            new Execution(Environment.ProcessorCount),
            Settings);

    private static IEnumerable<(int Target, VisibleLeftBehindOutcome Decision, int Occupied, int Removed, string Areas)> Summarize(VisibleLeftBehindResult result) =>
        result.Evaluations.Select(check => (
            check.TargetIndex,
            check.Decision,
            check.Surroundings.OccupiedCount,
            check.Surroundings.RemovedCount,
            string.Join(' ', CompassDirections.All.Select(sector => $"{check.Surroundings.Removed(sector):R}/{check.Surroundings.Total(sector):R}"))));

    private sealed class KindsOfBases(IReadOnlyDictionary<RecordKey, BaseRecordKind> kinds) : IBaseFacts
    {
        public BaseFacts Of(BaseKey baseKey) =>
            BaseFacts.Unresolved(baseKey.Record) with { Resolved = true, Kind = kinds.GetValueOrDefault(baseKey.Record, BaseRecordKind.Other) };
    }

    private sealed class FailingSurroundings(IVisibleTargetObjects inner) : IVisibleTargetObjects
    {
        public IReadOnlyList<RecordKey> Spaces => inner.Spaces;

        public void Around(RecordKey space, Vector3 point, float radius, List<VisibleTargetNear> into) => throw new InvalidOperationException("it broke.");

        public bool AnyContains(RecordKey space, Vector3 point, Func<TargetId, bool> include, ObjectQueryScratch scratch) => inner.AnyContains(space, point, include, scratch);

        public IEnumerable<OrientedBox> BoxesNear(RecordKey space, Vector3 point, float radius, Func<TargetId, bool> include) =>
            inner.BoxesNear(space, point, radius, include);
    }
}
