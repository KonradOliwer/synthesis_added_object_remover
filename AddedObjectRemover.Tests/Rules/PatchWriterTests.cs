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
            .Apply(AlsoRemoveRounds.Opening, [Propose(1)]);
        var writing = new RecordingWriting();

        var written = PatchWriter.Write(new(final, [.. targets], OtherModsObjectsResult.SwitchedOff, RestoreWhereRemovedResult.SwitchedOff(),new ReadPlugin(), writing, TestTargets.TargetName));

        Assert.Equal([0, 4, 1, 2], written.Removed.Select(id => id.Index));
        Assert.Equal(
            written.Removed.Select(id => targets[id.Index].Key),
            writing.Orders.OfType<SetInitiallyDisabled>().Select(order => order.Record));
        Assert.Equal(0, written.Overrides.GetValueOrDefault(WrittenPart.KeptMarkerMoved));
    }

    [Fact]
    public void TheWritingCopiesFromTheRecordsThePluginRead()
    {
        var targets = TestTargets.CreateMany(1);
        var protection = ObjectsToKeep.Build(targets, [], TestTargets.References(1), TestBaseFacts.Unresolved);
        var final = TestSeededDecisions.StartDisablingAll(protection, 1).Apply(RoundKind.TooClose, [Propose(0)]);
        var plugin = new ReadPlugin();
        var writing = new RecordingWriting();

        PatchWriter.Write(new(final, [.. targets], OtherModsObjectsResult.SwitchedOff, RestoreWhereRemovedResult.SwitchedOff(),plugin, writing, TestTargets.TargetName));

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

        PatchWriter.Write(new(final, [.. targets], OtherModsObjectsResult.SwitchedOff, RestoreWhereRemovedResult.SwitchedOff(),new ReadPlugin(), writing, TestTargets.TargetName));

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

        var written = PatchWriter.Write(new(final, [.. targets], OtherModsObjectsResult.SwitchedOff, RestoreWhereRemovedResult.SwitchedOff(),new ReadPlugin(), writing, TestTargets.TargetName));

        Assert.Equal<WriteOrder>([new SetPosition(targets[3].Key, new Vector3(1, 2, 3))], writing.Orders);
        Assert.Equal(1, written.Overrides.GetValueOrDefault(WrittenPart.KeptMarkerMoved));
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

        var written = PatchWriter.Write(new(final, [.. targets], OtherModsObjectsResult.SwitchedOff, RestoreWhereRemovedResult.SwitchedOff(),new ReadPlugin(), writing, TestTargets.TargetName));

        Assert.Equal<WriteOrder>([new SetPlacement(targets[0].Key, to.Position, to.Rotation, to.Scale)], writing.Orders);
        Assert.Equal((0, 1), (written.Removed.Length, written.Overrides.GetValueOrDefault(WrittenPart.Fitted)));
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

        var written = PatchWriter.Write(new(final, [.. targets], settled, RestoreWhereRemovedResult.SwitchedOff(),new ReadPlugin(), writing, TestTargets.TargetName));

        Assert.Equal<WriteOrder>(
            [
                new SetInitiallyDisabled(targets[0].Key),
                new SetPosition(targets[0].Key, targets[0].Transform.Position with { Z = -30000f }),
                new SetInitiallyDisabled(removed.Key),
                new SetPosition(removed.Key, new Vector3(1, 2, -30000f)),
                new SetPosition(pulled.Key, new Vector3(4, 5, 10)),
            ],
            writing.Orders);
        Assert.Equal((1, 1), (written.Overrides.GetValueOrDefault(WrittenPart.OtherModsObjectRemoved), written.Overrides.GetValueOrDefault(WrittenPart.OtherModsObjectPulledDown)));
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

        Assert.Equal<WriteOrder>([new CopyEarlierPlacedParts(target.Key, undo)], writing.Orders);
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
            [new PlaceInCell(target.Key, earlierCell.Key, InPersistentList: false), new CopyEarlierPlacedParts(target.Key, earlier.Undo)], writing.Orders);
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
            [new PlaceInCell(target.Key, earlierCell.Key, persistentBefore), new CopyEarlierPlacedParts(target.Key, PlacedParts.Presence)], writing.Orders);
    }

    /// <summary>A later plugin or an earlier patcher made the object persistent: its Persistent flag and list stay while its presence goes back.</summary>
    [Fact]
    public void ARestoreWhosePresenceGoesBackKeepsThePersistenceALaterPluginSet()
    {
        var target = TestTargets.Create(0, TestTargets.At(Vector3.Zero)) with { Cell = new CellFact(TestTargets.SpaceKey(0x200), null), IsPersistent = true };
        var earlier = EarlierAt(target.Placement) with { Cell = target.Cell!, Undo = PlacedParts.Position | PlacedParts.Presence };

        var (_, writing) = WriteOne(target, new WhenRemoved.Restore(earlier));

        Assert.Equal<WriteOrder>([new CopyEarlierPlacedParts(target.Key, PlacedParts.Position | PlacedParts.Presence)], writing.Orders);
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

        Assert.Equal<WriteOrder>([new CopyEarlierPlacedParts(target.Key, PlacedParts.Presence)], writing.Orders);
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

        PatchWriter.Write(new(final, [.. targets], OtherModsObjectsResult.SwitchedOff, RestoreWhereRemovedResult.SwitchedOff(),new ReadPlugin(), writing, TestTargets.TargetName));

        Assert.Equal<Type>(
            [typeof(SetInitiallyDisabled), typeof(SetPosition), typeof(SetPlacement), typeof(CopyEarlierPlacedParts), typeof(SetPosition)],
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
        var cleared = TestAreaShares.Of(AreaVerdict.Cleared);
        var standing = TestAreaShares.Of(AreaVerdict.StillStanding);
        var stays = ChangedRock(0x901);
        var whereRemoved = new RestoreWhereRemovedResult(
            StepOutcome.Performed,
            [
                new PlacedChangeJudged(rock, null, cleared, null, Refused: null, IsRestored: true, ChangeJudge.KindOf(rock), KeptForArea: null, PlacedBack: null),
                new PlacedChangeJudged(stays, null, standing, null, Refused: null, IsRestored: false, ChangeJudge.KindOf(stays), AreaVerdict.StillStanding, PlacedBack: null),
            ],
            [new CellChangeJudged(cell, [cell.Parts[0]], null, cleared, WaterKeptBy: null, IsRestored: true, KeptForArea: null)],
            [],
            [],
            [],
            Passes: 1,
            DoorsLeftRemoved: [],
            Failures: [],
            RestoreWhereRemovedTimes.None);
        var writing = new RecordingWriting();

        var written = PatchWriter.Write(new(final, [target], OtherModsObjectsResult.SwitchedOff, whereRemoved, new ReadPlugin(), writing, TestTargets.TargetName));

        Assert.Equal((1, 2), (written.Restored.Length, written.Overrides.GetValueOrDefault(WrittenPart.WhereRemoved)));
        Assert.Equal<WriteOrder>(
            [
                new CopyEarlierPlacedParts(target.Key, PlacedParts.Position | PlacedParts.Rotation | PlacedParts.Scale),
                new PlaceInCell(rock.Key, earlierCell.Key, InPersistentList: false),
                new CopyEarlierPlacedParts(rock.Key, rock.Earlier.Undo),
            ],
            writing.Orders.Take(3));
        var cellOrder = Assert.IsType<CopyEarlierCellParts>(Assert.Single(writing.Orders.Skip(3)));
        Assert.Equal(cell.Cell, cellOrder.Record);
        Assert.Equal([new CellPartName(CellPart.Music, null)], cellOrder.Parts.ToList());
    }

    /// <summary>
    /// Three rocks went back with a copy planned: the first goes back with it; a guard refused the second and the third lost its copy,
    /// so neither goes back nor gets a copy. The copy is written before every other order, so it copies the record before the restore
    /// changes it, with its marker, and the summary names it with the key the writing gave it.
    /// </summary>
    [Fact]
    public void ACopyIsWrittenOnlyForARecordThatGoesBackWithItBeforeEveryOtherOrder()
    {
        var target = TestTargets.Create(0, TestTargets.At(new Vector3(500, 0, 0)));
        var protection = ObjectsToKeep.Build([target], [], TestTargets.References(1), TestBaseFacts.Unresolved);
        var final = RemovalDecisions.Start(protection, [new WhenRemoved.Disable()]);
        var (goesBack, refused, lost) = (ChangedRock(0x900), ChangedRock(0x901), ChangedRock(0x902));
        var planned = new CloneJudged(RefusedBy: null);
        var cleared = TestAreaShares.Of(AreaVerdict.Cleared);
        var cell = ChangedCell(TestTargets.SpaceKey(0x202));
        var whereRemoved = new RestoreWhereRemovedResult(
            StepOutcome.Performed,
            [
                new PlacedChangeJudged(goesBack, null, cleared, cleared, Refused: null, IsRestored: true, ChangeJudge.KindOf(goesBack), KeptForArea: null, PlacedBack: null, planned),
                new PlacedChangeJudged(refused, null, cleared, cleared, new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, goesBack.Key, Failure: null), IsRestored: false, ChangeJudge.KindOf(refused), KeptForArea: null, PlacedBack: null, planned),
                new PlacedChangeJudged(lost, null, cleared, cleared, Refused: null, IsRestored: false, ChangeJudge.KindOf(lost), AreaVerdict.StillStanding, PlacedBack: null, planned with { RefusedBy = CloneRefusal.ActorsWalkOnIt }),
            ],
            [new CellChangeJudged(cell, [cell.Parts[0]], null, cleared, WaterKeptBy: null, IsRestored: true, KeptForArea: null)],
            [],
            [],
            [],
            Passes: 1,
            DoorsLeftRemoved: [],
            Failures: [],
            RestoreWhereRemovedTimes.None);
        var writing = new RecordingWriting();

        var written = PatchWriter.Write(new(final, [target], OtherModsObjectsResult.SwitchedOff, whereRemoved, new ReadPlugin(), writing, TestTargets.TargetName));

        Assert.Equal<Type>([typeof(AddClone), typeof(PlaceInCell), typeof(CopyEarlierPlacedParts), typeof(CopyEarlierCellParts)], writing.Orders.Select(order => order.GetType()));
        Assert.Equal(new AddClone(goesBack.Key, CloneMarker.Of(goesBack.Key, TestTargets.TargetName)), writing.Orders[0]);
        Assert.Equal([new CloneWritten(goesBack.Key, new RecordKey(TestTargets.PatchName, FirstAddedId))], written.Clones.ToArray());
        Assert.Equal((2, 2), (written.Overrides.GetValueOrDefault(WrittenPart.WhereRemoved), written.Overrides.Values.Sum()));
    }

    /// <summary>
    /// The parts given back are one per part, each with the plugin whose value it took: the parts of a target a removal step puts
    /// back and of a rock that goes back, by their undone flags; a cell's undone part, from the version below the cleaned mod; a
    /// LAND record's heights (only when some height goes back) and each texture quarter. A rock that stays gives none.
    /// </summary>
    [Fact]
    public void EachRecordThatGoesBackGivesOnePartPerPartWithThePluginItsValueCameFrom()
    {
        var target = TestTargets.Create(0, TestTargets.At(new Vector3(500, 0, 0)));
        var protection = ObjectsToKeep.Build([target], [], TestTargets.References(1), TestBaseFacts.Unresolved);
        var targetBefore = EarlierAt(target.Placement) with { Plugin = new PluginName("Between.esp"), Undo = PlacedParts.Position };
        var final = RemovalDecisions.Start(protection, [new WhenRemoved.Restore(targetBefore)]).Apply(RoundKind.TooClose, [Propose(0)]);
        var rock = ChangedRock(0x900) with { Earlier = EarlierAt(new EulerPlacement(Vector3.Zero, Vector3.Zero, 1f)) with { Undo = PlacedParts.Rotation | PlacedParts.Presence } };
        var stays = ChangedRock(0x901);
        var cell = ChangedCell(TestTargets.SpaceKey(0x202));
        var raised = ChangedLand(0x300);
        var painted = ChangedLand(0x301);
        var cleared = TestAreaShares.Of(AreaVerdict.Cleared);
        var whereRemoved = new RestoreWhereRemovedResult(
            StepOutcome.Performed,
            [
                new PlacedChangeJudged(rock, null, cleared, null, Refused: null, IsRestored: true, ChangeJudge.KindOf(rock), KeptForArea: null, PlacedBack: null),
                new PlacedChangeJudged(stays, null, cleared, null, Refused: null, IsRestored: false, ChangeJudge.KindOf(stays), AreaVerdict.StillStanding, PlacedBack: null),
            ],
            [new CellChangeJudged(cell, [cell.Parts[0]], null, cleared, WaterKeptBy: null, IsRestored: true, KeptForArea: null)],
            [JudgedLand(raised, PointSet.Of([0, 1]), QuarterSet.Of([3, 0])), JudgedLand(painted, PointSet.Of([]), QuarterSet.Of([2]))],
            [],
            [],
            Passes: 1,
            DoorsLeftRemoved: [],
            Failures: [],
            RestoreWhereRemovedTimes.None);

        var written = PatchWriter.Write(new(final, [target], OtherModsObjectsResult.SwitchedOff, whereRemoved, new ReadPlugin(), new RecordingWriting(), TestTargets.TargetName));

        var cleanedMod = TestTargets.TargetName;
        var below = new PluginName("Below.esp");
        Assert.Equal<RestoredPart>(
            new RestoredPart[]
            {
                new(cleanedMod, target.Key, new RestoredPartName.Placed(PlacedParts.Position), new PluginName("Between.esp")),
                new(cleanedMod, rock.Key, new RestoredPartName.Placed(PlacedParts.Rotation), new PluginName("Earlier.esp")),
                new(cleanedMod, rock.Key, new RestoredPartName.Placed(PlacedParts.Presence), new PluginName("Earlier.esp")),
                new(cleanedMod, cell.Cell, new RestoredPartName.Cell(new CellPartName(CellPart.Music, null)), new PluginName("Base.esm")),
                new(cleanedMod, raised.LandKey, new RestoredPartName.LandHeights(PointSet.Of([0, 1])), below),
                new(cleanedMod, raised.LandKey, new RestoredPartName.LandTextureQuarter(0), below),
                new(cleanedMod, raised.LandKey, new RestoredPartName.LandTextureQuarter(3), below),
                new(cleanedMod, painted.LandKey, new RestoredPartName.LandTextureQuarter(2), below),
            }.OrderBy(part => part.Record, RecordKeyOrder.Comparer),
            written.RestoredParts);
    }

    /// <summary>A door whose navmesh link goes back with its position gives both parts: the scan compares the link as a part of the door.</summary>
    [Fact]
    public void ADoorWhoseLinkGoesBackWithItsPositionGivesBothParts()
    {
        var target = TestTargets.Create(0, TestTargets.At(new Vector3(500, 0, 0)));
        var protection = ObjectsToKeep.Build([target], [], TestTargets.References(1), TestBaseFacts.Unresolved);
        var final = RemovalDecisions.Start(protection, [new WhenRemoved.Disable()]);
        var door = ChangedRock(0x900) with { Earlier = ChangedRock(0x900).Earlier with { Undo = PlacedParts.Position | PlacedParts.NavDoorLink } };
        var whereRemoved = RestoreWhereRemovedResult.SwitchedOff() with
        {
            Outcome = StepOutcome.Performed,
            Objects = [new PlacedChangeJudged(door, null, TestAreaShares.Of(AreaVerdict.Cleared), null, Refused: null, IsRestored: true, ChangeJudge.KindOf(door), KeptForArea: null, PlacedBack: null)],
        };

        var written = PatchWriter.Write(new(final, [target], OtherModsObjectsResult.SwitchedOff, whereRemoved, new ReadPlugin(), new RecordingWriting(), TestTargets.TargetName));

        Assert.Equal<RestoredPart>(
            [
                new(TestTargets.TargetName, door.Key, new RestoredPartName.Placed(PlacedParts.Position), new PluginName("Earlier.esp")),
                new(TestTargets.TargetName, door.Key, new RestoredPartName.Placed(PlacedParts.NavDoorLink), new PluginName("Earlier.esp")),
            ],
            written.RestoredParts);
    }

    /// <summary>
    /// The restore's follow-up puts one target back, removes another and pulls down another mod's object: only the target put back
    /// gives parts, from the version it goes back to.
    /// </summary>
    [Fact]
    public void OnlyATargetTheFollowUpOfTheRestorePutsBackGivesParts()
    {
        var targets = TestTargets.CreateMany(2);
        var final = TestSeededDecisions.StartDisablingAll(ObjectsToKeep.Build(targets, [], TestTargets.References(2), TestBaseFacts.Unresolved), 2);
        var pulled = TestShapes.Placed(ModKey.FromNameAndExtension("Other.esp"), 0, default, new Vector3(4, 5, 60));
        var rock = ChangedRock(0x900);
        var earlier = EarlierAt(new EulerPlacement(new Vector3(9, 9, 9), Vector3.Zero, 1f)) with { Undo = PlacedParts.Position | PlacedParts.Rotation };
        var whereRemoved = RestoreWhereRemovedResult.SwitchedOff() with
        {
            FollowUp = new RestoreFollowUp(
                [new FollowUpTarget(new TargetId(0), RestoredTo: null, [rock.Key]), new FollowUpTarget(new TargetId(1), earlier, [rock.Key])],
                [new FollowUpOtherObject(new OtherModsObjectOutcome(pulled with { Position = new Vector3(4, 5, 10) }, OtherModsObjectFate.PulledDown, LostShare: 1f, Drop: 50f, ReferenceRoles.None), [rock.Key])]),
        };

        var written = PatchWriter.Write(new(final, [.. targets], OtherModsObjectsResult.SwitchedOff, whereRemoved, new ReadPlugin(), new RecordingWriting(), TestTargets.TargetName));

        Assert.Equal<RestoredPart>(
            [
                new(TestTargets.TargetName, targets[1].Key, new RestoredPartName.Placed(PlacedParts.Position), new PluginName("Earlier.esp")),
                new(TestTargets.TargetName, targets[1].Key, new RestoredPartName.Placed(PlacedParts.Rotation), new PluginName("Earlier.esp")),
            ],
            written.RestoredParts);
    }

    /// <summary>
    /// A cell whose water level the restore keeps gives only the parts that go back, and a LAND record gives only the heights of the
    /// points that go back, not of those it keeps.
    /// </summary>
    [Fact]
    public void AKeptWaterLevelAndKeptGroundGiveNoPart()
    {
        var targets = TestTargets.CreateMany(1);
        var final = TestSeededDecisions.StartDisablingAll(ObjectsToKeep.Build(targets, [], TestTargets.References(1), TestBaseFacts.Unresolved), 1);
        var changed = ChangedCell(TestTargets.SpaceKey(0x202));
        var cell = changed with { Parts = [changed.Parts[0], new PartChange(new CellPartName(CellPart.WaterLevel, null), new PartOutcome.Undo())] };
        var land = ChangedLand(0x300);
        var cleared = TestAreaShares.Of(AreaVerdict.Cleared);
        var keeper = new RecordKey(new PluginName("Base.esm"), 0x901);
        var whereRemoved = RestoreWhereRemovedResult.SwitchedOff() with
        {
            Outcome = StepOutcome.Performed,
            Cells = [new CellChangeJudged(cell, [cell.Parts[0]], null, cleared, WaterKeptBy: keeper, IsRestored: true, KeptForArea: null, WaterKeptReason.ForChangedTerrainCheck)],
            Lands = [JudgedLand(land, PointSet.Of([0]), QuarterSet.Of([])) with { Kept = [new LandPointsKept(TerrainKeptReason.ForChangedTerrainCheck, PointSet.Of([1, 2]))] }],
        };

        var written = PatchWriter.Write(new(final, [.. targets], OtherModsObjectsResult.SwitchedOff, whereRemoved, new ReadPlugin(), new RecordingWriting(), TestTargets.TargetName));

        Assert.Equal<RestoredPart>(
            new RestoredPart[]
            {
                new(TestTargets.TargetName, cell.Cell, new RestoredPartName.Cell(new CellPartName(CellPart.Music, null)), new PluginName("Base.esm")),
                new(TestTargets.TargetName, land.LandKey, new RestoredPartName.LandHeights(PointSet.Of([0])), new PluginName("Below.esp")),
            }.OrderBy(part => part.Record, RecordKeyOrder.Comparer),
            written.RestoredParts);
    }

    /// <summary>
    /// The follow-up of the restore removes one target object and restores another, removes one other mod's object, pulls down one the
    /// run already pulled down, from where the run left it, and leaves one as it is. Its orders come after the run's own, so the
    /// restore's pull-down is written after the run's and wins.
    /// </summary>
    [Fact]
    public void TheFollowUpOfTheRestoreIsWrittenAfterTheRunsOwnOrders()
    {
        var targets = TestTargets.CreateMany(2);
        var protection = ObjectsToKeep.Build(targets, [], TestTargets.References(2), TestBaseFacts.Unresolved);
        var final = TestSeededDecisions.StartDisablingAll(protection, 2);
        var other = ModKey.FromNameAndExtension("Other.esp");
        var pulledByRun = TestShapes.Placed(other, 1, default, new Vector3(4, 5, 60));
        var removed = TestShapes.Placed(other, 0, default, new Vector3(1, 2, 3));
        var left = TestShapes.Placed(other, 2, default, new Vector3(7, 8, 9));
        var settled = new OtherModsObjectsResult(
            StepOutcome.Performed,
            [new OtherModsObjectOutcome(pulledByRun, OtherModsObjectFate.PulledDown, LostShare: 1f, Drop: 50f, ReferenceRoles.None)],
            Rounds: [],
            ProposedRounds: [],
            Failure: null);
        var rock = ChangedRock(0x900);
        var earlier = EarlierAt(new EulerPlacement(new Vector3(9, 9, 9), Vector3.Zero, 1f));
        var whereRemoved = RestoreWhereRemovedResult.SwitchedOff() with
        {
            FollowUp = new RestoreFollowUp(
                [new FollowUpTarget(new TargetId(0), RestoredTo: null, [rock.Key]), new FollowUpTarget(new TargetId(1), earlier, [rock.Key])],
                [
                    new FollowUpOtherObject(new OtherModsObjectOutcome(removed, OtherModsObjectFate.Removed, LostShare: 1f, Drop: 0f, ReferenceRoles.None), [rock.Key]),
                    new FollowUpOtherObject(
                        new OtherModsObjectOutcome(pulledByRun with { Position = new Vector3(4, 5, 10) }, OtherModsObjectFate.PulledDown, LostShare: 1f, Drop: 5f, ReferenceRoles.None),
                        [rock.Key]),
                    new FollowUpOtherObject(new OtherModsObjectOutcome(left, OtherModsObjectFate.LeftWonAfterPatch, LostShare: 1f, Drop: 0f, ReferenceRoles.None), [rock.Key]),
                ]),
        };
        var writing = new RecordingWriting();

        var written = PatchWriter.Write(new(final, [.. targets], settled, whereRemoved, new ReadPlugin(), writing, TestTargets.TargetName));

        Assert.Equal<WriteOrder>(
            [
                new SetPosition(pulledByRun.Key, new Vector3(4, 5, 10)),
                new SetInitiallyDisabled(targets[0].Key),
                new SetPosition(targets[0].Key, targets[0].Transform.Position with { Z = -30000f }),
                new CopyEarlierPlacedParts(targets[1].Key, earlier.Undo),
                new SetInitiallyDisabled(removed.Key),
                new SetPosition(removed.Key, new Vector3(1, 2, -30000f)),
                new SetPosition(pulledByRun.Key, new Vector3(4, 5, 5)),
            ],
            writing.Orders);
        AssertOverrides(
            written,
            writing,
            new()
            {
                [WrittenPart.FollowUpRemoved] = 1,
                [WrittenPart.FollowUpRestored] = 1,
                [WrittenPart.FollowUpOtherModsObjectRemoved] = 1,
                [WrittenPart.FollowUpOtherModsObjectPulledDown] = 1,
            });
    }

    /// <summary>
    /// The follow-up of the restore pulls down an object no order of the run writes, beside the run's own pull-down of another one: the
    /// follow-up's is counted on its own, so the overrides counted are the records written.
    /// </summary>
    [Fact]
    public void AnObjectOnlyTheFollowUpOfTheRestorePullsDownIsCountedAsTheFollowUpsAndInTheOverridesWritten()
    {
        var targets = TestTargets.CreateMany(1);
        var final = TestSeededDecisions.StartDisablingAll(ObjectsToKeep.Build(targets, [], TestTargets.References(1), TestBaseFacts.Unresolved), 1);
        var other = ModKey.FromNameAndExtension("Other.esp");
        var pulledByRun = TestShapes.Placed(other, 0, default, new Vector3(1, 2, 60));
        var pulledByFollowUp = TestShapes.Placed(other, 1, default, new Vector3(4, 5, 60));
        var settled = new OtherModsObjectsResult(
            StepOutcome.Performed,
            [new OtherModsObjectOutcome(pulledByRun, OtherModsObjectFate.PulledDown, LostShare: 1f, Drop: 50f, ReferenceRoles.None)],
            Rounds: [],
            ProposedRounds: [],
            Failure: null);
        var rock = ChangedRock(0x900);
        var whereRemoved = RestoreWhereRemovedResult.SwitchedOff() with
        {
            FollowUp = new RestoreFollowUp(
                [],
                [new FollowUpOtherObject(new OtherModsObjectOutcome(pulledByFollowUp, OtherModsObjectFate.PulledDown, LostShare: 1f, Drop: 30f, ReferenceRoles.None), [rock.Key])]),
        };
        var writing = new RecordingWriting();

        var written = PatchWriter.Write(new(final, [.. targets], settled, whereRemoved, new ReadPlugin(), writing, TestTargets.TargetName));

        AssertOverrides(written, writing, new() { [WrittenPart.OtherModsObjectPulledDown] = 1, [WrittenPart.FollowUpOtherModsObjectPulledDown] = 1 });
        Assert.Equal(2, written.Overrides.Values.Sum());
    }

    /// <summary>
    /// The run removes an other mod's object and the follow-up of the restore removes it again: one record, counted once, under the
    /// follow-up, whose order is written last.
    /// </summary>
    [Fact]
    public void AnObjectTheRunRemovedAndTheFollowUpOfTheRestoreRemovesAgainIsOneOverrideOfTheFollowUp()
    {
        var targets = TestTargets.CreateMany(1);
        var final = TestSeededDecisions.StartDisablingAll(ObjectsToKeep.Build(targets, [], TestTargets.References(1), TestBaseFacts.Unresolved), 1);
        var removed = TestShapes.Placed(ModKey.FromNameAndExtension("Other.esp"), 0, default, new Vector3(1, 2, 3));
        var settled = new OtherModsObjectsResult(
            StepOutcome.Performed,
            [new OtherModsObjectOutcome(removed, OtherModsObjectFate.RemovedNothingBelow, LostShare: 1f, Drop: 0f, ReferenceRoles.None)],
            Rounds: [],
            ProposedRounds: [],
            Failure: null);
        var rock = ChangedRock(0x900);
        var whereRemoved = RestoreWhereRemovedResult.SwitchedOff() with
        {
            FollowUp = new RestoreFollowUp([], [new FollowUpOtherObject(new OtherModsObjectOutcome(removed, OtherModsObjectFate.Removed, LostShare: 1f, Drop: 0f, ReferenceRoles.None), [rock.Key])]),
        };
        var writing = new RecordingWriting();

        var written = PatchWriter.Write(new(final, [.. targets], settled, whereRemoved, new ReadPlugin(), writing, TestTargets.TargetName));

        AssertOverrides(written, writing, new() { [WrittenPart.FollowUpOtherModsObjectRemoved] = 1 });
    }

    /// <summary>
    /// A removal step restores a target object and the follow-up of the restore lists it again, restored: one record, counted once,
    /// under the follow-up, whose order is written last; the removal step's restore stays in the restored targets for its report.
    /// </summary>
    [Fact]
    public void ATargetARemovalStepRestoredThatTheFollowUpOfTheRestoreListsAgainIsOneOverrideOfTheFollowUp()
    {
        var target = TestTargets.Create(0, TestTargets.At(new Vector3(500, 0, 0)));
        var earlier = EarlierAt(new EulerPlacement(new Vector3(1, 2, 3), Vector3.Zero, 1f));
        var protection = ObjectsToKeep.Build([target], [], TestTargets.References(1), TestBaseFacts.Unresolved);
        var final = RemovalDecisions.Start(protection, [new WhenRemoved.Restore(earlier)]).Apply(RoundKind.TooClose, [Propose(0)]);
        var rock = ChangedRock(0x900);
        var whereRemoved = RestoreWhereRemovedResult.SwitchedOff() with
        {
            FollowUp = new RestoreFollowUp([new FollowUpTarget(new TargetId(0), earlier, [rock.Key])], []),
        };
        var writing = new RecordingWriting();

        var written = PatchWriter.Write(new(final, [target], OtherModsObjectsResult.SwitchedOff, whereRemoved, new ReadPlugin(), writing, TestTargets.TargetName));

        Assert.Equal([0], written.Restored.Select(id => id.Index));
        AssertOverrides(written, writing, new() { [WrittenPart.FollowUpRestored] = 1 });
    }

    /// <summary>The overrides by part are the expected ones, and they add up to the records the orders write, the copies' new records left out.</summary>
    private static void AssertOverrides(WriteSummary written, RecordingWriting writing, Dictionary<WrittenPart, int> expected)
    {
        Assert.Equal(expected.OrderBy(entry => entry.Key), written.Overrides.OrderBy(entry => entry.Key));
        Assert.Equal(writing.Orders.Where(order => order is not AddClone).Select(order => order.Record).Distinct().Count(), written.Overrides.Values.Sum());
    }

    /// <summary>
    /// A rock goes back to its earlier place, where it would float, and the follow-up pulls it down or removes it there. The rock's
    /// restore is written first, then the follow-up's order for the same record, which wins.
    /// </summary>
    [Theory]
    [InlineData(OtherModsObjectFate.PulledDown)]
    [InlineData(OtherModsObjectFate.RemovedNothingBelow)]
    public void ACandidateTheFollowUpTakesAwayWhereItGoesBackIsWrittenAfterItsRestore(OtherModsObjectFate fate)
    {
        var targets = TestTargets.CreateMany(1);
        var final = TestSeededDecisions.StartDisablingAll(ObjectsToKeep.Build(targets, [], TestTargets.References(1), TestBaseFacts.Unresolved), 1);
        var rock = ChangedRock(0x900);
        var arrived = TestShapes.Placed(ModKey.FromNameAndExtension("Base.esm"), 0, default, new Vector3(3, 4, 50)) with { Key = rock.Key };
        var drop = fate == OtherModsObjectFate.PulledDown ? 30f : 0f;
        var cleared = TestAreaShares.Of(AreaVerdict.Cleared);
        var whereRemoved = new RestoreWhereRemovedResult(
            StepOutcome.Performed,
            [new PlacedChangeJudged(rock, null, cleared, cleared, Refused: null, IsRestored: true, ChangeJudge.KindOf(rock), KeptForArea: null, PlacedBack: null)],
            [],
            [],
            [],
            [],
            Passes: 1,
            DoorsLeftRemoved: [],
            Failures: [],
            RestoreWhereRemovedTimes.None)
        {
            FollowUp = new RestoreFollowUp([], [new FollowUpOtherObject(new OtherModsObjectOutcome(arrived, fate, LostShare: 1f, drop, ReferenceRoles.None), [rock.Key])]),
        };
        var writing = new RecordingWriting();

        var written = PatchWriter.Write(new(final, [.. targets], OtherModsObjectsResult.SwitchedOff, whereRemoved, new ReadPlugin(), writing, TestTargets.TargetName));

        AssertOverrides(
            written,
            writing,
            new() { [fate == OtherModsObjectFate.PulledDown ? WrittenPart.FollowUpOtherModsObjectPulledDown : WrittenPart.FollowUpOtherModsObjectRemoved] = 1 });
        Assert.Equal<WriteOrder>(
            fate == OtherModsObjectFate.PulledDown
                ? [new CopyEarlierPlacedParts(rock.Key, rock.Earlier.Undo), new SetPosition(rock.Key, new Vector3(3, 4, 20))]
                : [new CopyEarlierPlacedParts(rock.Key, rock.Earlier.Undo), new SetInitiallyDisabled(rock.Key), new SetPosition(rock.Key, new Vector3(3, 4, -30000f))],
            writing.Orders.Where(order => order.Record == rock.Key && order is not PlaceInCell));
    }

    private static LandChangedByCleanedMod ChangedLand(uint id) =>
        new(
            new ExteriorCell(TestTargets.Space, 0, 0),
            new RecordKey(new PluginName("Base.esm"), id),
            PointSet.Of([]),
            PointSet.Of([]),
            QuarterSet.Of([]),
            UndoRest: false,
            new LandLeftToLater(PointSet.Of([]), PointSet.Of([]), QuarterSet.Of([]), Rest: false, []),
            TestTargets.TargetName,
            Heights: null,
            EarlierPlugin: new PluginName("Below.esp"));

    private static LandChangeJudged JudgedLand(LandChangedByCleanedMod land, PointSet heights, QuarterSet textures) =>
        new(land, Never: null, Quarters: [], heights, PointSet.Of([]), textures, Rest: false, Kept: [], FinalHeights: null, new CellEdgeHeights(null, null, null, null), IsRestored: true, ChangedQuarters: [], RestKeptForArea: null);

    private static RecordChangedByCleanedMod ChangedRock(uint id) =>
        new(
            new RecordKey(new PluginName("Base.esm"), id),
            EditorId: null,
            TestTargets.Space,
            new CellFact(TestTargets.SpaceKey(0x200), null),
            TestTargets.TargetName,
            WinningModPastPatches: TestTargets.TargetName,
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

        var written = PatchWriter.Write(new(final, [target], OtherModsObjectsResult.SwitchedOff, RestoreWhereRemovedResult.SwitchedOff(),new ReadPlugin(), writing, TestTargets.TargetName));

        return (written, writing);
    }

    private static ProposedRemoval Propose(int index) => new(new TargetId(index), new TooClose(new OtherId(0), new TooCloseCause.ObjectSize()));

    private const uint FirstAddedId = 0x800;

    private sealed class RecordingWriting : IPatchWriting
    {
        public List<WriteOrder> Orders { get; } = [];

        public IWritableRecords? Records { get; private set; }

        public void WritePatchMarker() => throw new NotSupportedException();

        /// <returns>A new key in the patch for each copy ordered.</returns>
        public ImmutableArray<RecordKey> Write(IWritableRecords records, IReadOnlyList<WriteOrder> orders)
        {
            Records = records;
            Orders.AddRange(orders);
            return [.. orders.OfType<AddClone>().Select((_, index) => new RecordKey(TestTargets.PatchName, FirstAddedId + (uint)index))];
        }

        /// <summary>A world without navmeshes and doors, so a door link written names no navmesh to check.</summary>
        public INavmeshWorldView ReadFinalNavmeshes(IWritableRecords records) => new WorldWithoutNavmeshes();

        public TResult ReadNavmeshesIfWritten<TResult>(IWritableRecords records, IReadOnlyList<WriteOrder> orders, Func<INavmeshWorldView, TResult> read) =>
            throw new NotSupportedException();
    }

    private sealed class WorldWithoutNavmeshes : INavmeshWorldView
    {
        public IEnumerable<ImmutableArray<RecordKey>> PreferredPathSets => [];

        public NavmeshView? NavmeshOf(RecordKey navmesh) => null;

        public bool Exists(RecordKey navmesh) => false;

        public NavmeshEntryView? EntryOf(RecordKey navmesh) => null;

        public DoorView? DoorOf(RecordKey door) => null;

        public IEnumerable<RecordKey> NavmeshesLinkingTo(RecordKey navmesh) => [];

        public IEnumerable<RecordKey> DoorsLinkingTo(RecordKey navmesh) => [];
    }

    private sealed class NoRecords : IWritableRecords;

    /// <summary>A reader whose placed records were read: none has an enable parent.</summary>
    private sealed class ReadPlugin : IPluginRecords
    {
        public IWritableRecords Records { get; } = new NoRecords();

        public IWritableRecords ReadWritableRecords() => Records;

        public IRecordIndex ReadRecordIndex() => throw new NotSupportedException();

        public bool TryParsePluginName(string text, out PluginName name) => throw new NotSupportedException();

        public PlacedRecordFacts ReadPlacedRecords(PlacedReadScope scope, VersionWalkStop stopsTheWalk) => throw new NotSupportedException();

        public NonPlacedLinks ReadLinks(PluginName target, IReadOnlySet<RecordKey> targets, IReadOnlySet<PluginName> linkedPlugins) => throw new NotSupportedException();

        public CellGridPoint? ReadHomeCellGrid(RecordKey record) => throw new NotSupportedException();

        public float[]? ReadTerrain(ExteriorCell cell) => throw new NotSupportedException();

        public float[]? ReadTerrainBelow(PluginName plugin, ExteriorCell cell) => throw new NotSupportedException();

        public TerrainVersion[] ReadTerrainVersions(ExteriorCell cell) => throw new NotSupportedException();

        public WaterCopies ReadWaterCopies(ExteriorCell cell) => throw new NotSupportedException();

        public ExteriorCell? FindExteriorCell(RecordKey landOrCell) => throw new NotSupportedException();
        public RecordKey[] ReadTextureMaterialChain(RecordKey texture) => throw new NotSupportedException();

        public RecordKey? FindLandWorldspace(RecordKey space) => throw new NotSupportedException();

        public NavmeshWorldTriangle[]? ReadNavmeshes(NavmeshBucket bucket) => throw new NotSupportedException();

        public NpcTraits? NpcTraitsOf(BaseKey npc) => throw new NotSupportedException();

        public TraitSupplier? TraitSupplierOf(BaseKey spawn, NpcTemplateFlag templateFlag) => throw new NotSupportedException();

        public IReadOnlyList<RecordKey> LeveledEntriesOf(RecordKey list, NpcTemplateFlag templateFlag) => throw new NotSupportedException();
    }
}
