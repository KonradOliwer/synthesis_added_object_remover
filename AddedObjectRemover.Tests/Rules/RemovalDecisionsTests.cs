using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>Scene: linked groups {0,1,2}, {3,4} and {5,6,7}; targets 8 to 11 are alone.</summary>
public class RemovalDecisionsTests
{
    private const int TargetCount = 12;
    private const int ShuffleSeed = 5;
    private const int ShuffleCount = 25;

    private static readonly KeepReason QuestReason = TestKeepReasons.Quest;

    private static readonly TargetLink[] Links =
    [
        TestTargets.Link(0, 1),
        TestTargets.Link(1, 2),
        TestTargets.Link(3, 4),
        TestTargets.Link(5, 6),
        TestTargets.Link(6, 7),
    ];

    private static readonly RemovalReason TooClose = new RemovalReason.TooClose(new OtherId(0), new TooCloseCause.ObjectSize());

    private static readonly EulerPlacement FitPlacement = new(Vector3.One, Vector3.Zero, 1f);

    private static readonly EarlierVersion Earlier = TestTargets.EarlierVersionAt(new PluginName("Earlier.esp"), new EulerPlacement(new Vector3(7, 8, 9), Vector3.Zero, 1f));

    [Fact]
    public void RoundsDecidedOnAWorkingCopyLeaveTheRunsDecisionsUnchanged()
    {
        var afterTooClose = Start().Apply(RoundKind.TooClose, [Propose(8, TooClose)]);
        var move = new MarkerMove(Id(11), Vector3.Zero, Vector3.One);
        var withMove = afterTooClose.AddMoves([move]);

        var (copy, _) = withMove.WorkingCopy().DecideRoundsUntilNothingRemoved(new ScriptedRounds([Propose(9, TooClose)], []), LastRoundRemovals(withMove));

        Assert.Equal(3, copy.Rounds.Length);
        Assert.True(copy.IsRemoved(Id(9)));
        Assert.Equal(new[] { move }, copy.Moves.ToArray());
        Assert.Single(withMove.Rounds);
        Assert.False(withMove.IsDecided(Id(9)));
        Assert.True(withMove.Apply(RoundKind.AlsoRemove, [Propose(10, TooClose)]).IsRemoved(Id(10)));
    }

    [Fact]
    public void AWorkingCopyHoldsOnlyWhatItsViewShows()
    {
        var first = Start().Apply(RoundKind.TooClose, [Propose(8, TooClose)]);
        first.Apply(RoundKind.AlsoRemove, [Propose(9, TooClose)]);

        var (copy, _) = first.WorkingCopy().DecideRoundsUntilNothingRemoved(new ScriptedRounds([Propose(10, TooClose)], []), LastRoundRemovals(first));

        Assert.False(copy.IsDecided(Id(9)));
        Assert.Equal(new[] { new Round(1, RoundKind.TooClose), new Round(2, RoundKind.AlsoRemove), new Round(3, RoundKind.AlsoRemove) }, copy.Rounds.ToArray());
    }

    /// <summary>Applying the copy's proposals as rounds gives the run the same decisions as the copy: linked members, kept objects and round numbers alike.</summary>
    [Fact]
    public void ApplyingTheRoundsOfAWorkingCopyGivesTheSameDecisionsAsTheCopy()
    {
        var touching = new RemovalReason.Touching(Id(0));
        var afterTooClose = Start(new Dictionary<int, KeepReason> { [4] = QuestReason }).Apply(RoundKind.TooClose, [Propose(0, TooClose)]);
        var rule = new ScriptedRounds([Propose(3, touching), Propose(5, touching)], [Propose(8, touching), Propose(6, touching)], []);

        var (copy, rounds) = afterTooClose.WorkingCopy().DecideRoundsUntilNothingRemoved(rule, LastRoundRemovals(afterTooClose));
        var applied = afterTooClose.ApplyRounds(RoundKind.AlsoRemove, [.. rounds.Select(round => round.Proposals)]);

        Assert.Equal(copy.Rounds.ToArray(), applied.Rounds.ToArray());
        Assert.Equal(copy.All().ToList(), applied.All().ToList());
        Assert.All(copy.Rounds, round =>
        {
            Assert.Equal(copy.RemovedIn(round).ToArray(), applied.RemovedIn(round).ToArray());
            Assert.Equal(copy.KeptIn(round).ToArray(), applied.KeptIn(round).ToArray());
        });
        Assert.IsType<Decision.Kept>(applied.Of(Id(3)));
        Assert.Equal(new RemovalReason.LinkedTo(Id(5)), Removed(applied, 7).Reason);
    }

