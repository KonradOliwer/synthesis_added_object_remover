using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.LoadOrder;

public class LinkRuleTests
{
    private static readonly RecordKey OtherPlaced = new FormKey(ModKey.FromNameAndExtension("Other.esp"), 0x900).ToRecordKey();
    private static readonly IReadOnlyList<TargetObject> Targets = TestTargets.CreateMany(4);

    [Fact]
    public void LinkBetweenTargetObjectsJoinsAGroup()
    {
        var outcome = LinkRule.DecideLinkOutcome(Targets, [Placed(TestTargets.Key(0), TestTargets.Key(1), RelationKind.EnableParent)]);

        Assert.Equal(TestTargets.Link(0, 1), Assert.Single(outcome.GroupLinks));
        Assert.All(outcome.References, Assert.Null);
        Assert.Equal(1, outcome.TargetObjectLinkCount);
    }

    [Fact]
    public void LinkFromATargetObjectToANonTargetIsCountedButJoinsNothing()
    {
        var outcome = LinkRule.DecideLinkOutcome(Targets, [Placed(TestTargets.Key(0), TestTargets.Key(9), RelationKind.LinkedReference)]);

        Assert.Empty(outcome.GroupLinks);
        Assert.Equal(1, outcome.TargetObjectLinkCount);
    }

    [Fact]
    public void LinkFromAnotherPlacedRecordKeepsTheTarget()
    {
        var outcome = LinkRule.DecideLinkOutcome(Targets, [Placed(OtherPlaced, TestTargets.Key(1), RelationKind.EnableParent)]);

        Assert.Empty(outcome.GroupLinks);
        var reason = outcome.References[1];
        Assert.Equal(KeepKind.PlacedReference, reason!.Kind);
        Assert.Equal("placed object: Enable Parent", KeepReasonText.Category(reason));
        Assert.Equal($"Enable Parent of {OtherPlaced}", KeepReasonText.Detail(reason));
    }

    [Fact]
    public void TeleportDestinationKeepsTheTargetEvenFromATargetObject()
    {
        var outcome = LinkRule.DecideLinkOutcome(Targets, [Placed(TestTargets.Key(0), TestTargets.Key(1), RelationKind.TeleportDestination)]);

        Assert.Empty(outcome.GroupLinks);
        Assert.Equal(0, outcome.TargetObjectLinkCount);
        Assert.Equal(KeepKind.TeleportDoor, outcome.References[1]!.Kind);
        Assert.Equal("placed object: teleport destination", KeepReasonText.Category(outcome.References[1]!));
    }

    [Fact]
    public void BaseObjectAndSelfLinksCountForNothing()
    {
        var outcome = LinkRule.DecideLinkOutcome(
            Targets,
            [Placed(OtherPlaced, TestTargets.Key(1), RelationKind.BaseObject), Placed(TestTargets.Key(2), TestTargets.Key(2), RelationKind.Other)]);

        Assert.Empty(outcome.GroupLinks);
        Assert.All(outcome.References, Assert.Null);
        Assert.Equal(0, outcome.TargetObjectLinkCount);
    }

    [Fact]
    public void NonPlacedRecordLinkKeepsTheTarget()
    {
        var link = new LinkFact(OtherPlaced, "OtherList", "FormList", TestTargets.Key(2), RelationKind.Other, SourceIsPlaced: false, SourceIsWorldspace: false);

        var outcome = LinkRule.DecideLinkOutcome(Targets, [link]);

        var reason = outcome.References[2]!;
        Assert.Equal(KeepKind.NonPlacedReference, reason.Kind);
        Assert.Equal("FormList record", KeepReasonText.Category(reason));
        Assert.Equal($"linked from FormList OtherList [{OtherPlaced}]", KeepReasonText.Detail(reason));
    }

    [Fact]
    public void WorldspaceLinkCountsForNothing()
    {
        var link = new LinkFact(OtherPlaced, "OtherWorld", "Worldspace", TestTargets.Key(1), RelationKind.Other, SourceIsPlaced: false, SourceIsWorldspace: true);

        var outcome = LinkRule.DecideLinkOutcome(Targets, [link]);

        Assert.All(outcome.References, Assert.Null);
    }

    [Fact]
    public void TheFirstLinkFoundKeepsItsReason()
    {
        var nonPlaced = new LinkFact(OtherPlaced, null, "FormList", TestTargets.Key(1), RelationKind.Other, SourceIsPlaced: false, SourceIsWorldspace: false);

        var outcome = LinkRule.DecideLinkOutcome(Targets, [Placed(OtherPlaced, TestTargets.Key(1), RelationKind.AttachRef), nonPlaced]);

        Assert.Equal("placed object: Attach Ref", KeepReasonText.Category(outcome.References[1]!));
    }

    private static LinkFact Placed(RecordKey source, RecordKey target, RelationKind relation) =>
        new(source, null, "PlacedObject", target, relation, SourceIsPlaced: true, SourceIsWorldspace: false);
}
