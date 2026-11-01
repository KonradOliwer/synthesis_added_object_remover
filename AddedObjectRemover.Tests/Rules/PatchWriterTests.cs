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

        var written = PatchWriter.Write(final, [.. targets], OtherModsObjectsResult.SwitchedOff, RestoreWhereRemovedResult.SwitchedOff, new ReadPlugin(), writing);

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

        PatchWriter.Write(final, [.. targets], OtherModsObjectsResult.SwitchedOff, RestoreWhereRemovedResult.SwitchedOff, plugin, writing);

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

        PatchWriter.Write(final, [.. targets], OtherModsObjectsResult.SwitchedOff, RestoreWhereRemovedResult.SwitchedOff, new ReadPlugin(), writing);

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

        var written = PatchWriter.Write(final, [.. targets], OtherModsObjectsResult.SwitchedOff, RestoreWhereRemovedResult.SwitchedOff, new ReadPlugin(), writing);

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

        var written = PatchWriter.Write(final, [.. targets], OtherModsObjectsResult.SwitchedOff, RestoreWhereRemovedResult.SwitchedOff, new ReadPlugin(), writing);

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

        var written = PatchWriter.Write(final, [.. targets], settled, RestoreWhereRemovedResult.SwitchedOff, new ReadPlugin(), writing);

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

    [Theory]
    [InlineData(PlacedParts.Position | PlacedParts.Rotation | PlacedParts.Scale)]
    [InlineData(PlacedParts.BaseAndData)]
    [InlineData(PlacedParts.Rotation | PlacedParts.Presence)]
    public void ARestoredObjectIsWrittenAsTheUndoOfExactlyThePartsTheCleanedModLastChangedAndIsNotDisabled(PlacedParts undo)
    {
        var target = TestTargets.Create(0, TestTargets.At(new Vector3(500, 0, 0)));
        var earlier = EarlierAt(new EulerPlacement(new Vector3(1, 2, 3), new Vector3(0, 0, 0.5f), 1f)) with { Undo = undo };

        var (written, writing) = WriteOne(target, new WhenRemoved.Restore(earlier));

        Assert.Equal<WriteOrder>([new UndoPlacedParts(target.Key, undo)], writing.Orders);
        Assert.Equal([0], written.Restored.Select(id => id.Index));
        Assert.Empty(written.Removed);
    }

    [Fact]
    public void ARestoredExteriorObjectGoesBackIntoItsEarlierCellBeforeAnythingElseIsWrittenForIt()
    {
        var earlierCell = new CellFact(TestTargets.SpaceKey(0x201), null);
        var target = TestTargets.Create(0, TestTargets.At(Vector3.Zero)) with { Cell = new CellFact(TestTargets.SpaceKey(0x200), null) };
        var earlier = EarlierAt(target.Placement) with { Cell = earlierCell };

        var (_, writing) = WriteOne(target, new WhenRemoved.Restore(earlier));

        Assert.Equal<WriteOrder>(
            [new PlaceInCell(target.Key, earlierCell.Key, InPersistentList: false), new UndoPlacedParts(target.Key, earlier.Undo)], writing.Orders);
    }

    [Fact]
    public void ARestoredObjectStaysInItsCellWhenItIsPersistentOrTheEarlierVersionIsOrItStandsInTheSameInteriorOrItsPositionStays()
    {
        var current = new CellFact(TestTargets.SpaceKey(0x200), null);
        var other = new CellFact(TestTargets.SpaceKey(0x201), null);
        var exterior = TestTargets.Create(0, TestTargets.At(Vector3.Zero)) with { Cell = current };
        var earlier = EarlierAt(exterior.Placement) with { Cell = other };

        var persistentNow = WriteOne(exterior with { IsPersistent = true }, new WhenRemoved.Restore(earlier)).Writing.Orders;
        var persistentBefore = WriteOne(exterior, new WhenRemoved.Restore(earlier with { IsPersistent = true })).Writing.Orders;
        var sameInterior = WriteOne(exterior with { Cell = null }, new WhenRemoved.Restore(earlier with { Cell = new CellFact(exterior.SpaceKey, null) })).Writing.Orders;
        var positionStays = WriteOne(exterior, new WhenRemoved.Restore(earlier with { Undo = PlacedParts.Rotation })).Writing.Orders;

        Assert.All([persistentNow, persistentBefore, sameInterior, positionStays], orders => Assert.Empty(orders.OfType<PlaceInCell>()));
    }

    /// <summary>A record whose presence goes back, such as one the cleaned mod deleted, stands again in the list of its earlier version.</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ARestoreWhosePresenceAndPersistenceGoBackPutsTheObjectIntoTheEarlierVersionsCellAndList(bool persistentNow, bool persistentBefore)
    {
        var earlierCell = new CellFact(TestTargets.SpaceKey(0x201), null);
        var target = TestTargets.Create(0, TestTargets.At(Vector3.Zero)) with { Cell = new CellFact(TestTargets.SpaceKey(0x200), null), IsPersistent = persistentNow };
        var earlier = EarlierAt(target.Placement) with { Cell = earlierCell, IsPersistent = persistentBefore, Undo = PlacedParts.Presence, PersistenceGoesBack = true };

        var (_, writing) = WriteOne(target, new WhenRemoved.Restore(earlier));

        Assert.Equal<WriteOrder>(
            [new PlaceInCell(target.Key, earlierCell.Key, persistentBefore), new UndoPlacedParts(target.Key, PlacedParts.Presence)], writing.Orders);
    }

    /// <summary>A later plugin or an earlier patcher made the object persistent: its Persistent flag and list stay while its presence goes back.</summary>
    [Fact]
    public void ARestoreWhosePresenceGoesBackKeepsThePersistenceALaterPluginSet()
    {
        var target = TestTargets.Create(0, TestTargets.At(Vector3.Zero)) with { Cell = new CellFact(TestTargets.SpaceKey(0x200), null), IsPersistent = true };
        var earlier = EarlierAt(target.Placement) with { Cell = target.Cell!, Undo = PlacedParts.Position | PlacedParts.Presence };

        var (_, writing) = WriteOne(target, new WhenRemoved.Restore(earlier));

        Assert.Equal<WriteOrder>([new UndoPlacedParts(target.Key, PlacedParts.Position | PlacedParts.Presence)], writing.Orders);
    }

    /// <summary>A later plugin moved the object, perhaps into another cell: it stays in its cell and list while its presence and persistence go back.</summary>
    [Fact]
    public void ARestoreWhosePositionALaterPluginMovedStaysInItsCellAndListWhileItsPresenceGoesBack()
    {
        var target = TestTargets.Create(0, TestTargets.At(Vector3.Zero)) with { Cell = new CellFact(TestTargets.SpaceKey(0x200), null) };
        var earlier = EarlierAt(target.Placement) with
        {
            Cell = new CellFact(TestTargets.SpaceKey(0x201), null),
            Undo = PlacedParts.Presence,
            PersistenceGoesBack = true,
            LeftToLaterPlugins = ImmutableSortedDictionary<PlacedParts, PluginName>.Empty.Add(PlacedParts.Position, new PluginName("Later.esp")),
        };

        var (_, writing) = WriteOne(target, new WhenRemoved.Restore(earlier));

        Assert.Equal<WriteOrder>([new UndoPlacedParts(target.Key, PlacedParts.Presence)], writing.Orders);
    }

    [Fact]
    public void RestoresAreWrittenAfterTheFitsAndBeforeTheMarkerMoves()
    {
        var targets = TestTargets.CreateMany(4);
        var protection = ObjectsToKeep.Build(targets, [], TestTargets.References(4), TestBaseFacts.Unresolved);
        var restoreTo = new WhenRemoved.Restore(EarlierAt(targets[2].Placement));
        var final = RemovalDecisions.Start(protection, [new WhenRemoved.Disable(), new WhenRemoved.Disable(), restoreTo, new WhenRemoved.Disable()])
            .ApplyFits([new ProposedFit(new TargetId(1), Propose(1).Reason, targets[1].Placement with { Scale = 0.9f })])
            .Apply(RoundKind.TooClose, [Propose(0), Propose(2)])
            .AddMoves([new MarkerMove(new TargetId(3), Vector3.Zero, Vector3.One)]);
        var writing = new RecordingWriting();

        PatchWriter.Write(final, [.. targets], OtherModsObjectsResult.SwitchedOff, RestoreWhereRemovedResult.SwitchedOff, new ReadPlugin(), writing);

        Assert.Equal<Type>(
            [typeof(SetInitiallyDisabled), typeof(SetPosition), typeof(SetPlacement), typeof(UndoPlacedParts), typeof(SetPosition)],
            writing.Orders.Select(order => order.GetType()));
        Assert.Equal([targets[0].Key, targets[0].Key, targets[1].Key, targets[2].Key, targets[3].Key], writing.Orders.Select(order => order.Record));
    }

    /// <summary>
    /// A rock goes back into its earlier cell and a cell's music goes back, after the target restores; the kept rock and the cell's
    /// part left to a later plugin are not written. The rock's two orders count as one record written.
    /// </summary>
    [Fact]
    public void WhatGoesBackWhereTheCleanedModWasRemovedIsWrittenAfterTheTargetRestoresPlacedRecordsBeforeCells()
    {
        var target = TestTargets.Create(0, TestTargets.At(new Vector3(500, 0, 0)));
        var protection = ObjectsToKeep.Build([target], [], TestTargets.References(1), TestBaseFacts.Unresolved);
        var final = RemovalDecisions.Start(protection, [new WhenRemoved.Restore(EarlierAt(target.Placement))]).Apply(RoundKind.TooClose, [Propose(0)]);
        var earlierCell = new CellFact(TestTargets.SpaceKey(0x201), null);
        var rock = ChangedRock(0x900) with { Earlier = EarlierAt(new EulerPlacement(Vector3.Zero, Vector3.Zero, 1f)) with { Cell = earlierCell } };
        var cell = ChangedCell(TestTargets.SpaceKey(0x202));
        var cleared = new AreaShare(Objects: 1, Standing: 0, Size: 1, StandingSize: 0, AreaVerdict.Cleared);
        var standing = cleared with { Standing = 1, StandingSize = 1, Verdict = AreaVerdict.StillStanding };
        var whereRemoved = new RestoreWhereRemovedResult(
            StepOutcome.Performed,
            [new PlacedChangeJudged(rock, null, cleared, null, Refused: null, IsRestored: true), new PlacedChangeJudged(ChangedRock(0x901), null, standing, null, Refused: null, IsRestored: false)],
            [new CellChangeJudged(cell, [cell.Parts[0]], null, cleared, WaterKeptBy: null, IsRestored: true)],
            [],
            [],
            [],
            Passes: 1,
            DoorsLeftRemoved: [],
            Failures: []);
        var writing = new RecordingWriting();

        var written = PatchWriter.Write(final, [target], OtherModsObjectsResult.SwitchedOff, whereRemoved, new ReadPlugin(), writing);

        Assert.Equal((1, 2), (written.Restored.Length, written.WrittenWhereRemoved));
        Assert.Equal<WriteOrder>(
            [
                new UndoPlacedParts(target.Key, PlacedParts.Position | PlacedParts.Rotation | PlacedParts.Scale),
                new PlaceInCell(rock.Key, earlierCell.Key, InPersistentList: false),
                new UndoPlacedParts(rock.Key, rock.Earlier.Undo),
            ],
            writing.Orders.Take(3));
        var cellOrder = Assert.IsType<UndoCellParts>(Assert.Single(writing.Orders.Skip(3)));
        Assert.Equal(cell.Cell, cellOrder.Record);
        Assert.Equal([new CellPartName(CellPart.Music, null)], cellOrder.Parts.ToList());
    }

    private static RecordChangedByCleanedMod ChangedRock(uint id) =>
        new(
            new RecordKey(new PluginName("Base.esm"), id),
            EditorId: null,
            TestTargets.Space,
            new CellFact(TestTargets.SpaceKey(0x200), null),
            TestTargets.TargetName,
            WhereItIsNow: null,
            BaseNow: null,
            TestPlacedRecords.PlainReference,
            IsPersistent: false,
            EarlierAt(new EulerPlacement(Vector3.Zero, Vector3.Zero, 1f)),
            Target: null,
            LinkedFromCleanedMod: false,
            PresenceBefore: default);

    private static CellChangedByCleanedMod ChangedCell(RecordKey cell)
    {
        var water = new CellWaterCopy(new PluginName("Base.esm"), HasWater: false, OwnLevel: null);
        return new CellChangedByCleanedMod(
            cell,
            TestTargets.Space,
            (0, 0),
            [
                new PartChange(new CellPartName(CellPart.Music, null), new PartOutcome.Undo()),
                new PartChange(new CellPartName(CellPart.Location, null), new PartOutcome.LeftToLaterPlugin(new PluginName("Later.esp"))),
            ],
            LinkedFromCleanedMod: false,
            TestTargets.TargetName,
            water,
            water,
            DefaultWaterLevel: null);
    }

    private static EarlierVersion EarlierAt(EulerPlacement placement) => TestTargets.EarlierVersionAt(new PluginName("Earlier.esp"), placement);

    private static (WriteSummary Written, RecordingWriting Writing) WriteOne(TargetObject target, WhenRemoved whenRemoved)
    {
        var protection = ObjectsToKeep.Build([target], [], TestTargets.References(1), TestBaseFacts.Unresolved);
        var final = RemovalDecisions.Start(protection, [whenRemoved]).Apply(RoundKind.TooClose, [Propose(0)]);
        var writing = new RecordingWriting();

        var written = PatchWriter.Write(final, [target], OtherModsObjectsResult.SwitchedOff, RestoreWhereRemovedResult.SwitchedOff, new ReadPlugin(), writing);

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

        public INavmeshWorldView ReadFinalNavmeshes(IWritableRecords records) => throw new NotSupportedException();

        public NavmeshFault? FindNavmeshFaultIfWritten(IWritableRecords records, IReadOnlyList<WriteOrder> orders) => throw new NotSupportedException();
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

        public NonPlacedLinks ReadLinks(PluginName target, IReadOnlySet<RecordKey> targets, IReadOnlySet<PluginName> linkedPlugins) => throw new NotSupportedException();

        public CellGridPoint? ReadHomeCellGrid(RecordKey record) => throw new NotSupportedException();

        public float[]? ReadTerrain(ExteriorCell cell) => throw new NotSupportedException();

        public float[]? ReadTerrainBelowTarget(ExteriorCell cell) => throw new NotSupportedException();

        public TerrainVersion[] ReadTerrainVersions(ExteriorCell cell) => throw new NotSupportedException();

        public WaterCopies ReadWaterCopies(ExteriorCell cell) => throw new NotSupportedException();
        public RecordKey[] ReadTextureMaterialChain(RecordKey texture) => throw new NotSupportedException();

        public RecordKey? FindLandWorldspace(RecordKey space) => throw new NotSupportedException();

        public NavmeshWorldTriangle[]? ReadNavmeshes(NavmeshBucket bucket) => throw new NotSupportedException();

        public NpcTraits? NpcTraitsOf(BaseKey npc) => throw new NotSupportedException();

        public TraitSupplier? TraitSupplierOf(BaseKey spawn, NpcTemplateFlag templateFlag) => throw new NotSupportedException();

        public IReadOnlyList<RecordKey> LeveledEntriesOf(RecordKey list, NpcTemplateFlag templateFlag) => throw new NotSupportedException();
    }
}
