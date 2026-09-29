using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.LoadOrder;

public class LinkedGroupTests
{
    [Fact]
    public void LinksJoinTargetsInEitherDirection()
    {
        var targets = TestTargets.CreateMany(6);
        var groups = LinkedGroups.Build(targets.Count,
        [
            TestTargets.Link(2, 0),
            TestTargets.Link(4, 3),
            TestTargets.Link(5, 4),
        ]);

        Assert.Equal(new[] { new[] { 0, 2 }, new[] { 1 }, new[] { 3, 4, 5 } }, groups.All.Select(g => g.ToArray()));
        Assert.Equal(new[] { 3, 4, 5 }, groups.MembersOf(5));
        Assert.False(groups.IsLinked(1));
        Assert.Equal(2, groups.MultiMemberGroups.Count());
    }

    [Fact]
    public void LinkedRemovalsAddEachMissingMemberOnce()
    {
        var targets = TestTargets.CreateMany(5);
        var groups = LinkedGroups.Build(targets.Count,
        [
            TestTargets.Link(0, 1),
            TestTargets.Link(1, 2),
            TestTargets.Link(3, 4),
        ]);
        Removal[] decided = [new TooCloseRemoval(0, default), new TooCloseRemoval(2, default), new TouchingRemoval(4, 0)];

        var linked = groups.CollectLinkedRemovals(decided, new HashSet<int> { 0, 2, 4 });

        Assert.Equal(new[] { new LinkedRemoval(1, 0), new LinkedRemoval(3, 4) }, linked);
    }

    [Fact]
    public void OneKeptMemberKeepsItsWholeGroup()
    {
        var targets = TestTargets.CreateMany(4);
        targets[3] = TestTargets.Create(3, TestTargets.At(Vector3.Zero), isTeleportDoor: true);
        var references = TestTargets.References(targets.Count, new Dictionary<int, KeepReason>
        {
            [1] = new(KeepKind.NonPlacedReference, "QUST record", "linked from QUST MyQuest"),
        });

        var rule = Protection.Build(targets, [TestTargets.Link(0, 1)], references);

        Assert.True(rule.TryGetKeepReason(0, out var linkedReason));
        Assert.Equal(KeepKind.LinkedGroup, linkedReason.Kind);
        Assert.Contains(TestTargets.Key(1).ToString(), linkedReason.Detail);
        Assert.Null(rule.GetOwnReason(0));
        Assert.Equal(KeepKind.NonPlacedReference, rule.GetOwnReason(1)?.Kind);
        Assert.False(rule.TryGetKeepReason(2, out _));
        Assert.True(rule.TryGetKeepReason(3, out var doorReason));
        Assert.Equal(KeepKind.TeleportDoor, doorReason.Kind);
    }
}
