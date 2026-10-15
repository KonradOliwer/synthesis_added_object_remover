using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.LoadOrder;

public class ObjectsToKeepFinderTests
{
    private static readonly RecordKey OtherPlaced = new FormKey(ModKey.FromNameAndExtension("Other.esp"), 0x900).ToRecordKey();

    [Fact]
    public void TheLinksDecideTheGroupsTheKeptTargetsAndTheLinkCount()
    {
        var targets = TestTargets.CreateMany(4);
        LinkFact[] links =
        [
            Placed(TestTargets.Key(0), TestTargets.Key(1)),
            Placed(OtherPlaced, TestTargets.Key(3)),
        ];

        var found = ObjectsToKeepFinder.Find(targets, links);

        Assert.Equal(1, found.LinkCount);
        Assert.Equal([0, 1], found.ObjectsToKeep.Groups.MembersOf(0));
        Assert.True(found.ObjectsToKeep.IsProtected(3));
        Assert.False(found.ObjectsToKeep.IsProtected(0));
        Assert.Empty(found.CheckFailures);
    }

    private static LinkFact Placed(RecordKey source, RecordKey target) =>
        new(source, null, "PlacedObject", target, RelationKind.EnableParent, SourceIsPlaced: true, SourceIsWorldspace: false);
}
