using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.LoadOrder;

public class ObjectsToKeepFinderTests
{
    private static readonly RecordKey OtherPlaced = new FormKey(ModKey.FromNameAndExtension("Other.esp"), 0x900).ToRecordKey();
    private static readonly RecordKey BaseGameQuest = new FormKey(ModKey.FromNameAndExtension("Skyrim.esm"), 0x123).ToRecordKey();
    private static readonly BaseKey TargetScriptedBase = new(new FormKey(TestTargets.TargetMod, 0x901).ToRecordKey(), BaseLinkKind.PlaceableObject);
    private static readonly BaseKey OtherScriptedBase = new(new FormKey(ModKey.FromNameAndExtension("Other.esp"), 0x901).ToRecordKey(), BaseLinkKind.PlaceableObject);
    private static readonly Dictionary<TargetId, RestingObject> NothingResting = [];

    [Fact]
    public void AnObjectAnotherModsObjectRestsOnIsKeptWithItsLinkedGroupAndALinkComesFirst()
    {
        var targets = TestTargets.CreateMany(4);
        var lantern = new RestingObject(OtherPlaced, "Lantern", KeptFor: null);
        var restingOn = new Dictionary<TargetId, RestingObject>
        {
            [new TargetId(0)] = lantern,
            [new TargetId(2)] = new RestingObject(TestTargets.Key(0), "Book", KeptFor: lantern),
            [new TargetId(3)] = lantern,
        };

        var found = ObjectsToKeepFinder.Find(
            targets, [Placed(TestTargets.Key(1), TestTargets.Key(0)), Placed(OtherPlaced, TestTargets.Key(3))], TestBaseFacts.Unresolved, restingOn);

        var reason = found.ObjectsToKeep.GetOwnReason(0)!;
        Assert.Equal(KeepKind.OtherModsObjectRestsOnIt, reason.Kind);
        Assert.Equal("another mod's object rests on it", KeepReasonText.Category(reason));
        Assert.Equal($"Lantern [{OtherPlaced}] rests on it", KeepReasonText.Detail(reason));
        Assert.Equal($"Lantern [{OtherPlaced}]", KeepReasonText.RestingObjects(reason));
        var chain = $"Book [{TestTargets.Key(0)}] rests on it; Lantern [{OtherPlaced}] rests on that";
        Assert.Equal(chain, KeepReasonText.Detail(found.ObjectsToKeep.GetOwnReason(2)!));
        Assert.Equal(chain, KeepReasonText.RestingObjects(found.ObjectsToKeep.GetOwnReason(2)!));
        Assert.True(found.ObjectsToKeep.TryGetKeepReason(1, out var linked));
        Assert.Equal(KeepKind.LinkedGroup, linked.Kind);
        Assert.Equal(KeepKind.PlacedReference, found.ObjectsToKeep.GetOwnReason(3)!.Kind);
    }

    [Fact]
    public void TheLinksDecideTheGroupsTheKeptTargetsAndTheLinkCount()
    {
        var targets = TestTargets.CreateMany(4);
        LinkFact[] links =
        [
            Placed(TestTargets.Key(0), TestTargets.Key(1)),
            Placed(OtherPlaced, TestTargets.Key(3)),
        ];

        var found = ObjectsToKeepFinder.Find(targets, links, TestBaseFacts.Unresolved, NothingResting);

        Assert.Equal(1, found.LinkCount);
        Assert.Equal([0, 1], found.ObjectsToKeep.Groups.MembersOf(0));
        Assert.True(found.ObjectsToKeep.IsProtected(3));
        Assert.False(found.ObjectsToKeep.IsProtected(0));
        Assert.Empty(found.CheckFailures);
    }

    [Fact]
    public void AnObjectWhoseOwnScriptNamesAQuestIsKeptWithItsLinkedGroup()
    {
        var targets = TestTargets.CreateMany(3);
        targets[0] = targets[0] with { NamedByScript = new ScriptNamedRecord(BaseGameQuest, "SomeQuest", ScriptNamedKind.Quest) };

        var found = ObjectsToKeepFinder.Find(targets, [Placed(TestTargets.Key(1), TestTargets.Key(0))], TestBaseFacts.Unresolved, NothingResting);

        var reason = found.ObjectsToKeep.GetOwnReason(0)!;
        Assert.Equal(KeepKind.ScriptNamesQuestOrScene, reason.Kind);
        Assert.Equal("script names a quest or scene", KeepReasonText.Category(reason));
        Assert.Equal($"its own script names Quest SomeQuest [{BaseGameQuest}]", KeepReasonText.Detail(reason));
        Assert.True(found.ObjectsToKeep.TryGetKeepReason(1, out var linked));
        Assert.Equal(KeepKind.LinkedGroup, linked.Kind);
        Assert.False(found.ObjectsToKeep.IsProtected(2));
    }

    [Fact]
    public void AnObjectWhoseCleanedModBaseScriptNamesAQuestIsKept()
    {
        var targets = TestTargets.CreateMany(2);
        targets[0] = TestTargets.Create(0, TestTargets.At(default), TargetScriptedBase, TestTargets.Space);

        var found = ObjectsToKeepFinder.Find(targets, [], new ScriptedBases(), NothingResting);

        var reason = found.ObjectsToKeep.GetOwnReason(0)!;
        Assert.Equal(KeepKind.ScriptNamesQuestOrScene, reason.Kind);
        Assert.Equal($"its base object's script names Quest SomeQuest [{BaseGameQuest}]", KeepReasonText.Detail(reason));
        Assert.False(found.ObjectsToKeep.IsProtected(1));
    }