    /// <summary>One round decided on a working copy is the round <see cref="RemovalDecisions.Apply"/> makes on the shared list, and leaves the shared list as it was.</summary>
    [Fact]
    public void DecidingOneRoundOnAWorkingCopyGivesTheSameDecisionsAsApplyOnTheSharedList()
    {
        var afterTooClose = Start(new Dictionary<int, KeepReason> { [4] = QuestReason }).Apply(RoundKind.TooClose, [Propose(8, TooClose)]);
        var proposals = new[] { Propose(3, TooClose), Propose(5, TooClose), Propose(10, TooClose) };

        var copy = afterTooClose.WorkingCopy().DecideRound(RoundKind.LeftBehind, proposals);
        var applied = afterTooClose.Apply(RoundKind.LeftBehind, proposals);

        Assert.Equal(applied.Rounds.ToArray(), copy.Rounds.ToArray());
        Assert.Equal(applied.All().ToList(), copy.All().ToList());
        Assert.Equal(applied.RemovedIn(applied.Rounds[^1]).ToArray(), copy.RemovedIn(copy.Rounds[^1]).ToArray());
        Assert.Equal(applied.KeptIn(applied.Rounds[^1]).ToArray(), copy.KeptIn(copy.Rounds[^1]).ToArray());
        Assert.IsType<Decision.Kept>(copy.Of(Id(3)));
        Assert.Equal(new RemovalReason.LinkedTo(Id(5)), Removed(copy, 7).Reason);
    }

    [Fact]
    public void ADecidedRoundOnAWorkingCopyLeavesTheSharedListUnchanged()
    {
        var afterTooClose = Start().Apply(RoundKind.TooClose, [Propose(8, TooClose)]);

        afterTooClose.WorkingCopy().DecideRound(RoundKind.LeftBehind, [Propose(9, TooClose)]);

        Assert.Single(afterTooClose.Rounds);
        Assert.False(afterTooClose.IsDecided(Id(9)));
    }

    [Fact]
    public void RemovedMemberTakesTheUndecidedMembersOfItsGroupAlong()
    {
        var decisions = Start().Apply(RoundKind.TooClose, [Propose(1, TooClose)]);

        Assert.Equal(new RemovalReason.LinkedTo(Id(1)), Removed(decisions, 0).Reason);
        Assert.Equal(new RemovalReason.LinkedTo(Id(1)), Removed(decisions, 2).Reason);
        Assert.Equal(TooClose, Removed(decisions, 1).Reason);
        Assert.False(decisions.IsDecided(Id(3)));
    }

    [Fact]
    public void LinkedMembersFollowTheLowestDirectlyRemovedMemberOfTheirGroup()
    {
        var touching = new RemovalReason.Touching(Id(0));

        var decisions = Start().Apply(RoundKind.AlsoRemove, [Propose(4, touching), Propose(2, touching), Propose(0, TooClose)]);

        Assert.Equal(new RemovalReason.LinkedTo(Id(0)), Removed(decisions, 1).Reason);
        Assert.Equal(new RemovalReason.LinkedTo(Id(4)), Removed(decisions, 3).Reason);
    }

    [Fact]
    public void DirectlyRemovedMembersKeepTheirOwnReasonInsteadOfLinked()
    {
        var touching = new RemovalReason.Touching(Id(8));

        var decisions = Start().Apply(RoundKind.TooClose, [Propose(0, TooClose), Propose(1, touching)]);

        Assert.Equal(TooClose, Removed(decisions, 0).Reason);
        Assert.Equal(touching, Removed(decisions, 1).Reason);
        Assert.Equal(new RemovalReason.LinkedTo(Id(0)), Removed(decisions, 2).Reason);
    }

    [Fact]
    public void GroupWithAKeptMemberIsNeverRemoved()
    {
        var decisions = Start(new Dictionary<int, KeepReason> { [7] = QuestReason })
            .Apply(RoundKind.TooClose, [Propose(5, TooClose), Propose(6, TooClose), Propose(7, TooClose)]);

        Assert.All(new[] { 5, 6, 7 }, member => Assert.IsType<Decision.Kept>(decisions.Of(Id(member))));
        Assert.Empty(decisions.RemovedIn(decisions.Rounds[0]));
    }

    [Fact]
    public void ProposingOneMemberOfAKeptGroupLeavesTheOtherMembersUndecided()
    {
        var decisions = Start(new Dictionary<int, KeepReason> { [7] = QuestReason }).Apply(RoundKind.TooClose, [Propose(5, TooClose)]);

        Assert.IsType<Decision.Kept>(decisions.Of(Id(5)));
        Assert.False(decisions.IsDecided(Id(6)));
        Assert.False(decisions.IsDecided(Id(7)));
    }

    [Fact]
    public void KeptObjectGetsItsOwnReason()
    {
        var decisions = Start(new Dictionary<int, KeepReason> { [7] = QuestReason }).Apply(RoundKind.TooClose, [Propose(7, TooClose)]);

        var kept = Kept(decisions, 7);

        Assert.Equal(QuestReason, kept.KeepReason);
        Assert.Equal(TooClose, kept.Reason);
    }

