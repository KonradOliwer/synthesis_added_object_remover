using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;
using static AddedObjectRemover.Tests.Fixtures.TestOneRoundSteps;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// The visible left-behind step: a candidate stands at the origin with other visible objects of the cleaned mod around it,
/// one per direction, and is removed when enough of those directions were cleared by earlier steps.
/// </summary>
public class VisibleLeftBehindObjectsTests
{
    private const float RingDistance = 200f;
    private const float FartherOut = 250f;
    private static readonly TimeSpan FollowUpDuration = TimeSpan.FromSeconds(100);

    private static readonly ModKey OtherMod = ModKey.FromNameAndExtension("Other.esp");
    private static readonly IReadOnlyList<MeshTriangle> SmallBlock = TestMeshes.BoxTriangles(new Box(new Vector3(-20), new Vector3(20)));
    private static readonly TestStatic Block = new(new FormKey(OtherMod, 0x801), @"test\block.nif", SmallBlock);
    private static readonly TestStatic DoorBlock = new(new FormKey(OtherMod, 0x802), @"test\doorblock.nif", SmallBlock);

    /// <summary>Mist: a mesh whose only shape is drawn with an effect shader, as big as <see cref="Block"/>.</summary>
    private static readonly TestStatic Mist = new(new FormKey(OtherMod, 0x803), @"test\mist.nif", SmallBlock, EffectOnly: true);

    /// <summary>A building that, placed near the origin, holds the candidate there, so it lies in every direction around it.</summary>
    private static readonly TestStatic Building = new(new FormKey(OtherMod, 0x804), @"test\building.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-100), new Vector3(100))));

    private static readonly IBaseObjectShapes Shapes = TestVisibility.Over(TestShapes.Create(OtherMod, "VisibleLeftBehindObjectsData", Block, DoorBlock, Mist, Building));

    /// <summary>With a minimum of one object around, so the directions alone decide.</summary>
    private static readonly VisibleLeftBehindSettings Settings = new(
        new LeftBehindSurroundings(LookAround: 260, MinOccupiedDirections: 6, DirectionClearedPercent: 80, ClearedDirectionsPercent: 80),
        MinObjectsAround: 1);

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
        Assert.Equal(LeftBehindOutcome.RemovedSurroundingsRemoved, evaluation.Decision);
        Assert.Equal(6, evaluation.Surroundings.OccupiedCount);
        Assert.Equal(6, evaluation.Surroundings.RemovedCount);
        var proposal = Assert.Single(Proposals(result));
        Assert.Equal(new TargetId(0), proposal.Target);
        Assert.IsType<VisibleObjectSurroundingsRemoved>(proposal.Reason);
    }

    /// <summary>The cleaned mod's mist, whose mesh holds only effect shapes, stands alone where its surroundings were removed, so it is removed too.</summary>
    [Fact]
    public void AnObjectWhoseMeshHoldsOnlyEffectsIsRemovedWhenItsSurroundingsWereRemoved()
    {
        var targets = Ring(SixDirections).SetItem(0, MistAt(0, Vector3.Zero));

        var result = Find(targets, DecisionsRemoving(targets, 1, 2, 3, 4, 5, 6), Settings);

        var evaluation = Assert.Single(result.Evaluations);
        Assert.Equal((0, LeftBehindOutcome.RemovedSurroundingsRemoved), (evaluation.TargetIndex, evaluation.Decision));
        Assert.Equal(new TargetId(0), Assert.Single(Proposals(result)).Target);
    }

    /// <summary>The cleaned mod's mists stand in four of the six directions around a block whose other neighbours were removed.</summary>
    [Fact]
    public void StandingObjectsWhoseMeshesHoldOnlyEffectsAreNoSurroundings()
    {
        var targets = Ring(SixDirections);
        var mists = targets.Select(target => target.Id.Index is >= 1 and <= 4 ? MistAt(target.Id.Index, target.Transform.Position) : target).ToImmutableArray();

        var result = Find(mists, DecisionsRemoving(mists, 5, 6), Settings);

        var evaluation = result.Evaluations.Single(check => check.TargetIndex == 0);
        Assert.Equal((2, 2, LeftBehindOutcome.KeptTooFewOccupiedDirections), (evaluation.Surroundings.OccupiedCount, evaluation.Surroundings.RemovedCount, evaluation.Decision));
    }

    /// <summary>Two of the cleaned mod's mists side by side, whose solid surroundings were all removed: neither keeps the other.</summary>
    [Fact]
    public void ObjectsWhoseMeshesHoldOnlyEffectsDoNotKeepEachOther()
    {
        var ring = Ring(SixDirections).SetItem(0, MistAt(0, Vector3.Zero));
        var targets = ring.Add(MistAt(ring.Length, new Vector3(1, 0, 0)));

        var result = Find(targets, DecisionsRemoving(targets, 1, 2, 3, 4, 5, 6), Settings);

        Assert.Equal([new TargetId(0), new TargetId(ring.Length)], Proposals(result).Select(proposal => proposal.Target).Order());
    }

    [Fact]
    public void APerformedStepProposesVisibleLeftBehindRoundsAndNamesThemAsTheDecisionsNumberThem()
    {
        var decisions = CascadeDecisions();

        var (result, applied) = Decide(CascadeTargets, decisions, Settings);

        Assert.Equal(3, result.ProposedRounds.Length);
        Assert.All(result.ProposedRounds, round => Assert.Equal(RoundKind.VisibleLeftBehind, round.Opening.Kind));
        Assert.Equal(applied.RoundsAfter(decisions).ToArray(), result.Rounds.ToArray());
    }

