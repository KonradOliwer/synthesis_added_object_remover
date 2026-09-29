using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>Scene: linked groups {0,1,2}, {3,4} and {5,6,7}; targets 8 to 11 are alone.</summary>
public class LedgerTests
{
    private const int TargetCount = 12;
    private const int ShuffleSeed = 5;
    private const int ShuffleCount = 25;

    private static readonly KeepReason QuestReason = new(KeepKind.NonPlacedReference, "QUST record", "linked from QUST");

    private static readonly TargetLink[] Links =
    [
        TestTargets.Link(0, 1),
        TestTargets.Link(1, 2),
        TestTargets.Link(3, 4),
        TestTargets.Link(5, 6),
        TestTargets.Link(6, 7),
    ];

    private static readonly Cause TooClose = new Cause.TooClose(new OtherId(0));

    [Fact]
    public void RemovedMemberTakesTheUndecidedMembersOfItsGroupAlong()
    {
        var ledger = Start().Apply(RoundKind.TooClose, [Propose(1, TooClose)]);

        Assert.Equal(new Cause.Linked(Id(1)), Removed(ledger, 0).Cause);
        Assert.Equal(new Cause.Linked(Id(1)), Removed(ledger, 2).Cause);
        Assert.Equal(TooClose, Removed(ledger, 1).Cause);
        Assert.False(ledger.IsDecided(Id(3)));
    }

    [Fact]
    public void LinkedMembersFollowTheLowestDirectlyRemovedMemberOfTheirGroup()
    {
        var touching = new Cause.Touching(Id(0));

        var ledger = Start().Apply(RoundKind.FollowUp, [Propose(4, touching), Propose(2, touching), Propose(0, TooClose)]);

        Assert.Equal(new Cause.Linked(Id(0)), Removed(ledger, 1).Cause);
        Assert.Equal(new Cause.Linked(Id(4)), Removed(ledger, 3).Cause);
    }

    [Fact]
    public void DirectlyRemovedMembersKeepTheirOwnCauseInsteadOfLinked()
    {
        var touching = new Cause.Touching(Id(8));

        var ledger = Start().Apply(RoundKind.TooClose, [Propose(0, TooClose), Propose(1, touching)]);

        Assert.Equal(TooClose, Removed(ledger, 0).Cause);
        Assert.Equal(touching, Removed(ledger, 1).Cause);
        Assert.Equal(new Cause.Linked(Id(0)), Removed(ledger, 2).Cause);
    }

    [Fact]
    public void GroupWithAKeptMemberIsNeverRemoved()
    {
        var ledger = Start(new Dictionary<int, KeepReason> { [7] = QuestReason })
            .Apply(RoundKind.TooClose, [Propose(5, TooClose), Propose(6, TooClose), Propose(7, TooClose)]);

        Assert.All(new[] { 5, 6, 7 }, member => Assert.IsType<Verdict.Held>(ledger.Of(Id(member))));
        Assert.Empty(ledger.RemovedIn(ledger.Rounds[0]));
    }

    [Fact]
    public void ProposingOneMemberOfAKeptGroupLeavesTheOtherMembersUndecided()
    {
        var ledger = Start(new Dictionary<int, KeepReason> { [7] = QuestReason }).Apply(RoundKind.TooClose, [Propose(5, TooClose)]);

        Assert.IsType<Verdict.Held>(ledger.Of(Id(5)));
        Assert.False(ledger.IsDecided(Id(6)));
        Assert.False(ledger.IsDecided(Id(7)));
    }

    [Fact]
    public void HeldObjectGetsItsOwnReason()
    {
        var ledger = Start(new Dictionary<int, KeepReason> { [7] = QuestReason }).Apply(RoundKind.TooClose, [Propose(7, TooClose)]);

        var held = Held(ledger, 7);

        Assert.Equal(QuestReason, held.Reason);
        Assert.Equal(TooClose, held.Cause);
    }

    [Fact]
    public void HeldGroupMemberWithoutOwnReasonGetsTheLinkedGroupReason()
    {
        var ledger = Start(new Dictionary<int, KeepReason> { [7] = QuestReason }).Apply(RoundKind.TooClose, [Propose(5, TooClose)]);

        Assert.Equal(KeepKind.LinkedGroup, Held(ledger, 5).Reason.Kind);
    }

    [Fact]
    public void VerdictOfAnEarlierRoundNeverChanges()
    {
        var first = Start().Apply(RoundKind.TooClose, [Propose(0, TooClose)]);
        var firstRound = first.Rounds[0];

        var second = first.Apply(RoundKind.FollowUp, [Propose(0, new Cause.Touching(Id(8))), Propose(8, TooClose)]);

        Assert.Equal(firstRound, Removed(second, 0).Round);
        Assert.Equal(TooClose, Removed(second, 0).Cause);
        Assert.Equal(Ids(8), second.RemovedIn(second.Rounds[1]).ToArray());
    }