    [Fact]
    public void KeptGroupMemberWithoutOwnReasonGetsTheLinkedGroupReason()
    {
        var decisions = Start(new Dictionary<int, KeepReason> { [7] = QuestReason }).Apply(RoundKind.TooClose, [Propose(5, TooClose)]);

        Assert.Equal(KeepKind.LinkedGroup, Kept(decisions, 5).KeepReason.Kind);
    }

    [Fact]
    public void DecisionOfAnEarlierRoundNeverChanges()
    {
        var first = Start().Apply(RoundKind.TooClose, [Propose(0, TooClose)]);
        var firstRound = first.Rounds[0];

        var second = first.Apply(RoundKind.AlsoRemove, [Propose(0, new RemovalReason.Touching(Id(8))), Propose(8, TooClose)]);

        Assert.Equal(firstRound, Removed(second, 0).Round);
        Assert.Equal(TooClose, Removed(second, 0).Reason);
        Assert.Equal(Ids(8), second.RemovedIn(second.Rounds[1]).ToArray());
    }

    [Fact]
    public void OlderViewDoesNotSeeLaterRounds()
    {
        var first = Start().Apply(RoundKind.TooClose, [Propose(0, TooClose)]);

        first.Apply(RoundKind.AlsoRemove, [Propose(8, TooClose)]);

        Assert.Null(first.Of(Id(8)));
        Assert.False(first.IsDecided(Id(8)));
        Assert.Single(first.Rounds);
        Assert.DoesNotContain(first.All(), decided => decided.Target == Id(8));
        Assert.DoesNotContain(Id(8), first.RemovedTargets());
    }

    [Fact]
    public void OnlyTheNewestViewAppliesARound()
    {
        var start = Start();
        var first = start.Apply(RoundKind.TooClose, [Propose(0, TooClose)]);
        first.Apply(RoundKind.AlsoRemove, [Propose(8, TooClose)]);

        Assert.Throws<InvalidOperationException>(() => start.Apply(RoundKind.TooClose, []));
        Assert.Throws<InvalidOperationException>(() => first.Apply(RoundKind.AlsoRemove, []));
    }

    [Fact]
    public void RoundReportsDirectRemovalsInTargetOrderThenLinkedMembers()
    {
        var decisions = Start().Apply(RoundKind.TooClose, [Propose(4, TooClose), Propose(1, TooClose)]);

        Assert.Equal(Ids(1, 4, 0, 2, 3), decisions.RemovedIn(decisions.Rounds[0]).ToArray());
        Assert.Empty(decisions.KeptIn(decisions.Rounds[0]));
    }

    [Fact]
    public void RoundReportsKeptObjectsInTargetOrder()
    {
        var decisions = Start(new Dictionary<int, KeepReason> { [7] = QuestReason, [9] = QuestReason })
            .Apply(RoundKind.TooClose, [Propose(9, TooClose), Propose(1, TooClose), Propose(7, TooClose)]);

        Assert.Equal(Ids(7, 9), decisions.KeptIn(decisions.Rounds[0]).ToArray());
        Assert.Equal(Ids(1, 0, 2), decisions.RemovedIn(decisions.Rounds[0]).ToArray());
    }

    [Fact]
    public void DecisionsDoNotDependOnProposalOrder()
    {
        var references = new Dictionary<int, KeepReason> { [7] = QuestReason, [10] = QuestReason };
        var proposals = new[] { 0, 2, 3, 5, 8, 9, 10 }.Select(index => Propose(index, new RemovalReason.Touching(Id(index)))).ToList();
        var expected = Start(references).Apply(RoundKind.AlsoRemove, proposals);
        var random = new Random(ShuffleSeed);

        for (var attempt = 0; attempt < ShuffleCount; attempt++)
        {
            var shuffled = proposals.OrderBy(_ => random.Next()).ToList();

            var actual = Start(references).Apply(RoundKind.AlsoRemove, shuffled);

            Assert.Equal(expected.All().ToList(), actual.All().ToList());
            Assert.Equal(expected.RemovedIn(expected.Rounds[0]).ToArray(), actual.RemovedIn(actual.Rounds[0]).ToArray());
            Assert.Equal(expected.KeptIn(expected.Rounds[0]).ToArray(), actual.KeptIn(actual.Rounds[0]).ToArray());
        }
    }

    [Fact]
    public void FirstProposalWinsForATargetProposedTwice()
    {
        var touching = new RemovalReason.Touching(Id(9));

        var decisions = Start().Apply(RoundKind.TooClose, [Propose(8, TooClose), Propose(8, touching)]);

        Assert.Equal(TooClose, Removed(decisions, 8).Reason);
        Assert.Equal(Ids(8), decisions.RemovedIn(decisions.Rounds[0]).ToArray());
    }