    /// <summary>The removed buildings around the candidate lie in all 8 directions, so only their number keeps it; the candidate itself never counts.</summary>
    [Theory]
    [InlineData(4, nameof(LeftBehindOutcome.KeptTooFewObjectsAround))]
    [InlineData(5, nameof(LeftBehindOutcome.RemovedSurroundingsRemoved))]
    public void RemovedBuildingsInEveryDirectionRemoveTheObjectOnlyFromTheMinimumOn(int buildings, string expected)
    {
        var targets = AmongBuildings(buildings);

        var evaluation = Assert.Single(Find(targets, DecisionsRemoving(targets, [.. Enumerable.Range(1, buildings)]), Settings with { MinObjectsAround = 5 }).Evaluations);

        Assert.Equal((buildings, CompassDirections.SectorCount, expected), (evaluation.Surroundings.Objects, evaluation.Surroundings.ClearedCount, evaluation.Decision.ToString()));
    }

    /// <summary>Two removed buildings around the candidate pass the directions value, which alone would remove it.</summary>
    [Fact]
    public void TwoRemovedBuildingsAroundTheObjectKeepItByTheMinimumNotByTheDirections()
    {
        var targets = AmongBuildings(2);

        var byMinimum = Assert.Single(Find(targets, DecisionsRemoving(targets, 1, 2), Settings with { MinObjectsAround = 5 }).Evaluations);
        var byDirections = Assert.Single(Find(targets, DecisionsRemoving(targets, 1, 2), Settings with { MinObjectsAround = 2 }).Evaluations);

        Assert.Equal((CompassDirections.SectorCount, CompassDirections.SectorCount), (byMinimum.Surroundings.OccupiedCount, byMinimum.Surroundings.ClearedCount));
        Assert.Equal(LeftBehindOutcome.KeptTooFewObjectsAround, byMinimum.Decision);
        Assert.Equal(LeftBehindOutcome.RemovedSurroundingsRemoved, byDirections.Decision);
    }

    [Fact]
    public void WithTooFewObjectsAndTooFewDirectionsTheMinimumIsTheReason()
    {
        var targets = Ring(SixDirections.Take(3));

        var evaluation = Assert.Single(Find(targets, DecisionsRemoving(targets, 1, 2, 3), Settings with { MinObjectsAround = 5 }).Evaluations);

        Assert.Equal(LeftBehindOutcome.KeptTooFewObjectsAround, evaluation.Decision);
    }

    [Fact]
    public void FiveOccupiedDirectionsKeepTheObjectEvenWhenAllOfThemWereCleared()
    {
        var targets = Ring(SixDirections.Take(5));

        var result = Find(targets, DecisionsRemoving(targets, 1, 2, 3, 4, 5), Settings);

        var evaluation = Assert.Single(result.Evaluations);
        Assert.Equal(LeftBehindOutcome.KeptTooFewOccupiedDirections, evaluation.Decision);
        Assert.Empty(Proposals(result));
    }

    [Fact]
    public void ObjectsWhoseSurroundingsAreMostlyKeptAreKeptWithTheirClearedDirectionsCounted()
    {
        var targets = Ring(SixDirections);

        var result = Find(targets, DecisionsRemoving(targets, 1, 2, 3, 4), Settings);

        var evaluation = result.Evaluations.Single(check => check.TargetIndex == 0);
        Assert.Equal(LeftBehindOutcome.KeptSurroundingsMostlyKept, evaluation.Decision);
        Assert.Equal(6, evaluation.Surroundings.OccupiedCount);
        Assert.Equal(4, evaluation.Surroundings.RemovedCount);
        Assert.DoesNotContain(Proposals(result), proposal => proposal.Target.Index == 0);
    }

    [Fact]
    public void RemovalsOfTheFirstAlsoRemovePassCountAsClearedLikeTheOtherRemovals()
    {
        var targets = Ring(SixDirections);
        var decisions = DecisionsRemoving(targets, 1, 2, 3)
            .Apply(AlsoRemoveRounds.Opening, [.. new[] { 4, 5, 6 }.Select(index => new ProposedRemoval(new TargetId(index), new TouchingTargetThatLeft(new TargetId(1))))]);

        var evaluation = Assert.Single(Find(targets, decisions, Settings).Evaluations);

        Assert.Equal(6, evaluation.Surroundings.RemovedCount);
        Assert.Equal(LeftBehindOutcome.RemovedSurroundingsRemoved, evaluation.Decision);
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
        Assert.Equal(LeftBehindOutcome.RemovedSurroundingsRemoved, evaluation.Decision);
    }

    [Fact]
    public void ObjectsAnEarlierStepDecidedAreNotEvaluated()
    {
        var targets = Ring(SixDirections);

        var result = Find(targets, DecisionsRemoving(targets, 0, 1, 2, 3, 4, 5, 6), Settings);

        Assert.Empty(result.Evaluations);
        Assert.Empty(Proposals(result));
    }

    [Fact]
    public void ARemovalOfAReferencedObjectIsKeptAsReferencedWithTheKeepReason()
    {
        var targets = Ring(SixDirections);
        var protection = ObjectsToKeep.Build(targets, [], References(targets.Length, [0]), UnresolvedBases);
        var decisions = TestSeededDecisions.StartDisablingAll(protection, targets.Length).Apply(RoundKind.TooClose, [.. new[] { 1, 2, 3, 4, 5, 6 }.Select(TooClose)]);

        var result = Find(targets, decisions, Settings);

        var evaluation = Assert.Single(result.Evaluations);
        Assert.Equal(LeftBehindOutcome.KeptReferenced, evaluation.Decision);
        Assert.Equal(TestKeepReasons.Quest.Kind, evaluation.KeepReason?.Kind);
        Assert.False(evaluation.IsRemoved);
        Assert.Equal(new TargetId(0), Assert.Single(Proposals(result)).Target);
    }

