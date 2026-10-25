using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.LoadOrder;

public class LinkReaderTests
{
    private static readonly ModKey OtherMod = ModKey.FromNameAndExtension("Other.esp");

    [Fact]
    public void PlacedLinkIntoTheTargetIsReadWithItsRelation()
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

        var facts = ReadNonPlacedLinks(mod,new HashSet<RecordKey> { TestTargets.Key(2), TestTargets.Key(3) });

        var fact = Assert.Single(facts);
        Assert.Equal(TestTargets.Key(2), fact.Target);
        Assert.Equal("FormList", fact.SourceRecordType);
        Assert.Equal("OtherList", fact.SourceEditorId);
        Assert.False(fact.SourceIsPlaced);
    }

    [Fact]
    public void ALocationListingARecordWithARefTypeIsReadAsALocationRefTypeRelation()
    {
        var mod = new SkyrimMod(OtherMod, SkyrimRelease.SkyrimSE);
        var location = new Location(new FormKey(OtherMod, 0x905), SkyrimRelease.SkyrimSE)
        {
            LocationRefTypeReferencesStatic = [new LocationRefTypeReference { Ref = new FormLink<IPlacedGetter>(TestTargets.FormKeyOf(1)) }],
        };
        mod.Locations.Add(location);

        var facts = ReadNonPlacedLinks(mod,new HashSet<RecordKey> { TestTargets.Key(1) });

        Assert.Equal(RelationKind.LocationRefType, Assert.Single(facts).Relation);
    }

    [Fact]
    public void ALocationsWorldAndHorseMarkersAreReadAsOtherLinks()
    {
        var mod = new SkyrimMod(OtherMod, SkyrimRelease.SkyrimSE);
        var location = new Location(new FormKey(OtherMod, 0x905), SkyrimRelease.SkyrimSE);
        location.WorldLocationMarkerRef.SetTo(TestTargets.FormKeyOf(1));
        location.HorseMarkerRef.SetTo(TestTargets.FormKeyOf(2));
        mod.Locations.Add(location);

        var facts = ReadNonPlacedLinks(mod,new HashSet<RecordKey> { TestTargets.Key(1), TestTargets.Key(2) });

        Assert.Equal([TestTargets.Key(1), TestTargets.Key(2)], facts.Select(fact => fact.Target).ToArray());
        Assert.All(facts, fact => Assert.Equal(RelationKind.Other, fact.Relation));
    }

    [Fact]
    public void ALocationsAutomaticAndRemovedListsAreNotRead()
    {
        var mod = new SkyrimMod(OtherMod, SkyrimRelease.SkyrimSE);
        var listed = TestTargets.FormKeyOf(1);
        var location = new Location(new FormKey(OtherMod, 0x905), SkyrimRelease.SkyrimSE)
        {
            PersistentActorReferencesAdded = [new PersistentActorReference { Actor = new FormLink<IPlacedNpcGetter>(listed) }],
            PersistentActorReferencesStatic = [new PersistentActorReference { Actor = new FormLink<IPlacedNpcGetter>(listed) }],
            PersistentActorReferencesRemoved = [new FormLink<IPlacedSimpleGetter>(listed)],
            UniqueActorReferencesAdded = [new UniqueActorReference { Ref = new FormLink<IPlacedNpcGetter>(listed) }],
            UniqueActorReferencesStatic = [new UniqueActorReference { Ref = new FormLink<IPlacedNpcGetter>(listed) }],
            LocationRefTypeReferencesRemoved = [new FormLink<IPlacedSimpleGetter>(listed)],
            InitiallyDisabledReferencesAdded = [new FormLink<IPlacedGetter>(listed)],
            InitiallyDisabledReferencesStatic = [new FormLink<IPlacedGetter>(listed)],
            EnableParentReferencesAdded = [new EnableParentReference { Actor = new FormLink<IPlacedGetter>(listed), Ref = new FormLink<IPlacedGetter>(listed) }],
            EnableParentReferencesStatic = [new EnableParentReference { Actor = new FormLink<IPlacedGetter>(listed), Ref = new FormLink<IPlacedGetter>(listed) }],
        };
        mod.Locations.Add(location);

        var facts = ReadNonPlacedLinks(mod,new HashSet<RecordKey> { TestTargets.Key(1) });

        Assert.Empty(facts);
    }

    [Fact]
    public void ALocationAddingARecordWithARefTypeIsReadAsALocationRefTypeRelation()
    {
        var mod = new SkyrimMod(OtherMod, SkyrimRelease.SkyrimSE);
        var location = new Location(new FormKey(OtherMod, 0x905), SkyrimRelease.SkyrimSE)
        {
            LocationRefTypeReferencesAdded = [new LocationRefTypeReference { Ref = new FormLink<IPlacedGetter>(TestTargets.FormKeyOf(1)) }],
        };
        mod.Locations.Add(location);

        var facts = ReadNonPlacedLinks(mod,new HashSet<RecordKey> { TestTargets.Key(1) });

        Assert.Equal(RelationKind.LocationRefType, Assert.Single(facts).Relation);
    }

    [Fact]
    public void AQuestAliasFillingWithATargetNpcAsUniqueActorIsReadForTheNpcBase()
    {
        var mod = new SkyrimMod(OtherMod, SkyrimRelease.SkyrimSE);
        var npcBase = new FormKey(TestTargets.TargetMod, 0x700);
        var quest = new Quest(new FormKey(OtherMod, 0x906), SkyrimRelease.SkyrimSE) { EditorID = "SomeQuest" };
        quest.Aliases.Add(new QuestAlias { UniqueActor = new FormLinkNullable<INpcGetter>(npcBase) });
        quest.Aliases.Add(new QuestAlias { UniqueActor = new FormLinkNullable<INpcGetter>(new FormKey(OtherMod, 0x907)) });
        mod.Quests.Add(quest);

        var facts = ReadNonPlacedLinks(mod,new HashSet<RecordKey>());

        var fact = Assert.Single(facts);
        Assert.Equal(RelationKind.UniqueActor, fact.Relation);
        Assert.Equal(npcBase.ToRecordKey(), fact.Target);
        Assert.Equal("SomeQuest", fact.SourceEditorId);
        Assert.Equal(AliasFillFlags.None, fact.AliasFlags);
    }

    [Theory]
    [InlineData(QuestAlias.Flag.Optional, AliasFillFlags.Optional)]
    [InlineData(QuestAlias.Flag.AllowDisabled, AliasFillFlags.AllowDisabled)]
    [InlineData(QuestAlias.Flag.Optional | QuestAlias.Flag.AllowDisabled | QuestAlias.Flag.Essential, AliasFillFlags.Optional | AliasFillFlags.AllowDisabled)]
    public void AUniqueActorAliasIsReadWithItsOptionalAndAllowDisabledFlags(QuestAlias.Flag aliasFlags, AliasFillFlags expected)
    {
        var mod = new SkyrimMod(OtherMod, SkyrimRelease.SkyrimSE);
        var quest = new Quest(new FormKey(OtherMod, 0x906), SkyrimRelease.SkyrimSE);
        quest.Aliases.Add(new QuestAlias { UniqueActor = new FormLinkNullable<INpcGetter>(new FormKey(TestTargets.TargetMod, 0x700)), Flags = aliasFlags });
        mod.Quests.Add(quest);

        var facts = ReadNonPlacedLinks(mod,new HashSet<RecordKey>());

        Assert.Equal(expected, Assert.Single(facts).AliasFlags);
    }

    [Fact]
    public void WorldspaceLinkIsReadAsAWorldspaceSourceAndKeepsNothing()
    {
        var mod = new SkyrimMod(OtherMod, SkyrimRelease.SkyrimSE);
        var worldspace = new Worldspace(new FormKey(OtherMod, 0x904), SkyrimRelease.SkyrimSE);
        worldspace.Climate.SetTo(TestTargets.FormKeyOf(1));
        mod.Worldspaces.Add(worldspace);

        var facts = ReadNonPlacedLinks(mod,new HashSet<RecordKey> { TestTargets.Key(1) });

        Assert.True(Assert.Single(facts).SourceIsWorldspace);
        var outcome = LinkRule.DecideLinkOutcome(TestTargets.CreateMany(4), facts);
        Assert.All(outcome.References, Assert.Null);
    }

    [Fact]
    public void EveryRecordAPlacedRecordLinksToButItsBaseAndItselfIsCollectedInTheSameWalk()
    {
        var parent = new FormKey(OtherMod, 0x901);
        var linkedReference = new FormKey(OtherMod, 0x902);
        var source = WithEnableParent(new FormKey(OtherMod, 0x903), parent);
        source.Base.SetTo(new FormKey(OtherMod, 0x904));
        source.LinkedReferences.Add(new LinkedReferences { Reference = new FormLink<IPlacedGetter>(linkedReference) });
        source.LinkedReferences.Add(new LinkedReferences { Reference = new FormLink<IPlacedGetter>(source.FormKey) });
        var linked = new HashSet<RecordKey>();

        var facts = ReadFromPlaced(source, linked);

        Assert.Empty(facts);
        Assert.Equal(new HashSet<RecordKey> { parent.ToRecordKey(), linkedReference.ToRecordKey() }, linked);
    }

    private static PluginNames CreateNames() => new(new KnownPluginNames([TestTargets.TargetName, OtherMod.ToPluginName()]));

    private static List<LinkFact> ReadNonPlacedLinks(ISkyrimModGetter mod, IReadOnlySet<RecordKey> records) =>
        [.. LinkReader.ReadFromNonPlaced([mod], records, TestTargets.TargetName, CreateNames(), new Execution(1)).Links];

    private static List<LinkFact> ReadFromPlaced(IPlacedGetter record, ISet<RecordKey>? linkedRecords = null)
    {
        var facts = new List<LinkFact>();
        LinkReader.ReadFromPlaced(record, record.FormKey.ToRecordKey(), linked => linked.Plugin == TestTargets.TargetName, CreateNames(), facts, linkedRecords);
        return facts;
    }

    private static PlacedObject WithEnableParent(FormKey source, FormKey parent)
    {
        var enableParent = new EnableParent();
        enableParent.Reference.SetTo(parent);
        return new PlacedObject(source, SkyrimRelease.SkyrimSE) { EnableParent = enableParent };
    }
}