    [Fact]
    public void LinkedMembersAreTakenAlongInEveryRoundKind()
    {
        var decisions = Start()
            .Apply(RoundKind.TooClose, [Propose(0, TooClose)])
            .Apply(RoundKind.AlsoRemove, [Propose(3, TooClose)])
            .Apply(RoundKind.LeftBehind, [Propose(5, TooClose)]);

        Assert.Equal(RoundKind.TooClose, Removed(decisions, 1).Round.Kind);
        Assert.Equal(RoundKind.AlsoRemove, Removed(decisions, 4).Round.Kind);
        Assert.Equal(RoundKind.LeftBehind, Removed(decisions, 6).Round.Kind);
        Assert.Equal(new RemovalReason.LinkedTo(Id(0)), Removed(decisions, 2).Reason);
        Assert.Equal(new RemovalReason.LinkedTo(Id(3)), Removed(decisions, 4).Reason);
        Assert.Equal(new RemovalReason.LinkedTo(Id(5)), Removed(decisions, 7).Reason);
    }

    [Fact]
    public void RemovedTargetsListsEveryRemovedObjectInTargetOrder()
    {
        var decisions = Start(new Dictionary<int, KeepReason> { [9] = QuestReason })
            .Apply(RoundKind.TooClose, [Propose(8, TooClose), Propose(9, TooClose)])
            .Apply(RoundKind.AlsoRemove, [Propose(3, TooClose)]);

        Assert.Equal(Ids(3, 4, 8), decisions.RemovedTargets().ToArray());
    }

    [Fact]
    public void RoundsAfterAnOlderViewAreTheRoundsAddedSince()
    {
        var first = Start().Apply(RoundKind.TooClose, [Propose(0, TooClose)]);
        var second = first.Apply(RoundKind.AlsoRemove, [Propose(8, TooClose)]).Apply(RoundKind.LeftBehind, []);

        Assert.Equal(new[] { second.Rounds[1], second.Rounds[2] }, second.RoundsAfter(first).ToArray());
        Assert.Empty(first.RoundsAfter(first));
    }

    [Fact]
    public void MovesBelongToTheViewThatAddedThem()
    {
        var before = Start().Apply(RoundKind.TooClose, [Propose(0, TooClose)]);
        var move = new MarkerMove(Id(9), new Vector3(1, 2, 3), new Vector3(4, 5, 6));

        var after = before.AddMoves([move]);

        Assert.Empty(before.Moves);
        Assert.Equal(new[] { move }, after.Moves.ToArray());
        Assert.Equal(before.Rounds.ToArray(), after.Rounds.ToArray());
        Assert.Throws<InvalidOperationException>(() => before.AddMoves([move]));
    }

    [Fact]
    public void RoundLoopStopsWhenTheStartingRoundRemovedNothing()
    {
        var rule = new ScriptedRounds();
        var start = Start().Apply(RoundKind.TooClose, []);

        var (decisions, rounds) = start.WorkingCopy().DecideRoundsUntilNothingRemoved(rule, LastRoundRemovals(start));

        Assert.Empty(rule.RemovedLastRound);
        Assert.Empty(rounds);
        Assert.Single(decisions.Rounds);
    }

    [Fact]
    public void RoundLoopStopsOnTheFirstEmptyRound()
    {
        var rule = new ScriptedRounds([Propose(8, TooClose)], [], [Propose(9, TooClose)]);
        var start = Start().Apply(RoundKind.TooClose, [Propose(10, TooClose)]);

        var (decisions, rounds) = start.WorkingCopy().DecideRoundsUntilNothingRemoved(rule, LastRoundRemovals(start));

        Assert.Equal(2, rule.RemovedLastRound.Count);
        Assert.Equal(2, rounds.Length);
        Assert.Equal(3, decisions.Rounds.Length);
        Assert.False(decisions.IsDecided(Id(9)));
    }

    [Fact]
    public void RoundLoopAddsAlsoRemoveRoundsNumberedUpward()
    {
        var rule = new ScriptedRounds([Propose(8, TooClose)], [Propose(9, TooClose)], []);
        var start = Start().Apply(RoundKind.TooClose, [Propose(10, TooClose)]);

        var (decisions, _) = start.WorkingCopy().DecideRoundsUntilNothingRemoved(rule, LastRoundRemovals(start));

        Assert.Equal(
            new[] { new Round(1, RoundKind.TooClose), new Round(2, RoundKind.AlsoRemove), new Round(3, RoundKind.AlsoRemove), new Round(4, RoundKind.AlsoRemove) },
            decisions.Rounds.ToArray());
    }

    [Fact]
    public void RoundLoopHandsTheRuleTheLastRoundsRemovalsIncludingLinkedMembers()
    {
        var rule = new ScriptedRounds([Propose(3, TooClose)], []);
        var start = Start().Apply(RoundKind.TooClose, [Propose(1, TooClose)]);

        start.WorkingCopy().DecideRoundsUntilNothingRemoved(rule, LastRoundRemovals(start));

        Assert.Equal(Ids(1, 0, 2), rule.RemovedLastRound[0]);
        Assert.Equal(Ids(3, 4), rule.RemovedLastRound[1]);
    }

