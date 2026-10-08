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

    private static readonly RemovalReason TooClose = new RemovalReason.TooClose(new OtherId(0));

    [Fact]
    public void DroppingTheNewerRoundsMakesAnOlderViewTheNewestAgain()
    {
        var older = Start().Apply(RoundKind.TooClose, [Propose(8, TooClose)]);
        older.Apply(RoundKind.AlsoRemove, [Propose(9, new RemovalReason.Touching(Id(8)))]);
        Assert.Throws<InvalidOperationException>(() => older.Apply(RoundKind.LeftBehind, []));

        older.DropNewerThanThisView();
        var again = older.Apply(RoundKind.LeftBehind, [Propose(10, TooClose)]);

        Assert.Equal(2, again.Rounds.Length);
        Assert.False(again.IsDecided(Id(9)));
        Assert.True(again.IsRemoved(Id(10)));
        Assert.Equal(RoundKind.LeftBehind, Removed(again, 10).Round.Kind);
        Assert.Equal(older.Rounds[0], again.Rounds[0]);
    }

    [Fact]
    public void DroppingTheNewerRoundsAlsoDropsTheNewerMoves()
    {
        var older = Start().Apply(RoundKind.TooClose, [Propose(8, TooClose)]);
        older.AddMoves([new MarkerMove(Id(9), Vector3.Zero, Vector3.One)]);

        older.DropNewerThanThisView();

        Assert.Empty(older.Moves);
        Assert.Empty(older.AddMoves([]).Moves);
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

        var (decisions, details) = start.ApplyRoundsUntilNothingRemoved(rule);

        Assert.Empty(rule.RemovedLastRound);
        Assert.Empty(details);
        Assert.Single(decisions.Rounds);
    }

    [Fact]
    public void RoundLoopStopsOnTheFirstEmptyRound()
    {
        var rule = new ScriptedRounds([Propose(8, TooClose)], [], [Propose(9, TooClose)]);
        var start = Start().Apply(RoundKind.TooClose, [Propose(10, TooClose)]);

        var (decisions, details) = start.ApplyRoundsUntilNothingRemoved(rule);

        Assert.Equal(2, rule.RemovedLastRound.Count);
        Assert.Equal(2, details.Length);
        Assert.Equal(3, decisions.Rounds.Length);
        Assert.False(decisions.IsDecided(Id(9)));
    }

    [Fact]
    public void RoundLoopAddsAlsoRemoveRoundsNumberedUpward()
    {
        var rule = new ScriptedRounds([Propose(8, TooClose)], [Propose(9, TooClose)], []);
        var start = Start().Apply(RoundKind.TooClose, [Propose(10, TooClose)]);

        var (decisions, _) = start.ApplyRoundsUntilNothingRemoved(rule);

        Assert.Equal(
            new[] { new Round(1, RoundKind.TooClose), new Round(2, RoundKind.AlsoRemove), new Round(3, RoundKind.AlsoRemove), new Round(4, RoundKind.AlsoRemove) },
            decisions.Rounds.ToArray());
    }

    [Fact]
    public void RoundLoopHandsTheRuleTheLastRoundsRemovalsIncludingLinkedMembers()
    {
        var rule = new ScriptedRounds([Propose(3, TooClose)], []);
        var start = Start().Apply(RoundKind.TooClose, [Propose(1, TooClose)]);

        start.ApplyRoundsUntilNothingRemoved(rule);

        Assert.Equal(Ids(1, 0, 2), rule.RemovedLastRound[0]);
        Assert.Equal(Ids(3, 4), rule.RemovedLastRound[1]);
    }

    [Fact]
    public void RoundLoopReturnsTheDetailsInRoundOrder()
    {
        var rule = new ScriptedRounds([Propose(8, TooClose)], [Propose(9, TooClose)], []);
        var start = Start().Apply(RoundKind.TooClose, [Propose(10, TooClose)]);

        var (_, details) = start.ApplyRoundsUntilNothingRemoved(rule);

        Assert.Equal(new[] { "details 1", "details 2", "details 3" }, details.ToArray());
    }

    private static RemovalDecisions Start(IReadOnlyDictionary<int, KeepReason>? references = null) =>
        RemovalDecisions.Start(ObjectsToKeep.Build(TestTargets.CreateMany(TargetCount), Links, TestTargets.References(TargetCount, references)), TargetCount);

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
