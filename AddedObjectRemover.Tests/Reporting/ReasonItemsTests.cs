using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Reporting;

public sealed class ReasonItemsTests
{
    private const int TargetCount = 4;

    private static readonly TooCloseCause BySize = new TooCloseCause.ObjectSize();

    [Fact]
    public void EveryReasonIsCountedUnderItsOwnItem()
    {
        var surroundings = new SectorAreaTally(50).Build();
        ObjectWithReason[] objects =
        [
            new ObjectWithReason.TerrainChanged(0, new PluginName("Other.esp"), TerrainChangeKind.Height),
            new ObjectWithReason.TooClose(1, default, BySize),
            new ObjectWithReason.TooClose(2, default, BySize),
            new ObjectWithReason.Touching(3, 0),
            new ObjectWithReason.LostSupport(4, 0.5f, 0f, 0),
            new ObjectWithReason.TouchingAnotherModsObject(5, default),
            new ObjectWithReason.LostSupportWithAnotherModsObject(6, 0.5f, 0f, default),
            new ObjectWithReason.VisibleLeftBehind(7, new VisibleLeftBehindCheck(7, null, surroundings, VisibleLeftBehindOutcome.RemovedSurroundingsRemoved, KeepReason: null)),
            new ObjectWithReason.LeftBehind(8, new LeftBehindCheck(8, InvisibleObjectKind.XMarkers, 100f, null, surroundings, LeftBehindOutcome.RemovedSurroundingsRemoved, KeepReason: null)),
            new ObjectWithReason.LinkedTo(9, 0),
            new ObjectWithReason.LinkedTo(10, 0),
            new ObjectWithReason.LinkedTo(11, 0),
        ];

        Assert.Equal(
            "1 on changed terrain, 2 too close, 4 follow-up removals, 1 visible objects left behind, 1 invisible objects left behind, 3 linked to removed objects",
            ReasonItems.Describe(objects, StartDecisions(), StepOutcome.Performed, StepOutcome.Performed, ReasonItems.ForRemovals));
    }

    [Fact]
    public void ARestoreSummaryNamesTheFollowUpAndLinkedItemsWithoutTheWordRemoved()
    {
        ObjectWithReason[] objects = [new ObjectWithReason.Touching(3, 0), new ObjectWithReason.LinkedTo(9, 0)];

        Assert.Equal(
            "0 too close, 1 follow-up, 0 invisible objects left behind, 1 linked",
            ReasonItems.Describe(objects, StartDecisions(), StepOutcome.SwitchedOff, StepOutcome.SwitchedOff, ReasonItems.ForRestorations));
    }

    [Fact]
    public void ARemovedObjectLinkedToARestoredOneIsCountedUnderItsOwnItemAndNotAsLinkedToARemovedOne()
    {
        var targets = TestTargets.CreateMany(TargetCount);
        var protection = ObjectsToKeep.Build(targets, [], TestTargets.References(TargetCount), TestBaseFacts.Unresolved);
        var restore = new WhenRemoved.Restore(TestTargets.EarlierVersionAt(new PluginName("Earlier.esp"), targets[0].Placement));
        var decisions = RemovalDecisions.Start(protection, [restore, new WhenRemoved.Disable(), new WhenRemoved.Disable(), new WhenRemoved.Disable()])
            .Apply(RoundKind.TooClose, [Propose(0, new RemovalReason.TooClose(new OtherId(0), BySize)), Propose(3, new RemovalReason.TooClose(new OtherId(0), BySize))]);
        ObjectWithReason[] objects = [new ObjectWithReason.LinkedTo(1, 0), new ObjectWithReason.LinkedTo(2, 3)];

        Assert.Equal(
            "0 too close, 0 follow-up removals, 0 invisible objects left behind, 1 linked to removed objects, 1 linked to restored objects",
            ReasonItems.Describe(objects, decisions, StepOutcome.SwitchedOff, StepOutcome.SwitchedOff, ReasonItems.ForRemovals));
    }

    [Theory]
    [InlineData(StepOutcome.SwitchedOff)]
    [InlineData(StepOutcome.StoppedByUnexpectedError)]
    public void TheItemOfAStepThatWasNotPerformedIsLeftOut(StepOutcome notPerformed)
    {
        var surroundings = new SectorAreaTally(50).Build();
        ObjectWithReason[] objects =
        [
            new ObjectWithReason.TerrainChanged(0, new PluginName("Other.esp"), TerrainChangeKind.Height),
            new ObjectWithReason.VisibleLeftBehind(1, new VisibleLeftBehindCheck(1, null, surroundings, VisibleLeftBehindOutcome.RemovedSurroundingsRemoved, KeepReason: null)),
        ];

        Assert.Equal(
            "0 too close, 0 follow-up removals, 0 invisible objects left behind, 0 linked to removed objects",
            ReasonItems.Describe(objects, StartDecisions(), notPerformed, notPerformed, ReasonItems.ForRemovals));
    }

    [Fact]
    public void NoObjectsGiveZeroCountsForTheItemsEveryRunShows()
    {
        Assert.Equal(
            "0 too close, 0 follow-up removals, 0 invisible objects left behind, 0 linked to removed objects",
            ReasonItems.Describe([], StartDecisions(), StepOutcome.SwitchedOff, StepOutcome.SwitchedOff, ReasonItems.ForRemovals));
    }

    [Fact]
    public void AFollowUpFirstShrunkOrTurnedIsCountedAmongTheFollowUps()
    {
        var decisions = StartDecisions()
            .ApplyFits([new ProposedFit(new TargetId(1), new RemovalReason.TooClose(new OtherId(0), BySize), new EulerPlacement(Vector3.Zero, Vector3.Zero, 0.8f))])
            .Apply(RoundKind.TooClose, [Propose(0, new RemovalReason.TooClose(new OtherId(0), BySize))])
            .Apply(RoundKind.AlsoRemove, [Propose(1, new RemovalReason.Touching(new TargetId(0)))]);
        ObjectWithReason[] objects = [new ObjectWithReason.Touching(1, 0), new ObjectWithReason.LostSupport(2, 0.5f, 0f, 0)];

        Assert.Equal(
            "0 too close, 2 follow-up removals (1 of them first shrunk or turned), 0 invisible objects left behind, 0 linked to removed objects",
            ReasonItems.Describe(objects, decisions, StepOutcome.SwitchedOff, StepOutcome.SwitchedOff, ReasonItems.ForRemovals));
    }

    private static RemovalDecisions StartDecisions() =>
        TestSeededDecisions.StartDisablingAll(ObjectsToKeep.Build(TestTargets.CreateMany(TargetCount), [], TestTargets.References(TargetCount), TestBaseFacts.Unresolved), TargetCount);

    private static ProposedRemoval Propose(int index, RemovalReason reason) => new(new TargetId(index), reason);
}