    [Fact]
    public void OlderViewDoesNotSeeLaterRounds()
    {
        var first = Start().Apply(RoundKind.TooClose, [Propose(0, TooClose)]);

        first.Apply(RoundKind.FollowUp, [Propose(8, TooClose)]);

        Assert.Null(first.Of(Id(8)));
        Assert.False(first.IsDecided(Id(8)));
        Assert.Single(first.Rounds);
        Assert.DoesNotContain(first.All(), decided => decided.Target == Id(8));
    }

    [Fact]
    public void OnlyTheNewestViewAppliesARound()
    {
        var start = Start();
        var first = start.Apply(RoundKind.TooClose, [Propose(0, TooClose)]);
        first.Apply(RoundKind.FollowUp, [Propose(8, TooClose)]);

        Assert.Throws<InvalidOperationException>(() => start.Apply(RoundKind.TooClose, []));
        Assert.Throws<InvalidOperationException>(() => first.Apply(RoundKind.FollowUp, []));
    }

    [Fact]
    public void RoundReportsDirectRemovalsInTargetOrderThenLinkedMembers()
    {
        var ledger = Start().Apply(RoundKind.TooClose, [Propose(4, TooClose), Propose(1, TooClose)]);

        Assert.Equal(Ids(1, 4, 0, 2, 3), ledger.RemovedIn(ledger.Rounds[0]).ToArray());
        Assert.Empty(ledger.HeldIn(ledger.Rounds[0]));
    }

    [Fact]
    public void RoundReportsHeldObjectsInTargetOrder()
    {
        var ledger = Start(new Dictionary<int, KeepReason> { [7] = QuestReason, [9] = QuestReason })
            .Apply(RoundKind.TooClose, [Propose(9, TooClose), Propose(1, TooClose), Propose(7, TooClose)]);

        Assert.Equal(Ids(7, 9), ledger.HeldIn(ledger.Rounds[0]).ToArray());
        Assert.Equal(Ids(1, 0, 2), ledger.RemovedIn(ledger.Rounds[0]).ToArray());
    }

    [Fact]
    public void DecisionsDoNotDependOnProposalOrder()
    {
        var references = new Dictionary<int, KeepReason> { [7] = QuestReason, [10] = QuestReason };
        var proposals = new[] { 0, 2, 3, 5, 8, 9, 10 }.Select(index => Propose(index, new Cause.Touching(Id(index)))).ToList();
        var expected = Start(references).Apply(RoundKind.FollowUp, proposals);
        var random = new Random(ShuffleSeed);

        for (var attempt = 0; attempt < ShuffleCount; attempt++)
        {
            var shuffled = proposals.OrderBy(_ => random.Next()).ToList();

            var actual = Start(references).Apply(RoundKind.FollowUp, shuffled);

            Assert.Equal(expected.All().ToList(), actual.All().ToList());
            Assert.Equal(expected.RemovedIn(expected.Rounds[0]).ToArray(), actual.RemovedIn(actual.Rounds[0]).ToArray());
            Assert.Equal(expected.HeldIn(expected.Rounds[0]).ToArray(), actual.HeldIn(actual.Rounds[0]).ToArray());
        }
    }

    [Fact]
    public void FirstProposalWinsForATargetProposedTwice()
    {
        var touching = new Cause.Touching(Id(9));

        var ledger = Start().Apply(RoundKind.TooClose, [Propose(8, TooClose), Propose(8, touching)]);

        Assert.Equal(TooClose, Removed(ledger, 8).Cause);
        Assert.Equal(Ids(8), ledger.RemovedIn(ledger.Rounds[0]).ToArray());
    }

    [Fact]
    public void LinkedMembersAreTakenAlongInEveryRoundKind()
    {
        var ledger = Start()
            .Apply(RoundKind.TooClose, [Propose(0, TooClose)])
            .Apply(RoundKind.FollowUp, [Propose(3, TooClose)])
            .Apply(RoundKind.Leftover, [Propose(5, TooClose)]);

        Assert.Equal(RoundKind.TooClose, Removed(ledger, 1).Round.Kind);
        Assert.Equal(RoundKind.FollowUp, Removed(ledger, 4).Round.Kind);
        Assert.Equal(RoundKind.Leftover, Removed(ledger, 6).Round.Kind);
        Assert.Equal(new Cause.Linked(Id(0)), Removed(ledger, 2).Cause);
        Assert.Equal(new Cause.Linked(Id(3)), Removed(ledger, 4).Cause);
        Assert.Equal(new Cause.Linked(Id(5)), Removed(ledger, 7).Cause);
    }

    private static Ledger Start(IReadOnlyDictionary<int, KeepReason>? references = null) =>
        Ledger.Start(Protection.Build(TestTargets.CreateMany(TargetCount), Links, TestTargets.References(TargetCount, references)), TargetCount);

    private static TargetId Id(int index) => new(index);

    private static TargetId[] Ids(params int[] indexes) => [.. indexes.Select(Id)];

    private static Proposal Propose(int index, Cause cause) => new(Id(index), cause);

    private static Verdict.Removed Removed(Ledger ledger, int index) => Assert.IsType<Verdict.Removed>(ledger.Of(Id(index)));

    private static Verdict.Held Held(Ledger ledger, int index) => Assert.IsType<Verdict.Held>(ledger.Of(Id(index)));
}