    [Fact]
    public void RoundLoopStartsFromTheSeedsItIsGiven()
    {
        var rule = new ScriptedRounds([Propose(9, TooClose)], []);
        var start = Start()
            .Apply(RoundKind.ChangedTerrain, [Propose(8, TooClose)])
            .Apply(RoundKind.TooClose, [Propose(10, TooClose)]);

        var (decisions, _) = start.WorkingCopy().DecideRoundsUntilNothingRemoved(rule, [.. Ids(8, 10)]);

        Assert.Equal(Ids(8, 10), rule.RemovedLastRound[0]);
        Assert.Equal(Ids(9), rule.RemovedLastRound[1]);
        Assert.Equal(new Round(3, RoundKind.AlsoRemove), decisions.Rounds[2]);
    }

    [Fact]
    public void RoundLoopReturnsTheDetailsInRoundOrder()
    {
        var rule = new ScriptedRounds([Propose(8, TooClose)], [Propose(9, TooClose)], []);
        var start = Start().Apply(RoundKind.TooClose, [Propose(10, TooClose)]);

        var (_, rounds) = start.WorkingCopy().DecideRoundsUntilNothingRemoved(rule, LastRoundRemovals(start));

        Assert.Equal(new[] { "details 1", "details 2", "details 3" }, rounds.Select(round => round.Details).ToArray());
    }

    [Fact]
    public void AFitRoundComesFirstAndALaterRemovalRoundSkipsTheFittedObjects()
    {
        var fitted = Start().ApplyFits([Fit(9), Fit(8)]);

        var final = fitted.Apply(RoundKind.TooClose, [Propose(8, TooClose), Propose(9, TooClose), Propose(10, TooClose)]);

        Assert.Equal(new Round(1, RoundKind.Fit), final.Rounds[0]);
        Assert.Equal(Ids(8, 9), final.FittedIn(final.Rounds[0]).ToArray());
        Assert.Equal(Ids(10), final.RemovedIn(final.Rounds[1]).ToArray());
        Assert.IsType<Decision.Fitted>(final.Of(Id(9)));
    }

    [Fact]
    public void AFittedObjectIsNotRemoved()
    {
        var final = Start().ApplyFits([Fit(8), Fit(11)]).Apply(RoundKind.TooClose, [Propose(10, TooClose)]);

        Assert.Equal((false, false, true, false), (final.IsRemoved(Id(8)), final.IsRemoved(Id(9)), final.IsRemoved(Id(10)), final.IsRemoved(Id(11))));
        Assert.Equal(Ids(10), final.RemovedTargets().ToArray());
    }

    /// <summary>Linked group {0,1,2} removed through target 0; target 3 proposed but kept with its protected partner 4; target 8 fitted; target 10 removed alone.</summary>
    [Fact]
    public void WhenNothingIsRestoredAnObjectHasLeftItsPlaceExactlyWhenItIsRemoved()
    {
        var final = Start(new Dictionary<int, KeepReason> { [4] = QuestReason })
            .ApplyFits([Fit(8)])
            .Apply(RoundKind.TooClose, [Propose(0, TooClose), Propose(3, TooClose), Propose(10, TooClose)]);

        Assert.Equal(Ids(0, 1, 2, 10), Ids([.. Enumerable.Range(0, TargetCount).Where(index => final.HasLeftItsPlace(Id(index)))]));
        Assert.All(Enumerable.Range(0, TargetCount), index => Assert.Equal(final.IsRemoved(Id(index)), final.HasLeftItsPlace(Id(index))));
    }

    [Fact]
    public void WhenNothingIsRestoredTheObjectsThatLeftTheirPlaceInARoundAreTheOnesItRemoved()
    {
        var final = Start().ApplyFits([Fit(8)]).Apply(RoundKind.TooClose, [Propose(0, TooClose), Propose(10, TooClose)]);

        Assert.All(final.Rounds, round => Assert.Equal(final.RemovedIn(round).ToArray(), final.LeftIn(round).ToArray()));
        Assert.Equal(Ids(0, 10, 1, 2), final.LeftIn(final.Rounds[1]).ToArray());
        Assert.Empty(final.LeftIn(final.Rounds[0]));
    }

    [Fact]
    public void TargetsAsTheyStandPlaceOnlyTheFittedObjectsAtTheirNewPlacement()
    {
        var targets = TestTargets.CreateMany(TargetCount);
        var final = Start().ApplyFits([Fit(9)]).Apply(RoundKind.TooClose, [Propose(10, TooClose)]);

        var standing = final.TargetsAsTheyStand([.. targets]);

        Assert.Equal(FitPlacement, standing[9].Placement);
        Assert.All(Enumerable.Range(0, TargetCount).Where(index => index != 9), index => Assert.Equal(targets[index], standing[index]));
    }

