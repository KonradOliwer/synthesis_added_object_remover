using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.LoadOrder;

public class LinkReaderTests
{
    private static readonly ModKey OtherMod = ModKey.FromNameAndExtension("Other.esp");

    [Fact]
    public void PlacedLinkIntoTheTargetPluginIsReadWithItsRelation()
    {
        var source = WithEnableParent(TestTargets.FormKeyOf(0), TestTargets.FormKeyOf(1));

        var facts = ReadFromPlaced(source);

        var fact = Assert.Single(facts);
        Assert.Equal(TestTargets.Key(0), fact.Source);
        Assert.Equal(TestTargets.Key(1), fact.Target);
        Assert.Equal(RelationKind.EnableParent, fact.Relation);
        Assert.True(fact.SourceIsPlaced);
    }

    [Fact]
    public void LinkToAnotherPluginIsIgnored()
    {
        var source = WithEnableParent(TestTargets.FormKeyOf(0), new FormKey(OtherMod, 0x901));

        Assert.Empty(ReadFromPlaced(source));
    }

    [Fact]
    public void BaseObjectLinkIsReadAsABaseObjectRelation()
    {
        var targetBase = new FormKey(TestTargets.TargetMod, 0x700);
        var source = new PlacedObject(new FormKey(OtherMod, 0x903), SkyrimRelease.SkyrimSE);
        source.Base.SetTo(targetBase);

        var fact = Assert.Single(ReadFromPlaced(source));

        Assert.Equal(RelationKind.BaseObject, fact.Relation);
    }

    [Fact]
    public void NonPlacedRecordLinkIsReadOnlyForTheGivenTargets()
    {
        var mod = new SkyrimMod(OtherMod, SkyrimRelease.SkyrimSE);
        var list = new FormList(new FormKey(OtherMod, 0x902), SkyrimRelease.SkyrimSE) { EditorID = "OtherList" };
        list.Items.Add(new FormLink<ISkyrimMajorRecordGetter>(TestTargets.FormKeyOf(2)));
        list.Items.Add(new FormLink<ISkyrimMajorRecordGetter>(TestTargets.FormKeyOf(4)));
        mod.FormLists.Add(list);
        var facts = new List<LinkFact>();

        LinkReader.ReadFromNonPlaced(
            [mod], new HashSet<RecordKey> { TestTargets.Key(2), TestTargets.Key(3) }, TestTargets.TargetPlugin, new PluginNames(), facts);

        var fact = Assert.Single(facts);
        Assert.Equal(TestTargets.Key(2), fact.Target);
        Assert.Equal("FormList", fact.SourceRecordType);
        Assert.Equal("OtherList", fact.SourceEditorId);
        Assert.False(fact.SourceIsPlaced);
    }

    [Fact]
    public void WorldspaceLinkIsReadAsAWorldspaceSourceAndKeepsNothing()
    {
        var mod = new SkyrimMod(OtherMod, SkyrimRelease.SkyrimSE);
        var worldspace = new Worldspace(new FormKey(OtherMod, 0x904), SkyrimRelease.SkyrimSE);
        worldspace.Climate.SetTo(TestTargets.FormKeyOf(1));
        mod.Worldspaces.Add(worldspace);
        var facts = new List<LinkFact>();

        LinkReader.ReadFromNonPlaced(
            [mod], new HashSet<RecordKey> { TestTargets.Key(1) }, TestTargets.TargetPlugin, new PluginNames(), facts);

        Assert.True(Assert.Single(facts).SourceIsWorldspace);
        var outcome = LinkRule.DecideLinkOutcome(TestTargets.CreateMany(4), facts);
        Assert.All(outcome.References, Assert.Null);
    }

    private static List<LinkFact> ReadFromPlaced(IPlacedGetter record)
    {
        var facts = new List<LinkFact>();
        LinkReader.ReadFromPlaced(record, record.FormKey.ToRecordKey(), TestTargets.TargetPlugin, new PluginNames(), facts);
        return facts;
    }

    private static PlacedObject WithEnableParent(FormKey source, FormKey parent)
    {
        var enableParent = new EnableParent();
        enableParent.Reference.SetTo(parent);
        return new PlacedObject(source, SkyrimRelease.SkyrimSE) { EnableParent = enableParent };
    }
}