    [Fact]
    public void AnObjectWhoseOtherPluginBaseScriptNamesAQuestStaysRemovable()
    {
        List<TargetObject> targets = [TestTargets.Create(0, TestTargets.At(default), OtherScriptedBase, TestTargets.Space)];

        var found = ObjectsToKeepFinder.Find(targets, [], new ScriptedBases(), NothingResting);

        Assert.False(found.ObjectsToKeep.IsProtected(0));
    }

    [Fact]
    public void ALinkFromAnotherRecordComesBeforeTheScript()
    {
        var targets = TestTargets.CreateMany(1);
        targets[0] = targets[0] with { NamedByScript = new ScriptNamedRecord(BaseGameQuest, null, ScriptNamedKind.Scene) };

        var found = ObjectsToKeepFinder.Find(targets, [Placed(OtherPlaced, TestTargets.Key(0))], TestBaseFacts.Unresolved, NothingResting);

        Assert.Equal(KeepKind.PlacedReference, found.ObjectsToKeep.GetOwnReason(0)!.Kind);
    }

    [Fact]
    public void AnObjectWithALocationRefTypeIsKeptButAMapMarkerWithOneIsNot()
    {
        var targets = TestTargets.CreateMany(2);
        targets[0] = targets[0] with { HasLocationRefType = true };
        targets[1] = targets[1] with { HasLocationRefType = true, HasMapMarker = true };

        var found = ObjectsToKeepFinder.Find(targets, [], TestBaseFacts.Unresolved, NothingResting);

        var reason = found.ObjectsToKeep.GetOwnReason(0)!;
        Assert.Equal(KeepKind.LocationRole, reason.Kind);
        Assert.Equal("it has a location ref type, by which a quest can pick it", KeepReasonText.Detail(reason));
        Assert.False(found.ObjectsToKeep.IsProtected(1));
    }

    [Fact]
    public void AUniqueActorNpcAndALargeReferenceStayRemovableButAreListedAsRisks()
    {
        var npcBase = new BaseKey(new FormKey(ModKey.FromNameAndExtension("Target.esp"), 0x700).ToRecordKey(), BaseLinkKind.Npc);
        var otherNpcBase = npcBase with { Record = npcBase.Record with { Id = 0x701 } };
        List<TargetObject> targets =
        [
            TestTargets.Create(0, TestTargets.At(default), otherNpcBase, TestTargets.Space),
            TestTargets.Create(1, TestTargets.At(default), npcBase, TestTargets.Space),
            TestTargets.Create(2, TestTargets.At(default)),
        ];
        var firstQuest = new LinkFact(BaseGameQuest, "FirstQuest", "Quest", npcBase.Record, RelationKind.UniqueActor, SourceIsPlaced: false, SourceIsWorldspace: false, AliasFillFlags.None);
        var secondQuest = firstQuest with { SourceEditorId = "SecondQuest" };
        var largeReference = new LinkFact(OtherPlaced, "World", "Worldspace", TestTargets.Key(2), RelationKind.Other, SourceIsPlaced: false, SourceIsWorldspace: true, AliasFillFlags.None);

        var found = ObjectsToKeepFinder.Find(targets, [firstQuest, secondQuest, largeReference], TestBaseFacts.Unresolved, NothingResting);

        Assert.Equal([new UniqueActorRole(new TargetId(1), firstQuest)], found.Risks.UniqueActors.ToArray());
        Assert.Equal([new TargetId(2)], found.Risks.LargeReferences.ToArray());
        Assert.All(Enumerable.Range(0, 3), index => Assert.False(found.ObjectsToKeep.IsProtected(index)));
    }

    [Theory]
    [InlineData(AliasFillFlags.Optional)]
    [InlineData(AliasFillFlags.AllowDisabled)]
    public void AUniqueActorAliasThatCanStayEmptyOrFillWithADisabledNpcIsNoRisk(AliasFillFlags flags)
    {
        var npcBase = new BaseKey(new FormKey(ModKey.FromNameAndExtension("Target.esp"), 0x700).ToRecordKey(), BaseLinkKind.Npc);
        List<TargetObject> targets = [TestTargets.Create(0, TestTargets.At(default), npcBase, TestTargets.Space)];
        var lenientQuest = new LinkFact(
            BaseGameQuest, "LenientQuest", "Quest", npcBase.Record, RelationKind.UniqueActor, SourceIsPlaced: false, SourceIsWorldspace: false, flags);
        var requiredQuest = lenientQuest with { SourceEditorId = "RequiredQuest", AliasFlags = AliasFillFlags.None };

        Assert.Empty(ObjectsToKeepFinder.Find(targets, [lenientQuest], TestBaseFacts.Unresolved, NothingResting).Risks.UniqueActors);
        Assert.Equal(
            [new UniqueActorRole(new TargetId(0), requiredQuest)],
            ObjectsToKeepFinder.Find(targets, [lenientQuest, requiredQuest], TestBaseFacts.Unresolved, NothingResting).Risks.UniqueActors.ToArray());
    }

    private static LinkFact Placed(RecordKey source, RecordKey target) =>
        new(source, null, "PlacedObject", target, RelationKind.EnableParent, SourceIsPlaced: true, SourceIsWorldspace: false, AliasFillFlags.None);

    private sealed class ScriptedBases : IBaseFacts
    {
        public BaseFacts Of(BaseKey baseKey) =>
            BaseFacts.Unresolved(baseKey.Record) with { NamedByScript = new ScriptNamedRecord(BaseGameQuest, "SomeQuest", ScriptNamedKind.Quest) };
    }
}