    [Fact]
    public void AnyStandsElsewhereIsTrueExactlyWhenAnObjectIsKeptFitted()
    {
        var removedOnly = Start().Apply(RoundKind.TooClose, [Propose(10, TooClose)]);

        Assert.False(Start().AnyStandsElsewhere());
        Assert.False(removedOnly.AnyStandsElsewhere());
        Assert.True(Start().ApplyFits([Fit(9)]).AnyStandsElsewhere());
        Assert.True(Start().ApplyFits([Fit(9)]).Apply(RoundKind.TooClose, [Propose(10, TooClose)]).AnyStandsElsewhere());
        Assert.False(Start().ApplyFits([Fit(9)]).Apply(RoundKind.AlsoRemove, [Propose(9, TooClose)]).AnyStandsElsewhere());
    }

    [Fact]
    public void AFitThatAnAlsoRemoveRoundReplacedStandsAndIsMeasuredWhereItStoodAndTheOlderViewStillPlacesItAtTheFit()
    {
        var targets = TestTargets.CreateMany(TargetCount);
        var fitted = Start().ApplyFits([Fit(9)]);

        var replaced = fitted.Apply(RoundKind.AlsoRemove, [Propose(9, TooClose)]);

        Assert.Equal(targets[9], replaced.TargetsAsTheyStand([.. targets])[9]);
        Assert.Equal(targets[9], replaced.TargetsAsTheRoundsMeasureThem([.. targets])[9]);
        Assert.Equal(FitPlacement, fitted.TargetsAsTheyStand([.. targets])[9].Placement);
        Assert.Equal(FitPlacement, fitted.TargetsAsTheRoundsMeasureThem([.. targets])[9].Placement);
    }

    [Fact]
    public void AnObjectMarkedRestoreIsRestoredNotDisabledWhenARoundRemovesIt()
    {
        var decisions = StartRestoring([10]).Apply(RoundKind.TooClose, [Propose(10, TooClose), Propose(11, TooClose)]);
        var round = decisions.Rounds[0];

        var restored = Assert.IsType<Decision.Restored>(decisions.Of(Id(10)));

        Assert.Equal((round, TooClose, Earlier), (restored.Round, restored.Reason, restored.EarlierVersion));
        Assert.IsType<Decision.Removed>(decisions.Of(Id(11)));
        Assert.Equal((true, false), (decisions.HasLeftItsPlace(Id(10)), decisions.IsRemoved(Id(10))));
        Assert.Equal((true, true), (decisions.HasLeftItsPlace(Id(11)), decisions.IsRemoved(Id(11))));
        Assert.Equal(Ids(10, 11), decisions.LeftIn(round).ToArray());
        Assert.Equal(Ids(11), decisions.RemovedIn(round).ToArray());
        Assert.Equal(Ids(11), decisions.RemovedTargets().ToArray());
        Assert.True(decisions.IsDecided(Id(10)));
        Assert.False(decisions.MayStillBeRemoved(Id(10)));
    }

    /// <summary>Linked group {0,1,2}: 0 and 1 are marked restore, 2 is not.</summary>
    [Fact]
    public void ARestoredObjectTakesItsLinkedGroupAlongAndEachMemberGetsItsOwnOutcome()
    {
        var decisions = StartRestoring([0, 1]).Apply(RoundKind.TooClose, [Propose(1, TooClose)]);
        var round = decisions.Rounds[0];

        Assert.Equal(TooClose, Assert.IsType<Decision.Restored>(decisions.Of(Id(1))).Reason);
        Assert.Equal(new RemovalReason.LinkedTo(Id(1)), Assert.IsType<Decision.Restored>(decisions.Of(Id(0))).Reason);
        Assert.Equal(new RemovalReason.LinkedTo(Id(1)), Removed(decisions, 2).Reason);
        Assert.Equal(Ids(1, 0, 2), decisions.LeftIn(round).ToArray());
        Assert.Equal(Ids(2), decisions.RemovedIn(round).ToArray());
        Assert.Equal(
            new[]
            {
                new RoundEffect(RoundOutcome.Restored, AsLinked: false, KeepReason: null),
                new RoundEffect(RoundOutcome.Restored, AsLinked: true, KeepReason: null),
                new RoundEffect(RoundOutcome.Removed, AsLinked: true, KeepReason: null),
            },
            Ids(1, 0, 2).Select(target => RoundEffects.Of(decisions.Of(target), round)).ToArray());
    }

    [Fact]
    public void AProtectedObjectMarkedRestoreIsKeptAndHasNotLeftItsPlace()
    {
        var decisions = StartRestoring([7], new Dictionary<int, KeepReason> { [7] = QuestReason }).Apply(RoundKind.TooClose, [Propose(7, TooClose)]);

        Assert.IsType<Decision.Kept>(decisions.Of(Id(7)));
        Assert.False(decisions.HasLeftItsPlace(Id(7)));
    }