    [Fact]
    public void ARemovalOfAnObjectInADeletedCellIsKeptInADeletedCellNotAsReferenced()
    {
        var ring = Ring(SixDirections);
        var targets = ring.SetItem(0, ring[0] with { CellDeleted = TestKeepReasons.DeletedByCityMod });
        var protection = ObjectsToKeep.Build(targets, [], References(targets.Length, []), UnresolvedBases);
        var decisions = TestSeededDecisions.StartDisablingAll(protection, targets.Length).Apply(RoundKind.TooClose, [.. new[] { 1, 2, 3, 4, 5, 6 }.Select(TooClose)]);

        var result = Find(targets, decisions, Settings);

        var evaluation = Assert.Single(result.Evaluations);
        Assert.Equal(LeftBehindOutcome.KeptInDeletedCell, evaluation.Decision);
        Assert.Equal(KeepKind.CellDeleted, evaluation.KeepReason?.Kind);
    }

    [Fact]
    public void AMemberOfTheRemovedObjectsLinkedGroupIsRemovedWithItWhenTheRoundIsApplied()
    {
        var partner = 7;
        var targets = Ring(SixDirections).Add(Visible(partner, new Vector3(5000, 0, 0)));
        var protection = ObjectsToKeep.Build(targets, [TestTargets.Link(0, partner)], References(targets.Length, []), UnresolvedBases);
        var decisions = TestSeededDecisions.StartDisablingAll(protection, targets.Length).Apply(RoundKind.TooClose, [.. new[] { 1, 2, 3, 4, 5, 6 }.Select(TooClose)]);

        var applied = Decide(targets, decisions, Settings).Decisions;

        var linked = Assert.IsType<Decision.Removed>(applied.Of(new TargetId(partner)));
        Assert.Equal(new LinkedTo(new TargetId(0)), linked.Reason);
    }

    [Theory]
    [InlineData(false, LeftBehindOutcome.RemovedSurroundingsRemovedAndRestored)]
    [InlineData(true, LeftBehindOutcome.RestoredSurroundingsRemovedAndRestored)]
    public void ARestoredNeighbourGivesTheReasonSurroundingsRemovedAndRestoredWhateverBecomesOfTheObject(bool objectIsRestored, LeftBehindOutcome expected)
    {
        var targets = Ring(SixDirections);
        var protection = ObjectsToKeep.Build(targets, [], References(targets.Length, []), UnresolvedBases);
        WhenRemoved WhenRestored(int index) => new WhenRemoved.Restore(TestTargets.EarlierVersionAt(new PluginName("Earlier.esp"), targets[index].Placement));
        var decisions = RemovalDecisions.Start(
                protection,
                [objectIsRestored ? WhenRestored(0) : new WhenRemoved.Disable(), WhenRestored(1), .. Enumerable.Repeat<WhenRemoved>(new WhenRemoved.Disable(), 5)])
            .Apply(RoundKind.TooClose, [.. new[] { 1, 2, 3, 4, 5, 6 }.Select(TooClose)]);

        var evaluation = Assert.Single(Find(targets, decisions, Settings).Evaluations);

        Assert.Equal(expected, evaluation.Decision);
        Assert.Equal(objectIsRestored, evaluation.IsRestored);
        Assert.Equal(!objectIsRestored, evaluation.IsRemoved);
    }

    [Fact]
    public void ANeighbourKeptShrunkByAnEarlierStepIsMeasuredAtItsNewSize()
    {
        var targets = Ring(SixDirections);
        var neighbour = targets[6];
        RemovalDecisions Decisions() => DecisionsRemoving(targets, 1, 2, 3, 4, 5)
            .ApplyFits([new ProposedFit(neighbour.Id, new TooClose(new OtherId(0), new TooCloseCause.ObjectSize()), neighbour.Placement with { Scale = 0.5f })]);
        var shrunkSector = DirectionSector.SouthWest;

        var shrunk = Assert.Single(Find(targets, Decisions(), Settings).Evaluations).Surroundings;
        var unchanged = Assert.Single(Find(targets, Decisions(), Settings, surroundings: _ => Scene(targets, []).VisibleTargets).Evaluations).Surroundings;

        Assert.True(shrunk.Total(shrunkSector) < unchanged.Total(shrunkSector));
        Assert.Equal(unchanged.Total(DirectionSector.East), shrunk.Total(DirectionSector.East));
    }

    /// <summary>
    /// Object 0 has objects in eight directions and six of them were removed earlier: it is cleared only once object 1 to its east
    /// counts as removed too. Object 1 has seven removed objects of its own around it, so the first round removes it; that counts for
    /// object 0 only in the second round. The third round judges object 6 next to object 0 and keeps it.
    /// </summary>
    [Fact]
    public void ARemovalOfThisStepClearsTheSurroundingsOfAnotherObjectOfTheStepOnlyInTheNextRound()
    {
        var result = Find(CascadeTargets, CascadeDecisions(), Settings);

        Assert.Equal(new[] { new[] { 1 }, [0], [] }, ProposedTargets(result));
        Assert.Equal(LeftBehindOutcome.RemovedSurroundingsRemoved, Evaluation(result, 0).Decision);
        Assert.Equal(7, Evaluation(result, 0).Surroundings.RemovedCount);
        Assert.Equal(LeftBehindOutcome.RemovedSurroundingsRemoved, Evaluation(result, 1).Decision);
    }

