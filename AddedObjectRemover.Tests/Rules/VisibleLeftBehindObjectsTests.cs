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
    private const float FartherOut = 250f;
    private const int RandomSeed = 3141;
    private const int RandomTrials = 12;
    private const int RandomTargetCount = 60;
    private const int RandomLinkCount = 6;
    private static readonly TimeSpan FollowUpDuration = TimeSpan.FromSeconds(100);
    private const float RandomReach = 400f;
    private const int ReferencedOneIn = 8;
    private const int RestoredOneIn = 4;
    private const int RemovedEarlierOneIn = 3;
    private const float TouchGap = 1f;
    private const float SupportLostFraction = 0.5f;

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

    /// <summary>Loose enough that removals in random worlds clear further objects, round after round.</summary>
    private static readonly VisibleLeftBehindSettings LooseSettings = Settings with { MinOccupiedDirections = 3, DirectionClearedPercent = 50, ClearedDirectionsPercent = 50 };

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
        var proposal = Assert.Single(Proposals(result));
        Assert.Equal(new TargetId(0), proposal.Target);
        Assert.IsType<RemovalReason.VisibleObjectSurroundingsRemoved>(proposal.Reason);
    }

    [Fact]
    public void APerformedStepProposesVisibleLeftBehindRoundsAndNamesThemAsTheDecisionsNumberThem()
    {
        var decisions = CascadeDecisions();

        var result = Find(CascadeTargets, decisions, Settings);

        Assert.Equal(3, result.ProposedRounds.Length);
        Assert.All(result.ProposedRounds, round => Assert.Equal(RoundKind.VisibleLeftBehind, round.Kind));
        Assert.Equal(decisions.ApplyRounds(result.ProposedRounds).RoundsAfter(decisions).ToArray(), result.Rounds.ToArray());
    }

    [Fact]
    public void FiveOccupiedDirectionsKeepTheObjectEvenWhenAllOfThemWereCleared()
    {
        var targets = Ring(SixDirections.Take(5));

        var result = Find(targets, DecisionsRemoving(targets, 1, 2, 3, 4, 5), Settings);

        var evaluation = Assert.Single(result.Evaluations);
        Assert.Equal(VisibleLeftBehindOutcome.KeptTooFewSurroundingObjects, evaluation.Decision);
        Assert.Empty(Proposals(result));
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
        Assert.DoesNotContain(Proposals(result), proposal => proposal.Target.Index == 0);
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
        Assert.Empty(Proposals(result));
    }

    [Fact]
    public void AnObjectAnEarlierStepKeptIsNotEvaluatedAgain()
    {
        var targets = Ring(SixDirections);
        var protection = ObjectsToKeep.Build(targets, [], References(targets.Length, [0]), UnresolvedBases);
        var decisions = TestSeededDecisions.StartDisablingAll(protection, targets.Length)
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
        var decisions = TestSeededDecisions.StartDisablingAll(protection, targets.Length).Apply(RoundKind.TooClose, [.. new[] { 1, 2, 3, 4, 5, 6 }.Select(TooClose)]);

        var result = Find(targets, decisions, Settings);

        var evaluation = Assert.Single(result.Evaluations);
        Assert.Equal(VisibleLeftBehindOutcome.KeptReferenced, evaluation.Decision);
        Assert.Equal(TestKeepReasons.Quest.Kind, evaluation.KeepReason?.Kind);
        Assert.False(evaluation.IsRemoved);
        Assert.Equal(new TargetId(0), Assert.Single(Proposals(result)).Target);
    }

    [Fact]
    public void AMemberOfTheRemovedObjectsLinkedGroupIsRemovedWithItWhenTheRoundIsApplied()
    {
        var partner = 7;
        var targets = Ring(SixDirections).Add(Visible(partner, new Vector3(5000, 0, 0)));
        var protection = ObjectsToKeep.Build(targets, [TestTargets.Link(0, partner)], References(targets.Length, []), UnresolvedBases);
        var decisions = TestSeededDecisions.StartDisablingAll(protection, targets.Length).Apply(RoundKind.TooClose, [.. new[] { 1, 2, 3, 4, 5, 6 }.Select(TooClose)]);

        var result = Find(targets, decisions, Settings);
        var applied = decisions.ApplyRounds(result.ProposedRounds);

        var linked = Assert.IsType<Decision.Removed>(applied.Of(new TargetId(partner)));
        Assert.Equal(new RemovalReason.LinkedTo(new TargetId(0)), linked.Reason);
    }

    [Fact]
    public void AKeptObjectThatTheRoundRemovesAsALinkedGroupMemberIsEvaluatedAsRemovedLinked()
    {
        var partner = 7;
        var targets = Ring(SixDirections).Add(Visible(partner, new Vector3(5000, 0, 0)));
        var protection = ObjectsToKeep.Build(targets, [TestTargets.Link(0, partner)], References(targets.Length, []), UnresolvedBases);
        var decisions = TestSeededDecisions.StartDisablingAll(protection, targets.Length).Apply(RoundKind.TooClose, [.. new[] { 1, 2, 3, 4, 5, 6 }.Select(TooClose)]);

        var result = Find(targets, decisions, Settings);

        var member = result.Evaluations.Single(check => check.TargetIndex == partner);
        Assert.Equal(VisibleLeftBehindOutcome.RemovedLinkedToRemoval, member.Decision);
        Assert.True(member.IsRemoved);
        Assert.Equal(new TargetId(0), Assert.Single(Proposals(result)).Target);
    }

    [Fact]
    public void AnObjectTheRoundRestoresIsEvaluatedAsRestoredAndItsLinkedGroupMemberAsRestoredLinkedNeverAsRemoved()
    {
        var partner = 7;
        var targets = Ring(SixDirections).Add(Visible(partner, new Vector3(5000, 0, 0)));
        var protection = ObjectsToKeep.Build(targets, [TestTargets.Link(0, partner)], References(targets.Length, []), UnresolvedBases);
        var restore = new WhenRemoved.Restore(TestTargets.EarlierVersionAt(new PluginName("Earlier.esp"), targets[0].Placement));
        var decisions = RemovalDecisions.Start(protection, [restore, .. Enumerable.Repeat<WhenRemoved>(new WhenRemoved.Disable(), 6), restore])
            .Apply(RoundKind.TooClose, [.. new[] { 1, 2, 3, 4, 5, 6 }.Select(TooClose)]);

        var result = Find(targets, decisions, Settings);

        var restored = result.Evaluations.Single(check => check.TargetIndex == 0);
        var member = result.Evaluations.Single(check => check.TargetIndex == partner);
        Assert.Equal((VisibleLeftBehindOutcome.RestoredSurroundingsRemoved, true, false), (restored.Decision, restored.IsRestored, restored.IsRemoved));
        Assert.Equal((VisibleLeftBehindOutcome.RestoredAsLinked, true, false), (member.Decision, member.IsRestored, member.IsRemoved));
    }

    [Theory]
    [InlineData(false, VisibleLeftBehindOutcome.RemovedSurroundingsRemovedAndRestored)]
    [InlineData(true, VisibleLeftBehindOutcome.RestoredSurroundingsRemovedAndRestored)]
    public void ARestoredNeighbourGivesTheReasonSurroundingsRemovedAndRestoredWhateverBecomesOfTheObject(bool objectIsRestored, VisibleLeftBehindOutcome expected)
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
    public void ALinkedGroupMemberTheRoundRemovesAlongWithARestoredObjectIsEvaluatedAsRemovedLinkedToARestoredObject()
    {
        var partner = 7;
        var targets = Ring(SixDirections).Add(Visible(partner, new Vector3(5000, 0, 0)));
        var protection = ObjectsToKeep.Build(targets, [TestTargets.Link(0, partner)], References(targets.Length, []), UnresolvedBases);
        var restore = new WhenRemoved.Restore(TestTargets.EarlierVersionAt(new PluginName("Earlier.esp"), targets[0].Placement));
        var decisions = RemovalDecisions.Start(protection, [restore, .. Enumerable.Repeat<WhenRemoved>(new WhenRemoved.Disable(), 7)])
            .Apply(RoundKind.TooClose, [.. new[] { 1, 2, 3, 4, 5, 6 }.Select(TooClose)]);

        var result = Find(targets, decisions, Settings);

        var member = result.Evaluations.Single(check => check.TargetIndex == partner);
        Assert.Equal((VisibleLeftBehindOutcome.RemovedLinkedToRestoration, true), (member.Decision, member.IsRemoved));
    }

    [Fact]
    public void ANeighbourKeptShrunkByAnEarlierStepIsMeasuredAtItsNewSize()
    {
        var targets = Ring(SixDirections);
        var neighbour = targets[6];
        var decisions = DecisionsRemoving(targets, 1, 2, 3, 4, 5)
            .ApplyFits([new ProposedFit(neighbour.Id, new RemovalReason.TooClose(new OtherId(0), new TooCloseCause.ObjectSize()), neighbour.Placement with { Scale = 0.5f })]);
        var shrunkSector = DirectionSector.SouthWest;

        var shrunk = Assert.Single(FindAsTheRoundsMeasureThem(targets, decisions).Evaluations).Surroundings;
        var unchanged = Assert.Single(Find(targets, decisions, Settings).Evaluations).Surroundings;

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
        Assert.Equal(VisibleLeftBehindOutcome.RemovedSurroundingsRemoved, Evaluation(result, 0).Decision);
        Assert.Equal(7, Evaluation(result, 0).Surroundings.RemovedCount);
        Assert.Equal(VisibleLeftBehindOutcome.RemovedSurroundingsRemoved, Evaluation(result, 1).Decision);
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
        Assert.Equal(VisibleLeftBehindOutcome.KeptSurroundingsMostlyKept, Evaluation(result, 0).Decision);
        Assert.Equal(6, Evaluation(result, 0).Surroundings.RemovedCount);
    }

    /// <summary>
    /// Object 0 has three of its six directions removed earlier; objects 1 to its east and 3 to its west each have their own removed
    /// surroundings, so the first round proposes both. Object 0 is cleared only when both of them leave their place.
    /// </summary>
    [Theory]
    [InlineData(false, VisibleLeftBehindOutcome.RemovedSurroundingsRemoved, 5)]
    [InlineData(true, VisibleLeftBehindOutcome.KeptSurroundingsMostlyKept, 4)]
    public void AnObjectARoundKeepsAsReferencedClearsNothingInTheNextRound(bool westIsReferenced, VisibleLeftBehindOutcome expected, int removedDirections)
    {
        var targets = TwoSidedLayout();
        var protection = ObjectsToKeep.Build(targets, [], References(targets.Length, westIsReferenced ? [West] : []), UnresolvedBases);
        var decisions = TestSeededDecisions.StartDisablingAll(protection, targets.Length)
            .Apply(RoundKind.TooClose, [.. new[] { 2, 5, 6 }.Concat(Enumerable.Range(7, AwayFromTheWest.Length + AwayFromTheEast.Length)).Select(TooClose)]);

        var result = Find(targets, decisions, Settings);

        Assert.Equal(new[] { 1, West }, ProposedTargets(result)[0]);
        Assert.Equal(westIsReferenced ? VisibleLeftBehindOutcome.KeptReferenced : VisibleLeftBehindOutcome.RemovedSurroundingsRemoved, Evaluation(result, West).Decision);
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
        Assert.Equal(VisibleLeftBehindOutcome.RemovedLinkedToRemoval, Evaluation(result, 1).Decision);
        Assert.Equal(VisibleLeftBehindOutcome.RemovedSurroundingsRemoved, Evaluation(result, 0).Decision);
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
        Assert.Equal(VisibleLeftBehindOutcome.RestoredSurroundingsRemoved, Evaluation(result, 1).Decision);
        Assert.Equal(VisibleLeftBehindOutcome.RemovedSurroundingsRemovedAndRestored, Evaluation(result, 0).Decision);
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

    /// <remarks>
    /// The reference ends with a visible round that judges every undecided object and takes nothing; the step runs no round when no
    /// object's surroundings changed, so the rounds are compared without the visible rounds that propose nothing.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(8)]
    public void TheRoundsEqualAReferenceThatMeasuresEveryObjectAgainInEveryRoundAndRunsTheAlsoRemoveStepAfterIt(int workers)
    {
        var (cascades, followUpRemovals) = (0, 0);
        foreach (var world in RandomWorlds())
        {
            var reference = FindByMeasuringEverythingAgain(world);

            var result = Find(world.Targets, world.Start, LooseSettings, workers: workers, followUp: world.CreateFollowUp(workers));

            var final = Applied(world.Start, result.ProposedRounds);
            Assert.Equal(reference.Proposed.Where(ProposesSomething), DescribeRounds(result.ProposedRounds).Where(ProposesSomething));
            Assert.Equal(reference.Final.All().ToList(), final.All().ToList());
            Assert.Equal(reference.Checks.Select(check => Compared(check, final)), result.Evaluations.Select(check => Compared(check, final)));
            Assert.Equal(final.RoundsAfter(world.Start).Where(round => round.Kind == RoundKind.AlsoRemove), result.FollowUp.Rounds.AsEnumerable());
            if (result.Rounds.Count(round => !final.LeftIn(round).IsEmpty) > 1) cascades++;
            followUpRemovals += result.FollowUp.Rounds.Sum(round => final.LeftIn(round).Length);
        }
        Assert.True(cascades > 0);
        Assert.True(followUpRemovals > 0);
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
        Assert.Empty(Proposals(ticked));
        Assert.Equal(VisibleLeftBehindOutcome.RemovedSurroundingsRemoved, Assert.Single(unticked.Evaluations).Decision);
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
    public void TheStepDecidesItsRoundsOnACopyAndLeavesTheGivenDecisionsAsTheyWere()
    {
        var decisions = CascadeDecisions();
        var roundsBefore = decisions.Rounds.Length;

        var result = Find(CascadeTargets, decisions, Settings);

        Assert.NotEmpty(Proposals(result));
        Assert.Equal(roundsBefore, decisions.Rounds.Length);
        Assert.False(decisions.IsDecided(new TargetId(0)));
    }

    [Fact]
    public void AFailureInsideTheStepStopsItWithTheFailureAndNoRounds()
    {
        var targets = Ring(SixDirections);
        var decisions = DecisionsRemoving(targets, 1, 2, 3, 4, 5, 6);

        var result = VisibleLeftBehindObjects.Find(
            targets,
            decisions,
            new FailingSurroundings(Scene(targets, []).VisibleTargets),
            UnresolvedBases,
            Shapes,
            new ScriptedFollowUp(),
            () => TimeSpan.Zero,
            TargetWorkOrder.Of(targets),
            new Execution(Environment.ProcessorCount),
            Settings);

        Assert.Equal(StepOutcome.StoppedByUnexpectedError, result.Outcome);
        Assert.Equal(
            new PartFailure(VisibleLeftBehindParts.VisibleLeftBehind, "InvalidOperationException: it broke", Subject: null), result.Failure! with { Details = null });
        Assert.StartsWith("System.InvalidOperationException: it broke.", result.Failure.Details, StringComparison.Ordinal);
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

        var result = Find(CascadeTargets, decisions, Settings, followUp: followUp);

        var final = decisions.ApplyRounds(result.ProposedRounds);
        Assert.Equal(
            new[] { "VisibleLeftBehind:1", "AlsoRemove:5", "AlsoRemove:", "VisibleLeftBehind:0", "VisibleLeftBehind:" }, DescribeRounds(result.ProposedRounds));
        Assert.Equal(new[] { new[] { 1 }, [0] }, followUp.Seeds);
        Assert.Equal(final.RoundsAfter(decisions).Where(round => round.Kind == RoundKind.VisibleLeftBehind), result.Rounds.AsEnumerable());
        Assert.Equal(VisibleLeftBehindOutcome.RemovedSurroundingsRemoved, Evaluation(result, 0).Decision);
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

        var result = Find(CascadeTargets, decisions, Settings, followUp: new ScriptedFollowUp(seeds => seeds.SequenceEqual([1]) ? [[NorthWest], []] : []));

        var northWest = decisions.ApplyRounds(result.ProposedRounds).Of(new TargetId(NorthWest))!;
        Assert.Equal(RoundKind.AlsoRemove, northWest.Round.Kind);
        Assert.Equal(northWestIsReferenced, northWest is Decision.Kept);
        Assert.Equal(VisibleLeftBehindOutcome.KeptTooFewSurroundingObjects, Evaluation(result, NorthWest).Decision);
        Assert.Null(Evaluation(result, NorthWest).KeepReason);
        Assert.Equal(northWestIsReferenced ? VisibleLeftBehindOutcome.KeptSurroundingsMostlyKept : VisibleLeftBehindOutcome.RemovedSurroundingsRemoved, Evaluation(result, 0).Decision);
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
        TestSeededDecisions.StartDisablingAll(ObjectsToKeep.Build(targets, [], TestTargets.References(targets.Length), UnresolvedBases), targets.Length)
            .Apply(RoundKind.TooClose, [.. removed.Select(TooClose)]);

    private static IReadOnlyList<ProposedRemoval> Proposals(VisibleLeftBehindResult result) =>
        [.. result.ProposedRounds.SelectMany(round => round.Proposals)];

    private static ObjectCaches Scene(ImmutableArray<TargetObject> targets, IReadOnlyList<OtherObject> others) => TestScenes.Create(targets, others, Shapes);

    /// <param name="followUp">By default one that proposes nothing.</param>
    private static VisibleLeftBehindResult Find(
        ImmutableArray<TargetObject> targets,
        IRemovalDecisions decisions,
        VisibleLeftBehindSettings? settings,
        IReadOnlyList<OtherObject>? others = null,
        int workers = 4,
        IBaseFacts? bases = null,
        IAlsoRemoveFollowUp? followUp = null,
        Func<TimeSpan>? timeSinceStart = null) =>
        VisibleLeftBehindObjects.Find(
            targets,
            decisions,
            Scene(targets, others ?? []).VisibleTargets,
            bases ?? UnresolvedBases,
            Shapes,
            followUp ?? new ScriptedFollowUp(),
            timeSinceStart ?? (() => TimeSpan.Zero),
            TargetWorkOrder.Of(targets),
            new Execution(workers),
            settings);

    private static VisibleLeftBehindResult FindAsTheRoundsMeasureThem(ImmutableArray<TargetObject> targets, IRemovalDecisions decisions) =>
        VisibleLeftBehindObjects.Find(
            targets,
            decisions,
            ClashingObjectFitter.IndexAsTheRoundsMeasureThem(targets, decisions, Scene(targets, [])),
            UnresolvedBases,
            Shapes,
            new ScriptedFollowUp(),
            () => TimeSpan.Zero,
            TargetWorkOrder.Of(targets),
            new Execution(Environment.ProcessorCount),
            Settings);

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

    /// <summary>A check's target, kind and surroundings, and its verdict when no round decided it.</summary>
    private static string Compared(VisibleLeftBehindCheck check, IRemovalDecisions final) =>
        $"{check.TargetIndex} {check.Kind} {Fingerprint(check.Surroundings)} {(final.IsDecided(new TargetId(check.TargetIndex)) ? (VisibleLeftBehindOutcome?)null : check.Decision)}";

    private static IRemovalDecisions Applied(IRemovalDecisions start, ImmutableArray<ProposedRound> rounds)
    {
        var copy = start.WorkingCopy();
        var applied = start;
        foreach (var round in rounds) applied = copy.DecideRound(round.Kind, round.Proposals);
        return applied;
    }

    /// <param name="Start">The decisions of the earlier steps.</param>
    private sealed record RandomWorld(ImmutableArray<TargetObject> Targets, RemovalDecisions Start)
    {
        private static readonly AlsoRemoveSettings AlsoRemove = new(FollowUpRemovalMode.EverythingTouchingIt, TouchGap, SupportLostFraction, TouchChainsShown: false);

        private readonly TriangleStore _triangles = new(Shapes.ReadTriangles);

        public IAlsoRemoveFollowUp CreateFollowUp(int workers) =>
            RestingObjects.CreateFollowUp(
                Targets, AlsoRemove, new Execution(workers), () => TimeSpan.Zero, Shapes, _triangles, TestSurfaceSamples.Over(_triangles), TestGround.NoTerrain(), ObjectsOfAnyPlugin);

        /// <summary>The also-remove step seeded by the newest round of <paramref name="decisions"/>, without report files.</summary>
        public RestingObjectsResult FindAlsoRemoveRounds(IRemovalDecisions decisions) =>
            RestingObjects.Find(
                new RestingObjectsInput(Targets, decisions, [decisions.Rounds[^1]], AlsoRemove, new Execution(1), () => TimeSpan.Zero, StepOutcome.SwitchedOff),
                UnresolvedBases,
                Shapes,
                _triangles,
                TestSurfaceSamples.Over(_triangles),
                TestGround.NoTerrain(),
                ObjectsOfAnyPlugin);

        private IVisibleObjectsOfAnyPlugin ObjectsOfAnyPlugin() => Scene(Targets, []).VisibleObjectsOfAnyPlugin();
    }

    /// <param name="Proposed">Each round's kind and proposed targets (see <see cref="DescribeRound"/>).</param>
    /// <param name="Checks">Each candidate's check in the newest round that judged it, in target order.</param>
    private sealed record ReferenceRounds(List<string> Proposed, IRemovalDecisions Final, IEnumerable<VisibleLeftBehindCheck> Checks);

    private static IEnumerable<RandomWorld> RandomWorlds() => Enumerable.Range(0, RandomTrials).Select(trial => CreateRandomWorld(new Random(RandomSeed + trial)));

    private static RandomWorld CreateRandomWorld(Random random)
    {
        ImmutableArray<TargetObject> targets =
        [
            .. Enumerable.Range(0, RandomTargetCount).Select(index => TestTargets.Create(
                index,
                TestTargets.At(new Vector3(RandomOffset(random), RandomOffset(random), 0), random.NextSingle() * MathF.Tau),
                Block.Base,
                TestTargets.Space)),
        ];
        var referenced = Enumerable.Range(0, RandomTargetCount).Where(_ => random.Next(ReferencedOneIn) == 0).ToArray();
        TargetLink[] links = [.. Enumerable.Range(0, RandomLinkCount).Select(_ => random.Next(RandomTargetCount - 1)).Select(from => TestTargets.Link(from, from + 1))];
        var protection = ObjectsToKeep.Build(targets, links, References(RandomTargetCount, referenced), UnresolvedBases);
        var start = RemovalDecisions.Start(protection, [.. targets.Select(target => random.Next(RestoredOneIn) == 0 ? Restore(target) : new WhenRemoved.Disable())])
            .Apply(RoundKind.TooClose, [.. Enumerable.Range(0, RandomTargetCount).Where(_ => random.Next(RemovedEarlierOneIn) == 0).Select(TooClose)]);
        return new RandomWorld(targets, start);
    }

    private static float RandomOffset(Random random) => (random.NextSingle() * 2 - 1) * RandomReach;

    private static WhenRemoved Restore(TargetObject target) =>
        new WhenRemoved.Restore(TestTargets.EarlierVersionAt(new PluginName("Earlier.esp"), target.Placement));

    /// <summary>
    /// Each visible round measures every still-undecided candidate in full against the view before the round, until a round takes
    /// nothing from its place. After each round that took something, the also-remove step seeded by that round runs from the view, and
    /// its rounds are decided before the next visible round.
    /// </summary>
    private static ReferenceRounds FindByMeasuringEverythingAgain(RandomWorld world)
    {
        var index = Scene(world.Targets, []).VisibleTargets;
        var rule = new VisibleLeftBehindRule(world.Targets, Shapes, UnresolvedBases, LooseSettings);
        var candidates = rule.Candidates(world.Start);
        var copy = world.Start.WorkingCopy();
        IRemovalDecisions view = world.Start;
        var checks = new SortedDictionary<int, VisibleLeftBehindCheck>();
        var proposed = new List<string>();
        bool tookSomething;
        do
        {
            var round = candidates.Where(candidate => !view.IsDecided(new TargetId(candidate)))
                .Select(candidate => rule.Judge(new MeasuredSurroundings(candidate, MeasureFully(world.Targets[candidate], index, view))))
                .ToArray();
            foreach (var check in round) checks[check.TargetIndex] = check;
            var proposals = VisibleLeftBehindProposals.ProposalsOf(round);
            proposed.Add(DescribeRound(RoundKind.VisibleLeftBehind, proposals));
            view = copy.DecideRound(RoundKind.VisibleLeftBehind, proposals);
            tookSomething = !view.LeftIn(view.Rounds[^1]).IsEmpty;
            if (!tookSomething) continue;

            foreach (var alsoRemove in world.FindAlsoRemoveRounds(view).ProposedRounds)
            {
                proposed.Add(DescribeRound(RoundKind.AlsoRemove, alsoRemove));
                view = copy.DecideRound(RoundKind.AlsoRemove, alsoRemove);
            }
        }
        while (tookSomething);
        return new ReferenceRounds(proposed, view, checks.Values);
    }

    private static IEnumerable<string> DescribeRounds(IEnumerable<ProposedRound> rounds) => rounds.Select(round => DescribeRound(round.Kind, round.Proposals));

    private static string DescribeRound(RoundKind kind, IEnumerable<ProposedRemoval> proposals) =>
        $"{kind}:{string.Join(',', proposals.Select(proposal => proposal.Target.Index))}";

    private static bool ProposesSomething(string round) => !round.EndsWith(':');

    private static SectorAreas MeasureFully(TargetObject target, IVisibleTargetObjects index, IRemovalDecisions decisions)
    {
        var neighbours = new List<VisibleTargetNear>();
        index.Around(target.SpaceKey, target.Transform.Position, LooseSettings.LookAround, neighbours);
        return SectorAreaTally.Measure(
            neighbours.Where(neighbour => neighbour.Target != target.Id).Select(neighbour => (neighbour.Box, NeighbourFates.Of(decisions, neighbour.Target))),
            target.Transform.Position,
            LooseSettings.DirectionClearedPercent);
    }

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
            [.. targets.Select(target => new ProposedRemoval(new TargetId(target), new RemovalReason.Touching(new TargetId(1))))];
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

        public void Around(RecordKey space, Vector3 point, float radius, List<VisibleTargetNear> into) => throw new InvalidOperationException("it broke.");

        public bool AnyContains(RecordKey space, Vector3 point, Func<TargetId, bool> include, ObjectQueryScratch scratch) => inner.AnyContains(space, point, include, scratch);

        public IEnumerable<OrientedBox> BoxesNear(RecordKey space, Vector3 point, float radius, Func<TargetId, bool> include) =>
            inner.BoxesNear(space, point, radius, include);
    }
}
