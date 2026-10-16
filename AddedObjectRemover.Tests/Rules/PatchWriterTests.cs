using System.Collections.Immutable;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Rules;

public sealed class PatchWriterTests
{
    private const int TargetCount = 5;

    [Fact]
    public void RemovalsAreWrittenRoundByRoundDirectBeforeLinkedOnes()
    {
        var targets = TestTargets.CreateMany(TargetCount);
        var protection = ObjectsToKeep.Build(targets, [TestTargets.Link(1, 2)], TestTargets.References(TargetCount, new Dictionary<int, KeepReason>()), TestBaseFacts.Unresolved);
        var final = RemovalDecisions.Start(protection, TargetCount)
            .Apply(RoundKind.TooClose, [Propose(4), Propose(0)])
            .Apply(RoundKind.AlsoRemove, [Propose(1)]);
        var plugin = new RecordingPlugin();

        var written = PatchWriter.Write(final, [.. targets], plugin);

        Assert.Equal([0, 4, 1, 2], written.Removed.Select(id => id.Index));
        Assert.Equal(
            written.Removed.Select(id => targets[id.Index].Key),
            plugin.Orders.OfType<SetInitiallyDisabled>().Select(order => order.Record));
        Assert.Equal(0, written.Moved);
    }

    [Fact]
    public void APersistentObjectIsDisabledInPlaceAndOnlyANonPersistentOneIsMovedBelowTheWorld()
    {
        var targets = TestTargets.CreateMany(2);
        targets[1] = targets[1] with { IsPersistent = true };
        var protection = ObjectsToKeep.Build(targets, [], TestTargets.References(2), TestBaseFacts.Unresolved);
        var final = RemovalDecisions.Start(protection, 2).Apply(RoundKind.TooClose, [Propose(0), Propose(1)]);
        var plugin = new RecordingPlugin();

        PatchWriter.Write(final, [.. targets], plugin);

        Assert.Equal([targets[0].Key, targets[1].Key], plugin.Orders.OfType<SetInitiallyDisabled>().Select(order => order.Record));
        var move = Assert.Single(plugin.Orders.OfType<SetPosition>());
        Assert.Equal(targets[0].Key, move.Record);
        Assert.Equal(-30000f, move.Position.Z);
    }

    private static ProposedRemoval Propose(int index) => new(new TargetId(index), new RemovalReason.TooClose(new OtherId(0), new TooCloseCause.ObjectSize()));

    private sealed class RecordingPlugin : IPluginRecords
    {
        public List<WriteOrder> Orders { get; } = [];

        public bool HasEnableParent(RecordKey record) => false;

        public void Write(IReadOnlyList<WriteOrder> orders) => Orders.AddRange(orders);

        public bool TryParsePluginName(string text, out PluginName name) => throw new NotSupportedException();

        public PlacedRecordFacts ReadPlacedRecords(PlacedReadScope scope) => throw new NotSupportedException();

        public ImmutableArray<LinkFact> ReadLinks(PluginName target, IReadOnlySet<RecordKey> targets) => throw new NotSupportedException();

        public CellGridPoint? ReadHomeCellGrid(RecordKey record) => throw new NotSupportedException();

        public float[]? ReadTerrain(ExteriorCell cell) => throw new NotSupportedException();

        public TerrainVersion[] ReadTerrainVersions(ExteriorCell cell) => throw new NotSupportedException();
        public RecordKey[] ReadTextureMaterialChain(RecordKey texture) => throw new NotSupportedException();

        public RecordKey? FindLandWorldspace(RecordKey space) => throw new NotSupportedException();

        public MeshTriangle[]? ReadNavmeshes(NavmeshBucket bucket) => throw new NotSupportedException();

        public NpcTraits? NpcTraitsOf(BaseKey npc) => throw new NotSupportedException();

        public TraitSupplier? TraitSupplierOf(BaseKey spawn, NpcTemplateFlag templateFlag) => throw new NotSupportedException();

        public IReadOnlyList<RecordKey> LeveledEntriesOf(RecordKey list, NpcTemplateFlag templateFlag) => throw new NotSupportedException();
    }
}