    /// <summary>The object at the origin and the one to its east of <see cref="ARemovalOfThisStepClearsTheSurroundingsOfAnotherObjectOfTheStepOnlyInTheNextRound"/> swap their ids.</summary>
    [Fact]
    public void SwappingTheIdsOfTwoDependentObjectsChangesNothingButTheIds()
    {
        var swappedTargets = CascadeLayout(origin: 1, east: 0);

        var plain = Find(CascadeTargets, CascadeDecisions(), Settings);
        var swapped = Find(swappedTargets, DecisionsRemoving(swappedTargets, CascadeRemovedEarlier), Settings);

        Assert.Equal(new[] { new[] { 0 }, [1], [] }, ProposedTargets(swapped));
        Assert.Equal(Summary(Evaluation(plain, 0)), Summary(Evaluation(swapped, 1)));
        Assert.Equal(Summary(Evaluation(plain, 1)), Summary(Evaluation(swapped, 0)));
    }

    /// <summary>As in <see cref="ARemovalOfThisStepClearsTheSurroundingsOfAnotherObjectOfTheStepOnlyInTheNextRound"/>, but object 5 to the north-west of object 0 stands.</summary>
    [Fact]
    public void TheRoundsStopAfterARoundThatTakesNothingFromItsPlace()
    {
        var result = Find(CascadeTargets, DecisionsRemoving(CascadeTargets, 2, 3, 4, 7, 8, 9, 10, 11), Settings);

        Assert.Equal(new[] { new[] { 1 }, [] }, ProposedTargets(result));
        Assert.Equal(2, result.Rounds.Length);
        Assert.Equal(LeftBehindOutcome.KeptSurroundingsMostlyKept, Evaluation(result, 0).Decision);
        Assert.Equal(6, Evaluation(result, 0).Surroundings.RemovedCount);
    }

    /// <summary>
    /// Object 0 has three of its six directions removed earlier; objects 1 to its east and 3 to its west each have their own removed
    /// surroundings, so the first round proposes both. Object 0 is cleared only when both of them leave their place.
    /// </summary>
    [Theory]
    [InlineData(false, LeftBehindOutcome.RemovedSurroundingsRemoved, 5)]
    [InlineData(true, LeftBehindOutcome.KeptSurroundingsMostlyKept, 4)]
    public void AnObjectARoundKeepsAsReferencedClearsNothingInTheNextRound(bool westIsReferenced, LeftBehindOutcome expected, int removedDirections)
    {
        var targets = TwoSidedLayout();
        var protection = ObjectsToKeep.Build(targets, [], References(targets.Length, westIsReferenced ? [West] : []), UnresolvedBases);
        var decisions = TestSeededDecisions.StartDisablingAll(protection, targets.Length)
            .Apply(RoundKind.TooClose, [.. new[] { 2, 5, 6 }.Concat(Enumerable.Range(7, AwayFromTheWest.Length + AwayFromTheEast.Length)).Select(TooClose)]);

        var result = Find(targets, decisions, Settings);

        Assert.Equal(new[] { 1, West }, ProposedTargets(result)[0]);
        Assert.Equal(westIsReferenced ? LeftBehindOutcome.KeptReferenced : LeftBehindOutcome.RemovedSurroundingsRemoved, Evaluation(result, West).Decision);
        Assert.Equal(expected, Evaluation(result, 0).Decision);
        Assert.Equal(removedDirections, Evaluation(result, 0).Surroundings.RemovedCount);
    }

    /// <summary>
    /// Object 0 has four of its six directions removed earlier; object 1 to its east is linked to object 7 far away, whose surroundings
    /// were all removed. The first round removes 7 and 1 with it, which clears object 0 in the second round.
    /// </summary>
    [Fact]
    public void ALinkedGroupMemberARoundRemovesClearsItsDirectionInTheNextRound()
    {
        var partner = 7;
        var farAway = new Vector3(5000, 0, 0);
        var targets = Ring(SixDirections).Add(Visible(partner, farAway)).AddRange(RingAround(farAway, partner + 1, SixDirections));
        var protection = ObjectsToKeep.Build(targets, [TestTargets.Link(1, partner)], References(targets.Length, []), UnresolvedBases);
        var decisions = TestSeededDecisions.StartDisablingAll(protection, targets.Length)
            .Apply(RoundKind.TooClose, [.. new[] { 3, 4, 5, 6 }.Concat(Enumerable.Range(partner + 1, SixDirections.Length)).Select(TooClose)]);

        var result = Find(targets, decisions, Settings);

        Assert.Equal(new[] { new[] { partner }, [0], [] }, ProposedTargets(result));
        Assert.Equal(LeftBehindOutcome.RemovedLinkedToRemoval, Evaluation(result, 1).Decision);
        Assert.Equal(LeftBehindOutcome.RemovedSurroundingsRemoved, Evaluation(result, 0).Decision);
    }

    /// <summary>As in <see cref="ARemovalOfThisStepClearsTheSurroundingsOfAnotherObjectOfTheStepOnlyInTheNextRound"/>, but object 1 is restored instead of disabled.</summary>
    [Fact]
    public void AnObjectARoundRestoresGivesTheReasonSurroundingsRemovedAndRestoredInTheNextRound()
    {
        var protection = ObjectsToKeep.Build(CascadeTargets, [], References(CascadeTargets.Length, []), UnresolvedBases);
        var restore = new WhenRemoved.Restore(TestTargets.EarlierVersionAt(new PluginName("Earlier.esp"), CascadeTargets[1].Placement));
        var decisions = RemovalDecisions.Start(protection, [.. CascadeTargets.Select(target => target.Id.Index == 1 ? restore : (WhenRemoved)new WhenRemoved.Disable())])
            .Apply(RoundKind.TooClose, [.. CascadeRemovedEarlier.Select(TooClose)]);

        var result = Find(CascadeTargets, decisions, Settings);

        Assert.Equal(new[] { new[] { 1 }, [0], [] }, ProposedTargets(result));
        Assert.Equal(LeftBehindOutcome.RestoredSurroundingsRemoved, Evaluation(result, 1).Decision);
        Assert.Equal(LeftBehindOutcome.RemovedSurroundingsRemovedAndRestored, Evaluation(result, 0).Decision);
    }

