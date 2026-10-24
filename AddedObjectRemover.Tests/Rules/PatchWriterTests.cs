using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

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
        var writing = new RecordingWriting();

        var written = PatchWriter.Write(final, [.. targets], OtherModsObjectsResult.SwitchedOff, new ReadPlugin(), writing);

        Assert.Equal([0, 4, 1, 2], written.Removed.Select(id => id.Index));
        Assert.Equal(
            written.Removed.Select(id => targets[id.Index].Key),
            writing.Orders.OfType<SetInitiallyDisabled>().Select(order => order.Record));
        Assert.Equal(0, written.Moved);
    }

    [Fact]
    public void TheWritingCopiesFromTheRecordsThePluginRead()
    {
        var targets = TestTargets.CreateMany(1);
        var protection = ObjectsToKeep.Build(targets, [], TestTargets.References(1), TestBaseFacts.Unresolved);
        var final = RemovalDecisions.Start(protection, 1).Apply(RoundKind.TooClose, [Propose(0)]);
        var plugin = new ReadPlugin();
        var writing = new RecordingWriting();

        PatchWriter.Write(final, [.. targets], OtherModsObjectsResult.SwitchedOff, plugin, writing);

        Assert.Same(plugin.Records, writing.Records);
    }

    [Fact]
    public void APersistentObjectIsDisabledInPlaceAndOnlyANonPersistentOneIsMovedBelowTheWorld()
    {
        var targets = TestTargets.CreateMany(2);
        targets[1] = targets[1] with { IsPersistent = true };
        var protection = ObjectsToKeep.Build(targets, [], TestTargets.References(2), TestBaseFacts.Unresolved);
        var final = RemovalDecisions.Start(protection, 2).Apply(RoundKind.TooClose, [Propose(0), Propose(1)]);
        var writing = new RecordingWriting();

        PatchWriter.Write(final, [.. targets], OtherModsObjectsResult.SwitchedOff, new ReadPlugin(), writing);

        Assert.Equal([targets[0].Key, targets[1].Key], writing.Orders.OfType<SetInitiallyDisabled>().Select(order => order.Record));
        var move = Assert.Single(writing.Orders.OfType<SetPosition>());
        Assert.Equal(targets[0].Key, move.Record);
        Assert.Equal(-30000f, move.Position.Z);
    }

    [Fact]
    public void AKeptMarkersMoveIsWrittenToItsNewPositionAndCountedAsMoved()
    {
        var targets = TestTargets.CreateMany(TargetCount);
        var protection = ObjectsToKeep.Build(targets, [], TestTargets.References(TargetCount), TestBaseFacts.Unresolved);
        var markerMove = new MarkerMove(new TargetId(3), Vector3.Zero, new Vector3(1, 2, 3));
        var final = RemovalDecisions.Start(protection, TargetCount).AddMoves([markerMove]);
        var writing = new RecordingWriting();

        var written = PatchWriter.Write(final, [.. targets], OtherModsObjectsResult.SwitchedOff, new ReadPlugin(), writing);

        Assert.Equal<WriteOrder>([new SetPosition(targets[3].Key, new Vector3(1, 2, 3))], writing.Orders);
        Assert.Equal(1, written.Moved);
    }

    [Fact]
    public void AnObjectShrunkOrTurnedWhereItStandsIsWrittenAtItsNewPlacementAndNotDisabled()
    {
        var targets = TestTargets.CreateMany(1);
        var protection = ObjectsToKeep.Build(targets, [], TestTargets.References(1), TestBaseFacts.Unresolved);
        var to = new EulerPlacement(Vector3.Zero, new Vector3(0, 0, 0.25f), 0.8f);
        var final = RemovalDecisions.Start(protection, 1)
            .ApplyFits([new ProposedFit(new TargetId(0), Propose(0).Reason, to)])
            .Apply(RoundKind.TooClose, [Propose(0)]);
        var writing = new RecordingWriting();

        var written = PatchWriter.Write(final, [.. targets], OtherModsObjectsResult.SwitchedOff, new ReadPlugin(), writing);

        Assert.Equal<WriteOrder>([new SetPlacement(targets[0].Key, to)], writing.Orders);
        Assert.Equal((0, 1), (written.Removed.Length, written.Fitted));
    }

    [Fact]
    public void OtherModsObjectsAreRemovedOrPulledDownAfterTheTargetObjectsAndTheOthersLeftAlone()
    {
        var targets = TestTargets.CreateMany(1);
        var protection = ObjectsToKeep.Build(targets, [], TestTargets.References(1), TestBaseFacts.Unresolved);
        var final = RemovalDecisions.Start(protection, 1).Apply(RoundKind.TooClose, [Propose(0)]);
        var other = ModKey.FromNameAndExtension("Other.esp");
        var removed = TestShapes.Placed(other, 0, default, new Vector3(1, 2, 3));
        var pulled = TestShapes.Placed(other, 1, default, new Vector3(4, 5, 60));
        var left = TestShapes.Placed(other, 2, default, new Vector3(7, 8, 9));
        var settled = new OtherModsObjectsResult(
            StepOutcome.Performed,
            [
                new OtherModsObjectOutcome(removed, OtherModsObjectFate.RemovedNothingBelow, LostShare: 1f, Drop: 0f, ReferenceRoles.None),
                new OtherModsObjectOutcome(pulled, OtherModsObjectFate.PulledDown, LostShare: 1f, Drop: 50f, ReferenceRoles.None),
                new OtherModsObjectOutcome(left, OtherModsObjectFate.LeftNotRemovable, LostShare: 1f, Drop: 0f, ReferenceRoles.Persistent),
            ],
            Rounds: [],
            ProposedRounds: [],
            Failure: null);
        var writing = new RecordingWriting();

        var written = PatchWriter.Write(final, [.. targets], settled, new ReadPlugin(), writing);

        Assert.Equal<WriteOrder>(
            [
                new SetInitiallyDisabled(targets[0].Key),
                new SetPosition(targets[0].Key, targets[0].Transform.Position with { Z = -30000f }),
                new SetInitiallyDisabled(removed.Key),
                new SetPosition(removed.Key, new Vector3(1, 2, -30000f)),
                new SetPosition(pulled.Key, new Vector3(4, 5, 10)),
            ],
            writing.Orders);
        Assert.Equal((1, 1), (written.OtherModsObjectsRemoved, written.OtherModsObjectsPulledDown));
    }

    private static ProposedRemoval Propose(int index) => new(new TargetId(index), new RemovalReason.TooClose(new OtherId(0), new TooCloseCause.ObjectSize()));

    private sealed class RecordingWriting : IPatchWriting
    {
        public List<WriteOrder> Orders { get; } = [];

        public IWritableRecords? Records { get; private set; }

        public void Write(IWritableRecords records, IReadOnlyList<WriteOrder> orders)
        {
            Records = records;
            Orders.AddRange(orders);
        }
    }

    private sealed class NoRecords : IWritableRecords;

    /// <summary>A reader whose placed records were read: none has an enable parent.</summary>
    private sealed class ReadPlugin : IPluginRecords
    {
        public IWritableRecords Records { get; } = new NoRecords();

        public IWritableRecords ReadWritableRecords() => Records;

        public IRecordIndex ReadRecordIndex() => throw new NotSupportedException();

        public bool TryParsePluginName(string text, out PluginName name) => throw new NotSupportedException();

        public PlacedRecordFacts ReadPlacedRecords(PlacedReadScope scope, Func<PluginName, bool> stopsTheWalk) => throw new NotSupportedException();

        public NonPlacedLinks ReadLinks(PluginName target, IReadOnlySet<RecordKey> targets) => throw new NotSupportedException();

        public CellGridPoint? ReadHomeCellGrid(RecordKey record) => throw new NotSupportedException();

        public float[]? ReadTerrain(ExteriorCell cell) => throw new NotSupportedException();

        public TerrainVersion[] ReadTerrainVersions(ExteriorCell cell) => throw new NotSupportedException();

        public WaterCopies ReadWaterCopies(ExteriorCell cell) => throw new NotSupportedException();
        public RecordKey[] ReadTextureMaterialChain(RecordKey texture) => throw new NotSupportedException();

        public RecordKey? FindLandWorldspace(RecordKey space) => throw new NotSupportedException();

        public MeshTriangle[]? ReadNavmeshes(NavmeshBucket bucket) => throw new NotSupportedException();

        public NpcTraits? NpcTraitsOf(BaseKey npc) => throw new NotSupportedException();

        public TraitSupplier? TraitSupplierOf(BaseKey spawn, NpcTemplateFlag templateFlag) => throw new NotSupportedException();

        public IReadOnlyList<RecordKey> LeveledEntriesOf(RecordKey list, NpcTemplateFlag templateFlag) => throw new NotSupportedException();
    }
}