    [Fact]
    public void AWorkingCopyRestoresAsTheTableSaysAndLeavesTheRunsDecisionsUnchanged()
    {
        var start = StartRestoring([10]).Apply(RoundKind.TooClose, []);

        var decided = start.DecideRoundOnCopy(RoundKind.LeftBehind, [Propose(10, TooClose), Propose(11, TooClose)]);

        Assert.IsType<Decision.Restored>(decided[Id(10)]);
        Assert.IsType<Decision.Removed>(decided[Id(11)]);
        Assert.False(start.IsDecided(Id(10)));
    }

    [Fact]
    public void ARestoredObjectSeedsTheRoundLoopLikeARemovedOne()
    {
        var rule = new ScriptedRounds([Propose(9, TooClose)], []);
        var start = StartRestoring([8]).Apply(RoundKind.TooClose, [Propose(8, TooClose)]);

        var (decisions, _) = start.WorkingCopy().DecideRoundsUntilNothingRemoved(rule, start.LeftIn(start.Rounds[^1]));

        Assert.Equal(Ids(8), rule.RemovedLastRound[0]);
        Assert.Equal(Ids(9), rule.RemovedLastRound[1]);
        Assert.IsType<Decision.Removed>(decisions.Of(Id(9)));
    }

    /// <summary>The rounds and the left-behind steps measure a restored object where the cleaned mod put it, so what rests on it there loses its support; the standing world and the marker obstacles place it at its earlier placement.</summary>
    [Fact]
    public void TheRoundsMeasureARestoredObjectWhereTheCleanedModPutItAndItStandsAtItsEarlierPlacement()
    {
        var targets = TestTargets.CreateMany(TargetCount);
        var final = StartRestoring([10]).ApplyFits([Fit(9)]).Apply(RoundKind.TooClose, [Propose(10, TooClose)]);

        var measured = final.TargetsAsTheRoundsMeasureThem([.. targets]);
        var standing = final.TargetsAsTheyStand([.. targets]);

        Assert.Equal(targets[10], measured[10]);
        Assert.Equal(Earlier.Placement, standing[10].Placement);
        Assert.Equal(FitPlacement, measured[9].Placement);
        Assert.Equal(FitPlacement, standing[9].Placement);
        Assert.All(
            Enumerable.Range(0, TargetCount).Where(index => index is not (9 or 10)),
            index => Assert.Equal((targets[index], targets[index]), (measured[index], standing[index])));
    }

    [Fact]
    public void AWholeRecordRestoreStandsWithTheEarlierBaseWhileTheRoundsMeasureItWithTheCleanedModsBase()
    {
        var cleanedModsBase = new BaseKey(new RecordKey(new PluginName("Target.esp"), 0x300), BaseLinkKind.PlaceableObject);
        var earlierBase = new BaseKey(new RecordKey(new PluginName("Earlier.esp"), 0x301), BaseLinkKind.PlaceableObject);
        var targets = TestTargets.CreateMany(TargetCount);
        targets[10] = targets[10] with { Base = cleanedModsBase };
        var wholeRecord = Earlier with { Base = earlierBase, RestoresWholeRecord = true };
        var start = RemovalDecisions.Start(
            Protection(references: null),
            [.. Enumerable.Range(0, TargetCount).Select(index => index == 10 ? (WhenRemoved)new WhenRemoved.Restore(wholeRecord) : new WhenRemoved.Disable())]);

        var final = start.Apply(RoundKind.TooClose, [Propose(10, TooClose)]);

        Assert.Equal(targets[10], final.TargetsAsTheRoundsMeasureThem([.. targets])[10]);
        var standing = final.TargetsAsTheyStand([.. targets])[10];
        Assert.Equal((earlierBase, Earlier.Placement), (standing.Base, standing.Placement));
    }

    /// <summary>The whole record comes back, so the earlier version's primitive, marker, door and radius facts decide how the object shows, not the cleaned mod's.</summary>
    [Fact]
    public void AWholeRecordRestoreStandsWithTheReferenceFactsOfTheEarlierVersionAndAPlacementOnlyRestoreKeepsTheCleanedModsOwn()
    {
        var target = TestTargets.Create(0, TestTargets.At(Vector3.Zero));
        var reference = new ReferenceFacts(IsTeleportDoor: true, IsPrimitive: true, HasMapMarker: true, ReferenceRadius: 8f, PrimitiveBounds: new Vector3(1, 2, 3));

        var wholeRecord = target.RestoredAs(Earlier with { RestoresWholeRecord = true, Reference = reference });
        var placementOnly = target.RestoredAs(Earlier with { Reference = reference });

        Assert.Equal(
            (true, true, true, (float?)8f, (Vector3?)new Vector3(1, 2, 3)),
            (wholeRecord.IsTeleportDoor, wholeRecord.IsPrimitive, wholeRecord.HasMapMarker, wholeRecord.ReferenceRadius, wholeRecord.PrimitiveBounds));
        Assert.Equal(target.PlacedAt(Earlier.Placement), placementOnly);
    }