    [Fact]
    public void WithoutEarlierRemovalsNoRoundRunsAndEveryObjectIsEvaluatedAsKept()
    {
        var targets = Ring(SixDirections);

        var result = Find(targets, DecisionsRemoving(targets), Settings);

        Assert.Empty(result.ProposedRounds);
        Assert.Empty(result.Rounds);
        Assert.Equal(Enumerable.Range(0, targets.Length), result.Evaluations.Select(check => check.TargetIndex));
        Assert.All(result.Evaluations, check => Assert.False(check.IsRemoved || check.IsRestored));
    }

    [Fact]
    public void AnObjectIsNotItsOwnSurroundings()
    {
        ImmutableArray<TargetObject> targets = [Visible(0, Vector3.Zero)];

        var evaluation = Assert.Single(Find(targets, DecisionsRemoving(targets), Settings).Evaluations);

        Assert.Equal((0, 0), (evaluation.Surroundings.Objects, evaluation.Surroundings.OccupiedCount));
        Assert.Equal(LeftBehindOutcome.KeptTooFewObjectsAround, evaluation.Decision);
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
        var ticked = Find(
            targets, DecisionsRemoving(targets, 1, 2, 3, 4, 5, 6), Settings, bases: bases, alwaysKept: new HashSet<VisibleObjectKind> { VisibleObjectKind.Containers });
        var unticked = Find(
            targets, DecisionsRemoving(targets, 1, 2, 3, 4, 5, 6), Settings, bases: bases, alwaysKept: new HashSet<VisibleObjectKind> { VisibleObjectKind.Furniture });

        var keptContainer = Assert.Single(ticked.Evaluations);
        Assert.Equal(LeftBehindOutcome.KeptProtectedType, keptContainer.Decision);
        Assert.Equal(VisibleObjectKind.Containers, keptContainer.Kind);
        Assert.Empty(Proposals(ticked));
        Assert.Equal(LeftBehindOutcome.RemovedSurroundingsRemoved, Assert.Single(unticked.Evaluations).Decision);
        Assert.Single(Proposals(unticked));
    }

    [Fact]
    public void ASwitchedOffStepGivesNoEvaluationsAndNoRounds()
    {
        var targets = Ring(SixDirections);

        var result = Find(targets, DecisionsRemoving(targets, 1, 2, 3, 4, 5, 6), settings: null);

        Assert.Equal(StepOutcome.SwitchedOff, result.Outcome);
        Assert.Empty(result.Evaluations);
        Assert.Empty(result.ProposedRounds);
        Assert.Empty(result.Rounds);
    }

    [Fact]
    public void AFailureInsideTheStepStopsItWithTheFailureAndNoRounds()
    {
        var targets = Ring(SixDirections);
        var decisions = DecisionsRemoving(targets, 1, 2, 3, 4, 5, 6);

        var (result, applied) = Decide(targets, decisions, Settings, surroundings: _ => new FailingSurroundings(Scene(targets, []).VisibleTargets));

        Assert.Equal(StepOutcome.StoppedByUserDataFailure, result.Outcome);
        Assert.Equal(decisions.Rounds.AsEnumerable(), applied.Rounds.AsEnumerable());
        Assert.Equal(
            new PartFailure(VisibleLeftBehindParts.VisibleLeftBehind, "PlantedUserDataException: it broke", Subject: null), result.Failure! with { Details = null });
        Assert.StartsWith("AddedObjectRemover.Tests.Fixtures.PlantedUserDataException: it broke.", result.Failure.Details, StringComparison.Ordinal);
        Assert.Empty(result.Evaluations);
        Assert.Empty(result.ProposedRounds);
        Assert.Empty(result.Rounds);
        Assert.Equal(decisions.Rounds.AsEnumerable(), result.FollowUp.Rounds.AsEnumerable());
    }

    [Fact]
    public void ASwitchedOffStepCarriesTheFollowUpSummarizedFromTheGivenDecisions()
    {
        var targets = Ring(SixDirections);
        var decisions = DecisionsRemoving(targets, 1, 2, 3, 4, 5, 6);
        var followUp = new ScriptedFollowUp();

        var result = Find(targets, decisions, settings: null, followUp: followUp);

        Assert.Empty(followUp.Seeds);
        Assert.Equal(decisions.Rounds.AsEnumerable(), result.FollowUp.Rounds.AsEnumerable());
    }

    /// <summary>
    /// As in <see cref="TheRoundsStopAfterARoundThatTakesNothingFromItsPlace"/>, but the follow-up of the first round removes object 5
    /// to the north-west of object 0, which clears object 0 in the second visible round. The third judges object 6 next to object 0.
    /// </summary>
    [Fact]
    public void AFollowUpRemovalClearsItsDirectionForTheNextVisibleRound()
    {
        var decisions = DecisionsRemoving(CascadeTargets, 2, West, 4, 7, 8, 9, 10, 11);
        var followUp = new ScriptedFollowUp(seeds => seeds.SequenceEqual([1]) ? [[NorthWest], []] : []);

        var (result, final) = Decide(CascadeTargets, decisions, Settings, followUp: followUp);

        Assert.Equal(
            new[] { "VisibleLeftBehind:1", "AlsoRemove:5", "AlsoRemove:", "VisibleLeftBehind:0", "VisibleLeftBehind:" }, DescribeRounds(result.ProposedRounds));
        Assert.Equal(new[] { new[] { 1 }, [0] }, followUp.Seeds);
        Assert.Equal(final.RoundsAfter(decisions).Where(round => round.Kind == RoundKind.VisibleLeftBehind), result.Rounds.AsEnumerable());
        Assert.Equal(LeftBehindOutcome.RemovedSurroundingsRemoved, Evaluation(result, 0).Decision);
        Assert.Equal(7, Evaluation(result, 0).Surroundings.RemovedCount);
        Assert.Equal(final.Rounds.AsEnumerable(), result.FollowUp.Rounds.AsEnumerable());
    }

