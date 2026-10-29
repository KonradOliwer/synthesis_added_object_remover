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
        var final = TestSeededDecisions.StartDisablingAll(protection, TargetCount)
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
        var final = TestSeededDecisions.StartDisablingAll(protection, 1).Apply(RoundKind.TooClose, [Propose(0)]);
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
        var final = TestSeededDecisions.StartDisablingAll(protection, 2).Apply(RoundKind.TooClose, [Propose(0), Propose(1)]);
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
        var final = TestSeededDecisions.StartDisablingAll(protection, TargetCount).AddMoves([markerMove]);
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
        var final = TestSeededDecisions.StartDisablingAll(protection, 1)
            .ApplyFits([new ProposedFit(new TargetId(0), Propose(0).Reason, to)])
            .Apply(RoundKind.TooClose, [Propose(0)]);
        var writing = new RecordingWriting();

        var written = PatchWriter.Write(final, [.. targets], OtherModsObjectsResult.SwitchedOff, new ReadPlugin(), writing);

        Assert.Equal<WriteOrder>([new SetPlacement(targets[0].Key, to.Position, to.Rotation, to.Scale)], writing.Orders);
        Assert.Equal((0, 1), (written.Removed.Length, written.Fitted));
    }

    [Fact]
    public void OtherModsObjectsAreRemovedOrPulledDownAfterTheTargetObjectsAndTheOthersLeftAlone()
    {
        var targets = TestTargets.CreateMany(1);
        var protection = ObjectsToKeep.Build(targets, [], TestTargets.References(1), TestBaseFacts.Unresolved);
        var final = TestSeededDecisions.StartDisablingAll(protection, 1).Apply(RoundKind.TooClose, [Propose(0)]);
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

    [Fact]
    public void ARestoredObjectIsWrittenAtItsEarlierPlacementWithTheScaleTheEarlierVersionStoredAndIsNeitherDisabledNorSunk()
    {
        var target = TestTargets.Create(0, TestTargets.At(new Vector3(500, 0, 0)));
        var earlier = EarlierAt(new EulerPlacement(new Vector3(1, 2, 3), new Vector3(0, 0, 0.5f), 1f), storedScale: null);

        var (written, writing) = WriteOne(target, new WhenRemoved.Restore(earlier));

        Assert.Equal<WriteOrder>([new SetPlacement(target.Key, new Vector3(1, 2, 3), new Vector3(0, 0, 0.5f), null)], writing.Orders);
        Assert.Equal([0], written.Restored.Select(id => id.Index));
        Assert.Empty(written.Removed);
    }

    [Fact]
    public void ARestoredObjectWhoseEarlierVersionStoredAScaleWritesThatScale()
    {
        var target = TestTargets.Create(0, TestTargets.At(Vector3.Zero));
        var earlier = EarlierAt(new EulerPlacement(Vector3.One, Vector3.Zero, 1.25f), storedScale: 1.25f);

        var (_, writing) = WriteOne(target, new WhenRemoved.Restore(earlier));

        Assert.Equal<WriteOrder>([new SetPlacement(target.Key, Vector3.One, Vector3.Zero, 1.25f)], writing.Orders);
    }

    [Fact]
    public void ARestoredObjectWhoseBaseChangedIsWrittenAsTheWholeEarlierRecord()
    {
        var target = TestTargets.Create(0, TestTargets.At(Vector3.Zero));
        var earlier = EarlierAt(target.Placement, storedScale: null) with { RestoresWholeRecord = true };

        var (_, writing) = WriteOne(target, new WhenRemoved.Restore(earlier));

        Assert.Equal<WriteOrder>([new CopyVersion(target.Key)], writing.Orders);
    }

    [Fact]
    public void ARestoredExteriorObjectGoesBackIntoItsEarlierCellBeforeAnythingElseIsWrittenForIt()
    {
        var earlierCell = new CellFact(TestTargets.SpaceKey(0x201), null);
        var target = TestTargets.Create(0, TestTargets.At(Vector3.Zero)) with { Cell = new CellFact(TestTargets.SpaceKey(0x200), null) };
        var earlier = EarlierAt(target.Placement, storedScale: null) with { Cell = earlierCell };

        var (_, writing) = WriteOne(target, new WhenRemoved.Restore(earlier));

        Assert.Equal<WriteOrder>(
            [new PlaceInCell(target.Key, earlierCell.Key), new SetPlacement(target.Key, Vector3.Zero, Vector3.Zero, null)], writing.Orders);
    }

    [Fact]
    public void ARestoredObjectStaysInItsCellWhenItIsPersistentOrTheEarlierVersionIsOrItStandsInTheSameInterior()
    {
        var current = new CellFact(TestTargets.SpaceKey(0x200), null);
        var other = new CellFact(TestTargets.SpaceKey(0x201), null);
        var exterior = TestTargets.Create(0, TestTargets.At(Vector3.Zero)) with { Cell = current };
        var earlier = EarlierAt(exterior.Placement, storedScale: null) with { Cell = other };

        var persistentNow = WriteOne(exterior with { IsPersistent = true }, new WhenRemoved.Restore(earlier)).Writing.Orders;
        var persistentBefore = WriteOne(exterior, new WhenRemoved.Restore(earlier with { IsPersistent = true })).Writing.Orders;
        var sameInterior = WriteOne(exterior with { Cell = null }, new WhenRemoved.Restore(earlier with { Cell = new CellFact(exterior.SpaceKey, null) })).Writing.Orders;

        Assert.All([persistentNow, persistentBefore, sameInterior], orders => Assert.Empty(orders.OfType<PlaceInCell>()));
    }

    [Fact]
    public void RestoresAreWrittenAfterTheFitsAndBeforeTheMarkerMoves()
    {
        var targets = TestTargets.CreateMany(4);
        var protection = ObjectsToKeep.Build(targets, [], TestTargets.References(4), TestBaseFacts.Unresolved);
        var restoreTo = new WhenRemoved.Restore(EarlierAt(targets[2].Placement, storedScale: null));
        var final = RemovalDecisions.Start(protection, [new WhenRemoved.Disable(), new WhenRemoved.Disable(), restoreTo, new WhenRemoved.Disable()])
            .ApplyFits([new ProposedFit(new TargetId(1), Propose(1).Reason, targets[1].Placement with { Scale = 0.9f })])
            .Apply(RoundKind.TooClose, [Propose(0), Propose(2)])
            .AddMoves([new MarkerMove(new TargetId(3), Vector3.Zero, Vector3.One)]);
        var writing = new RecordingWriting();

        PatchWriter.Write(final, [.. targets], OtherModsObjectsResult.SwitchedOff, new ReadPlugin(), writing);

        Assert.Equal<Type>(
            [typeof(SetInitiallyDisabled), typeof(SetPosition), typeof(SetPlacement), typeof(SetPlacement), typeof(SetPosition)],
            writing.Orders.Select(order => order.GetType()));
        Assert.Equal([targets[0].Key, targets[0].Key, targets[1].Key, targets[2].Key, targets[3].Key], writing.Orders.Select(order => order.Record));
    }

    private static EarlierVersion EarlierAt(EulerPlacement placement, float? storedScale) =>
        TestTargets.EarlierVersionAt(new PluginName("Earlier.esp"), placement) with { StoredScale = storedScale };

    private static (WriteSummary Written, RecordingWriting Writing) WriteOne(TargetObject target, WhenRemoved whenRemoved)
    {
        var protection = ObjectsToKeep.Build([target], [], TestTargets.References(1), TestBaseFacts.Unresolved);
        var final = RemovalDecisions.Start(protection, [whenRemoved]).Apply(RoundKind.TooClose, [Propose(0)]);
        var writing = new RecordingWriting();

        var written = PatchWriter.Write(final, [target], OtherModsObjectsResult.SwitchedOff, new ReadPlugin(), writing);

        return (written, writing);
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