    [Fact]
    public void AFitThatAnAlsoRemoveRoundRestoredStandsAtItsEarlierPlacementAndIsMeasuredWhereTheCleanedModPutIt()
    {
        var targets = TestTargets.CreateMany(TargetCount);
        var final = StartRestoring([9]).ApplyFits([Fit(9)]).Apply(RoundKind.AlsoRemove, [Propose(9, TooClose)]);

        Assert.IsType<Decision.Restored>(final.Of(Id(9)));
        Assert.Equal(targets[9], final.TargetsAsTheRoundsMeasureThem([.. targets])[9]);
        Assert.Equal(Earlier.Placement, final.TargetsAsTheyStand([.. targets])[9].Placement);
    }

    [Fact]
    public void ARestoredObjectStandsElsewhereButIsMeasuredElsewhereOnlyWhenFitted()
    {
        var restored = StartRestoring([10]).Apply(RoundKind.TooClose, [Propose(10, TooClose)]);
        var fitted = Start().ApplyFits([Fit(9)]);

        Assert.Equal((true, false), (restored.AnyStandsElsewhere(), restored.AnyMeasuredElsewhere()));
        Assert.Equal((true, true), (fitted.AnyStandsElsewhere(), fitted.AnyMeasuredElsewhere()));
        Assert.Equal((false, false), (StartRestoring([10]).AnyStandsElsewhere(), StartRestoring([10]).AnyMeasuredElsewhere()));
    }

    [Fact]
    public void NoFitOpensNoRound()
    {
        var start = Start();

        Assert.Same(start, start.ApplyFits([]));
    }

    [Fact]
    public void FittingAProtectedOrLinkedObjectIsABug()
    {
        Assert.Throws<InvalidOperationException>(() => Start(new Dictionary<int, KeepReason> { [8] = QuestReason }).ApplyFits([Fit(8)]));
        Assert.Throws<InvalidOperationException>(() => Start().ApplyFits([Fit(0)]));
    }

    [Fact]
    public void AWorkingCopyKeepsTheFitRound()
    {
        var fitted = Start().ApplyFits([Fit(9)]);

        var copy = fitted.WorkingCopy().DecideRound(RoundKind.TooClose, [Propose(10, TooClose)]);

        Assert.Equal(Ids(9), copy.FittedIn(copy.Rounds[0]).ToArray());
        Assert.IsType<Decision.Fitted>(copy.Of(Id(9)));
    }

    private static ProposedFit Fit(int index) => new(Id(index), TooClose, FitPlacement);

    private static RemovalDecisions Start(IReadOnlyDictionary<int, KeepReason>? references = null) =>
        TestSeededDecisions.StartDisablingAll(Protection(references), TargetCount);

    /// <param name="restored">The targets marked restore; every other target is marked disable.</param>
    private static RemovalDecisions StartRestoring(IReadOnlyCollection<int> restored, IReadOnlyDictionary<int, KeepReason>? references = null) =>
        RemovalDecisions.Start(
            Protection(references),
            [.. Enumerable.Range(0, TargetCount).Select(index => restored.Contains(index) ? (WhenRemoved)new WhenRemoved.Restore(Earlier) : new WhenRemoved.Disable())]);

    private static IObjectsToKeep Protection(IReadOnlyDictionary<int, KeepReason>? references) =>
        ObjectsToKeep.Build(TestTargets.CreateMany(TargetCount), Links, TestTargets.References(TargetCount, references), TestBaseFacts.Unresolved);

    private static ImmutableArray<TargetId> LastRoundRemovals(IRemovalDecisions decisions) => decisions.RemovedIn(decisions.Rounds[^1]);

    private static TargetId Id(int index) => new(index);

    private static TargetId[] Ids(params int[] indexes) => [.. indexes.Select(Id)];

    private static ProposedRemoval Propose(int index, RemovalReason reason) => new(Id(index), reason);

    private static Decision.Removed Removed(IRemovalDecisions decisions, int index) => Assert.IsType<Decision.Removed>(decisions.Of(Id(index)));

    private static Decision.Kept Kept(IRemovalDecisions decisions, int index) => Assert.IsType<Decision.Kept>(decisions.Of(Id(index)));

    /// <summary>A rule that proposes one scripted list per round and records what it was handed.</summary>
    private sealed class ScriptedRounds(params ProposedRemoval[][] script) : IRemovalRounds<string>
    {
        public List<TargetId[]> RemovedLastRound { get; } = [];

        public RoundProposals<string> Next(IRemovalDecisions decisions, ImmutableArray<TargetId> removedLastRound)
        {
            var call = RemovedLastRound.Count;
            RemovedLastRound.Add([.. removedLastRound]);
            return new RoundProposals<string>(script[call], $"details {call + 1}");
        }
    }
}