    /// <summary>As in <see cref="AFollowUpRemovalClearsItsDirectionForTheNextVisibleRound"/>, with a follow-up that takes much longer than a visible round.</summary>
    [Fact]
    public void TheRoundsTimeIsThatOfTheVisibleRoundsAloneAndLeavesTheFollowUpsOut()
    {
        var decisions = DecisionsRemoving(CascadeTargets, 2, West, 4, 7, 8, 9, 10, 11);
        var clock = new StepClock();
        var followUp = new ScriptedFollowUp(
            seeds =>
            {
                clock.Advance(FollowUpDuration);
                return seeds.SequenceEqual([1]) ? [[NorthWest], []] : [];
            });

        var result = Find(CascadeTargets, decisions, Settings, followUp: followUp, timeSinceStart: clock.Read);

        Assert.Equal(3, result.Rounds.Length);
        Assert.Equal(StepClock.ReadingStep * result.Rounds.Length, result.RoundsTime);
    }

    /// <summary>
    /// As in <see cref="AFollowUpRemovalClearsItsDirectionForTheNextVisibleRound"/>; object 5, which its own visible round kept for too
    /// few surrounding objects, is then removed by the follow-up, or kept by it as referenced, which clears nothing.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnObjectAFollowUpDecidedKeepsTheVerdictOfItsVisibleRound(bool northWestIsReferenced)
    {
        var protection = ObjectsToKeep.Build(CascadeTargets, [], References(CascadeTargets.Length, northWestIsReferenced ? [NorthWest] : []), UnresolvedBases);
        var decisions = TestSeededDecisions.StartDisablingAll(protection, CascadeTargets.Length)
            .Apply(RoundKind.TooClose, [.. new[] { 2, West, 4, 7, 8, 9, 10, 11 }.Select(TooClose)]);

        var (result, applied) = Decide(CascadeTargets, decisions, Settings, followUp: new ScriptedFollowUp(seeds => seeds.SequenceEqual([1]) ? [[NorthWest], []] : []));

        var northWest = applied.Of(new TargetId(NorthWest))!;
        Assert.Equal(RoundKind.AlsoRemove, northWest.Round.Kind);
        Assert.Equal(northWestIsReferenced, northWest is Decision.Kept);
        Assert.Equal(LeftBehindOutcome.KeptTooFewOccupiedDirections, Evaluation(result, NorthWest).Decision);
        Assert.Null(Evaluation(result, NorthWest).KeepReason);
        Assert.Equal(northWestIsReferenced ? LeftBehindOutcome.KeptSurroundingsMostlyKept : LeftBehindOutcome.RemovedSurroundingsRemoved, Evaluation(result, 0).Decision);
    }

    [Fact]
    public void TheFollowUpFollowsEachVisibleRoundThatTookSomethingFromItsPlaceAndNoOther()
    {
        var followUp = new ScriptedFollowUp();

        var result = Find(CascadeTargets, CascadeDecisions(), Settings, followUp: followUp);

        Assert.Equal(new[] { new[] { 1 }, [0], [] }, ProposedTargets(result));
        Assert.Equal(new[] { new[] { 1 }, [0] }, followUp.Seeds);
    }

    /// <summary>The follow-up of the round that removes object 0 runs one round that takes nothing; no object's surroundings changed after it.</summary>
    [Fact]
    public void TheRoundsStopWhenNoUndecidedObjectsSurroundingsChanged()
    {
        var targets = Ring(SixDirections);

        var result = Find(targets, DecisionsRemoving(targets, 1, 2, 3, 4, 5, 6), Settings, followUp: new ScriptedFollowUp(_ => [[]]));

        Assert.Equal(new[] { "VisibleLeftBehind:0", "AlsoRemove:" }, DescribeRounds(result.ProposedRounds));
        Assert.Single(result.Rounds);
    }

    [Fact]
    public void AFailedFollowUpProposesNothingAndTheVisibleRoundsGoOnAlone()
    {
        var failure = new PartFailure(RestingObjectsParts.AlsoRemove, "InvalidOperationException: broke", Subject: null);

        var result = Find(CascadeTargets, CascadeDecisions(), Settings, followUp: new ScriptedFollowUp(_ => [[NorthWest]], failure));

        Assert.Equal(StepOutcome.Performed, result.Outcome);
        Assert.Null(result.Failure);
        Assert.Equal(new[] { "VisibleLeftBehind:1", "VisibleLeftBehind:0", "VisibleLeftBehind:" }, DescribeRounds(result.ProposedRounds));
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

    private const int West = 3;
    private const int NorthWest = 5;

    private static readonly DirectionSector[] AwayFromTheEast =
        [DirectionSector.West, DirectionSector.NorthWest, DirectionSector.SouthWest, DirectionSector.North, DirectionSector.South];

    private static readonly DirectionSector[] AwayFromTheWest =
        [DirectionSector.East, DirectionSector.NorthEast, DirectionSector.SouthEast, DirectionSector.North, DirectionSector.South];

    private static readonly ImmutableArray<TargetObject> CascadeTargets = CascadeLayout(origin: 0, east: 1);

    private static readonly int[] CascadeRemovedEarlier = [2, 3, 4, 5, 7, 8, 9, 10, 11];

    /// <summary>
    /// One object at the origin and one to its east, with the given ids, then the origin's other neighbours (2 north, 3 west, 4 south,
    /// 5 north-west, 6 south-west), and five objects (7 to 11) around the eastern one; of those, 10 and 11 to its north and south are
    /// within reach of the origin too.
    /// </summary>
    private static ImmutableArray<TargetObject> CascadeLayout(int origin, int east)
    {
        var eastPosition = Offset(DirectionSector.East, RingDistance);
        return
        [
            .. new[] { Visible(origin, Vector3.Zero), Visible(east, eastPosition) }.OrderBy(target => target.Id.Index),
            Visible(2, Offset(DirectionSector.North, RingDistance)),
            Visible(West, Offset(DirectionSector.West, RingDistance)),
            Visible(4, Offset(DirectionSector.South, RingDistance)),
            Visible(NorthWest, Offset(DirectionSector.NorthWest, RingDistance)),
            Visible(6, Offset(DirectionSector.SouthWest, RingDistance)),
            .. RingAround(eastPosition, 7, AwayFromTheWest),
        ];
    }

    /// <summary>
    /// Object 0 at the origin with six neighbours (1 east, 2 north, 3 west, 4 south, 5 north-west, 6 south-west); objects 1 and 3 lie
    /// farther out, so that the five objects around each of them (7 to 11 around 1, 12 to 16 around 3) are out of the origin's reach.
    /// </summary>
    private static ImmutableArray<TargetObject> TwoSidedLayout()
    {
        var east = Offset(DirectionSector.East, FartherOut);
        var west = Offset(DirectionSector.West, FartherOut);
        return
        [
            Visible(0, Vector3.Zero),
            Visible(1, east),
            Visible(2, Offset(DirectionSector.North, RingDistance)),
            Visible(West, west),
            Visible(4, Offset(DirectionSector.South, RingDistance)),
            Visible(5, Offset(DirectionSector.NorthWest, RingDistance)),
            Visible(6, Offset(DirectionSector.SouthWest, RingDistance)),
            .. RingAround(east, 7, AwayFromTheWest),
            .. RingAround(west, 7 + AwayFromTheWest.Length, AwayFromTheEast),
        ];
    }

    private static RemovalDecisions CascadeDecisions() => DecisionsRemoving(CascadeTargets, CascadeRemovedEarlier);

    private static ImmutableArray<TargetObject> Ring(IEnumerable<DirectionSector> occupied) => [Visible(0, Vector3.Zero), .. RingAround(Vector3.Zero, 1, occupied)];

    private static IEnumerable<TargetObject> RingAround(Vector3 center, int firstIndex, IEnumerable<DirectionSector> occupied) =>
        occupied.Select((sector, index) => Visible(firstIndex + index, center + Offset(sector, RingDistance)));

    /// <summary>The candidate at the origin, then the given number of buildings, each holding the origin.</summary>
    private static ImmutableArray<TargetObject> AmongBuildings(int buildings) =>
    [
        Visible(0, Vector3.Zero),
        .. Enumerable.Range(1, buildings).Select(index => TestTargets.Create(index, TestTargets.At(new Vector3(index, 0, 0)), Building.Base, TestTargets.Space)),
    ];

    private static TargetObject Visible(int index, Vector3 position) =>
        TestTargets.Create(index, TestTargets.At(position), Block.Base, TestTargets.Space);

    private static TargetObject MistAt(int index, Vector3 position) =>
        TestTargets.Create(index, TestTargets.At(position), Mist.Base, TestTargets.Space);

    /// <summary>East is 0 degrees and the directions go counter-clockwise, as in <see cref="DirectionSector"/>.</summary>
    private static Vector3 Offset(DirectionSector sector, float distance)
    {
        var angle = (int)sector * MathF.PI / CompassDirections.SectorCount * 2;
        return new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0) * distance;
    }

    private static KeepReason?[] References(int count, int[] quest) =>
        TestTargets.References(count, quest.ToDictionary(index => index, _ => TestKeepReasons.Quest));

    private static RemovalDecisions DecisionsRemoving(ImmutableArray<TargetObject> targets, params int[] removed) =>
        TestSeededDecisions.StartDisablingAll(ObjectsToKeep.Build(targets, [], TestTargets.References(targets.Length), UnresolvedBases), targets.Length)
            .Apply(RoundKind.TooClose, [.. removed.Select(TooClose)]);

    private static IReadOnlyList<ProposedRemoval> Proposals(VisibleLeftBehindResult result) =>
        [.. result.ProposedRounds.SelectMany(round => round.Proposals)];

    private static ObjectCaches Scene(ImmutableArray<TargetObject> targets, IReadOnlyList<OtherObject> others) => TestScenes.Create(targets, others, Shapes);

    private static VisibleLeftBehindResult Find(
        ImmutableArray<TargetObject> targets,
        IRunDecisions decisions,
        VisibleLeftBehindSettings? settings,
        IReadOnlyList<OtherObject>? others = null,
        int workers = 4,
        IBaseFacts? bases = null,
        IAlsoRemoveFollowUp? followUp = null,
        Func<TimeSpan>? timeSinceStart = null,
        Func<IReadOnlyList<TargetObject>, IVisibleTargetObjects>? surroundings = null,
        IReadOnlySet<VisibleObjectKind>? alwaysKept = null) =>
        Decide(targets, decisions, settings, others, workers, bases, followUp, timeSinceStart, surroundings, alwaysKept).Result;

    /// <param name="followUp">By default one that proposes nothing.</param>
    /// <param name="surroundings">The visible targets the caches index for the given targets; by default the scene's index among <paramref name="others"/>.</param>
    /// <returns>The step's result, with its rounds added to <paramref name="decisions"/>.</returns>
    private static Decided<VisibleLeftBehindResult> Decide(
        ImmutableArray<TargetObject> targets,
        IRunDecisions decisions,
        VisibleLeftBehindSettings? settings,
        IReadOnlyList<OtherObject>? others = null,
        int workers = 4,
        IBaseFacts? bases = null,
        IAlsoRemoveFollowUp? followUp = null,
        Func<TimeSpan>? timeSinceStart = null,
        Func<IReadOnlyList<TargetObject>, IVisibleTargetObjects>? surroundings = null,
        IReadOnlySet<VisibleObjectKind>? alwaysKept = null)
    {
        var caches = new TestRunCaches(Shapes, bases: bases ?? UnresolvedBases, visibleTargets: surroundings ?? Scene(targets, others ?? []).IndexVisibleTargets);
        return VisibleLeftBehindObjects.Find(
            new(
                targets,
                decisions,
                caches,
                LeftBehindRules.CreateVisibleKindsAlwaysKept(caches, new NeverRemovedVisibleKinds(alwaysKept ?? new HashSet<VisibleObjectKind>())),
                LeftBehindRules.CreateSurroundingsJudging(),
                LeftBehindRules.CreateLeftBehindRoundJoin(),
                followUp ?? new ScriptedFollowUp(),
                timeSinceStart ?? (() => TimeSpan.Zero),
                TargetWorkOrder.Of(targets),
                new Execution(workers),
                settings));
    }

    private static IEnumerable<(int Target, string Summary)> Summarize(VisibleLeftBehindResult result) =>
        result.Evaluations.Select(check => (check.TargetIndex, Summary(check)));

    private static string Summary(VisibleLeftBehindCheck check) =>
        $"{check.Decision} {check.Surroundings.OccupiedCount} {check.Surroundings.RemovedCount} {Fingerprint(check.Surroundings)}";

    private static string Fingerprint(SectorAreas areas) =>
        $"{Bits(areas.TotalAreas)}|{Bits(areas.RemovedAreas)}|{Bits(areas.RestoredAreas)}|{string.Join(',', areas.States)}";

    private static string Bits(IEnumerable<float> values) => string.Join(',', values.Select(value => BitConverter.SingleToInt32Bits(value).ToString("X8")));

    private static int[][] ProposedTargets(VisibleLeftBehindResult result) =>
        [.. result.ProposedRounds.Select(round => round.Proposals.Select(proposal => proposal.Target.Index).ToArray())];

    private static VisibleLeftBehindCheck Evaluation(VisibleLeftBehindResult result, int targetIndex) =>
        result.Evaluations.Single(check => check.TargetIndex == targetIndex);

    private static IEnumerable<string> DescribeRounds(IEnumerable<ProposedRound> rounds) =>
        rounds.Select(round => $"{round.Opening.Kind}:{string.Join(',', round.Proposals.Select(proposal => proposal.Target.Index))}");

    private sealed class KindsOfBases(IReadOnlyDictionary<RecordKey, BaseRecordKind> kinds) : IBaseFacts
    {
        public BaseFacts Of(BaseKey baseKey) =>
            BaseFacts.Unresolved(baseKey.Record) with { Resolved = true, Kind = kinds.GetValueOrDefault(baseKey.Record, BaseRecordKind.Other) };
    }

    /// <summary>
    /// A follow-up that proposes, as also-remove rounds, the targets its script gives for the seeds it is asked about (none without
    /// a script), or fails with <paramref name="failure"/>. Its summary is a switched-off result whose rounds are those of the
    /// decisions it was summarized from.
    /// </summary>
    private sealed class ScriptedFollowUp(Func<int[], int[][]>? script = null, PartFailure? failure = null) : IAlsoRemoveFollowUp
    {
        /// <summary>The seeds of each call, in call order.</summary>
        public List<int[]> Seeds { get; } = [];

        public FollowUpPass FollowUp(IRemovalDecisions decided, ImmutableArray<TargetId> seeds)
        {
            int[] seedIndexes = [.. seeds.Select(seed => seed.Index)];
            Seeds.Add(seedIndexes);
            if (failure is not null) return new FollowUpPass([], failure);

            return new FollowUpPass([.. (script?.Invoke(seedIndexes) ?? []).Select(Touching)], Failure: null);
        }

        public RestingObjectsResult Summarize(IRemovalDecisions final) => TestRestingObjects.SwitchedOff with { Rounds = final.Rounds };

        private static IReadOnlyList<ProposedRemoval> Touching(int[] targets) =>
            [.. targets.Select(target => new ProposedRemoval(new TargetId(target), new TouchingTargetThatLeft(new TargetId(1))))];
    }

    /// <summary>A clock one reading step further on each reading, which the tests also move on by hand.</summary>
    private sealed class StepClock
    {
        public static readonly TimeSpan ReadingStep = TimeSpan.FromSeconds(1);

        private TimeSpan _now;

        public TimeSpan Read() => _now += ReadingStep;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class FailingSurroundings(IVisibleTargetObjects inner) : IVisibleTargetObjects
    {
        public IReadOnlyList<RecordKey> Spaces => inner.Spaces;

        public void Around(RecordKey space, Vector3 point, float radius, List<VisibleTargetNear> into) => throw new PlantedUserDataException("it broke.");

        public void InColumn(RecordKey space, Vector2 point, float radius, List<VisibleTargetNear> into) => inner.InColumn(space, point, radius, into);

        public bool AnyContains(RecordKey space, Vector3 point, Func<TargetId, bool> include, ObjectQueryScratch scratch) => inner.AnyContains(space, point, include, scratch);

        public IEnumerable<OrientedBox> BoxesNear(RecordKey space, Vector3 point, float radius, Func<TargetId, bool> include) =>
            inner.BoxesNear(space, point, radius, include);
    }
}
