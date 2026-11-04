using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// What the patch holds for an object put back to its earlier version, read back from in-memory plugins: the base game places a
/// crate, the cleaned mod moves it (and in some tests also changes it), and the crate is restored by the real collector, decisions
/// and patch writer. The restored object is the only target of each world.
/// </summary>
public sealed class RestoredObjectPatchTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("aor-restored-");

    private const int Workers = 1;
    private const float DisabledDepth = -30000f;
    private const int EastGridX = 1;
    private const string EastCellEditorId = "AorEast";
    private const float CleanedModRadius = 77f;
    private const string CleanedModEditorId = "Renamed_Crate";
    private const float LaterRadius = 99f;

    private static readonly Vector3 EarlierPosition = new(1000, 1000, 40);
    private static readonly Vector3 EarlierRotation = new(0, 0, 0.5f);
    private static readonly Vector3 MovedPosition = new(ExteriorGrid.CellSize + 1000, 1000, 40);
    private static readonly Vector3 MovedRotation = new(0, 0, 1.5f);

    private static readonly WhatToCollect Plan = new(new ScanNeeds(), SettingDefaults.MovedDistanceInUnits, ProcessReplaced: false);

    private static readonly RemovalReason Reason = new TooClose(new OtherId(0), new TooCloseCause.ObjectSize());

    private readonly FixtureMod _baseGame = new("Skyrim.esm");
    private readonly FixtureMod _cleaned;
    private readonly FormKey _eastKey;
    private readonly FixtureCell _cleanedEast;
    private readonly FormKey _crateBase;

    /// <summary>The crate the base game places, once a test placed it.</summary>
    private FormKey _crate;

    public RestoredObjectPatchTests()
    {
        _cleaned = new FixtureMod("Cleaned.esp", _baseGame);
        FixtureSpaces.CreateIn(_baseGame).AddOverridesTo(_cleaned);
        _eastKey = _baseGame.NextKey();
        _baseGame.AddExteriorCell(_eastKey, EastCellEditorId, EastGridX);
        _cleanedEast = _cleaned.AddExteriorCell(_eastKey, EastCellEditorId, EastGridX);
        _crateBase = _baseGame.AddModeledStatic("B_CrateBase", FixtureMeshes.CrateModel);
    }

    public void Dispose() => _root.Delete(recursive: true);

    /// <summary>The radius lies in the base and data, whose last changer is the cleaned mod, so it goes back with the placement; the editor ID is the record's identity and stays.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData(2f)]
    public void ARestoreWritesBackEveryPartTheCleanedModLastChangedAndKeepsTheEditorId(float? earlierScale)
    {
        var original = PlaceOriginal(_baseGame.Interior, earlierScale);
        var moved = Move(_cleaned.Interior, original);

        var patch = RestoreTheMovedCrate();

        var written = Assert.Single(patch.EnumerateMajorRecords<IPlacedObjectGetter>());
        Assert.Equal((EarlierPosition, EarlierRotation, earlierScale, (float?)null), (PositionOf(written), RotationOf(written), written.Scale, written.Radius));
        var expected = original.DeepCopy();
        expected.EditorID = moved.EditorID;
        Assert.True(expected.Equals(written));
    }

    /// <summary>An earlier patcher of the group gave the crate another radius after the cleaned mod, so the base and data stay as the patch has them.</summary>
    [Fact]
    public void ARadiusAnEarlierPatcherChangedAgainStaysWhileThePlacementGoesBack()
    {
        var original = PlaceOriginal(_baseGame.Interior, scale: 2f);
        Move(_cleaned.Interior, original);

        var patch = RestoreTheMovedCrate(earlierPatcherChange: placed => placed.Radius = LaterRadius);

        var written = Assert.Single(patch.EnumerateMajorRecords<IPlacedObjectGetter>());
        Assert.Equal((EarlierPosition, EarlierRotation, (float?)2f, (float?)LaterRadius), (PositionOf(written), RotationOf(written), written.Scale, written.Radius));
    }

    /// <summary>The cleaned mod deleted the crate, which dropped its fields: its presence goes back as a whole copy of the earlier version, in that version's list.</summary>
    [Fact]
    public void UndoingADeletionWritesAWholeUndeletedCopyOfTheEarlierVersionIntoTheListItIsPlacedIn()
    {
        var original = PlaceOriginal(_baseGame.Interior, scale: 2f, persistent: true);
        var deleted = _cleaned.Interior.PlaceCopyOf(original);
        deleted.SetPersistent(false);
        deleted.IsDeleted = true;
        deleted.Placement = null;
        var state = FixturePatcherState.Create([_baseGame.Mod, _cleaned.Mod], _root.FullName, Path.Combine(_root.FullName, FixturePatcherState.PatchModKey.FileName));
        var key = original.FormKey.ToRecordKey();
        var handles = new RecordHandles(
            new Dictionary<RecordKey, PlacedRecordHandle> { [key] = new(deleted, new PlacedRecordLocation(state.LinkCache.Resolve<ICellGetter>(_cleaned.Interior.Key), InPersistentList: false)) },
            new Dictionary<RecordKey, IPlacedGetter> { [key] = original },
            new PlacedRecordParts(new PlacedPartTolerances()),
            areaRecords: null);

        new PatchWriting(new SynthesisWriteCache(state.LinkCache), state.PatchMod, Program.PatchMarkerText).Write(
            handles, [new PlaceInCell(key, _baseGame.Interior.Key.ToRecordKey(), InPersistentList: true), new CopyEarlierPlacedParts(key, PlacedParts.Position | PlacedParts.Presence)]);

        var written = Assert.Single(state.PatchMod.EnumerateMajorRecords<IPlacedObjectGetter>());
        Assert.True(original.Equals(written));
        Assert.Equal((true, false), StandsInPersistentAndTemporaryList(state.PatchMod, written));
    }

    [Fact]
    public void ARestoreOfAnotherBaseCopiesTheEarlierBaseWithTheEarlierData()
    {
        var original = PlaceOriginal(_baseGame.Interior, scale: 2f);
        var moved = Move(_cleaned.Interior, original);
        moved.Base.SetTo(_cleaned.AddModeledStatic("C_OtherCrateBase", FixtureMeshes.CrateModel));

        var patch = RestoreTheMovedCrate();

        var written = Assert.Single(patch.EnumerateMajorRecords<IPlacedObjectGetter>());
        Assert.Equal(_crateBase, written.Base.FormKey);
        var expected = original.DeepCopy();
        expected.EditorID = moved.EditorID;
        Assert.True(expected.Equals(written));
    }

    [Fact]
    public void ARestoreIntoAnotherCellPutsTheOverrideInTheEarlierCellAndNotInTheCleanedModsCell()
    {
        var original = PlaceOriginal(_baseGame.Exterior);
        Move(_cleanedEast, original);

        var patch = RestoreTheMovedCrate();

        var written = Assert.Single(patch.EnumerateMajorRecords<IPlacedObjectGetter>());
        Assert.Equal(EarlierPosition, PositionOf(written));
        var cells = patch.EnumerateMajorRecords<ICellGetter>().ToList();
        Assert.Equal(_baseGame.Exterior.Key, Assert.Single(cells, cell => cell.Temporary.Any(placed => placed.FormKey == written.FormKey)).FormKey);
        Assert.DoesNotContain(cells, cell => cell.FormKey == _eastKey);
    }

    [Fact]
    public void APersistentObjectStaysInTheCellOfTheVersionTheGameShowsNow()
    {
        var original = PlaceOriginal(_baseGame.Exterior, persistent: true);
        Move(_cleanedEast, original, persistent: true);

        var patch = RestoreTheMovedCrate();

        var written = Assert.Single(patch.EnumerateMajorRecords<IPlacedObjectGetter>());
        Assert.Equal(EarlierPosition, PositionOf(written));
        var cells = patch.EnumerateMajorRecords<ICellGetter>().ToList();
        Assert.Equal(_eastKey, Assert.Single(cells, cell => cell.Persistent.Any(placed => placed.FormKey == written.FormKey)).FormKey);
        Assert.DoesNotContain(cells, cell => cell.Temporary.Any(placed => placed.FormKey == written.FormKey));
        Assert.DoesNotContain(cells, cell => cell.FormKey == _baseGame.Exterior.Key);
    }

    /// <summary>An earlier patcher of the group moved the object (so the patch holds it in the cleaned mod's cell); the restore moves that copy.</summary>
    [Fact]
    public void ARestoreMovesTheCopyAnEarlierPatcherWroteSoThePatchHoldsTheRecordOnce()
    {
        var original = PlaceOriginal(_baseGame.Exterior);
        Move(_cleanedEast, original);

        var patch = RestoreTheMovedCrate(earlierPatcherWrites: key => [new SetPosition(key, MovedPosition + Vector3.UnitX)]);

        var written = Assert.Single(patch.EnumerateMajorRecords<IPlacedObjectGetter>());
        Assert.Equal(EarlierPosition, PositionOf(written));
        var cells = patch.EnumerateMajorRecords<ICellGetter>().ToList();
        Assert.Equal(_baseGame.Exterior.Key, Assert.Single(cells, cell => cell.Temporary.Any(placed => placed.FormKey == written.FormKey)).FormKey);
        Assert.Empty(cells.Single(cell => cell.FormKey == _eastKey).Temporary);
    }

    [Fact]
    public void ARestoredObjectIsNeitherDisabledNorSunkNorGivenAnEnableParent()
    {
        var original = PlaceOriginal(_baseGame.Interior);
        Move(_cleaned.Interior, original);

        var written = Assert.Single(RestoreTheMovedCrate().EnumerateMajorRecords<IPlacedObjectGetter>());

        Assert.False(written.IsInitiallyDisabled());
        Assert.NotEqual(DisabledDepth, written.Placement!.Position.Z);
        Assert.Null(written.EnableParent);
    }

    [Fact]
    public void TheInputRecordsAreNotChangedByARestoreOfAnotherBaseIntoAnotherCell()
    {
        var original = PlaceOriginal(_baseGame.Exterior);
        var moved = Move(_cleanedEast, original);
        moved.Base.SetTo(_cleaned.AddModeledStatic("C_OtherCrateBase", FixtureMeshes.CrateModel));
        var before = SnapshotOfInputs();

        RestoreTheMovedCrate();

        var after = SnapshotOfInputs();
        Assert.Equal(before.Keys, after.Keys);
        Assert.All(before, entry => Assert.Equal(entry.Value, after[entry.Key]));
    }

    [Fact]
    public void ARestoreOfAnotherBaseFromATemporaryEarlierVersionStaysPersistentWhereTheObjectNowStandsInThePersistentList()
    {
        var original = PlaceOriginal(_baseGame.Exterior);
        var moved = Move(_cleanedEast, original, persistent: true);
        moved.Base.SetTo(_cleaned.AddModeledStatic("C_OtherCrateBase", FixtureMeshes.CrateModel));

        var patch = RestoreTheMovedCrate();

        var written = Assert.Single(patch.EnumerateMajorRecords<IPlacedObjectGetter>());
        Assert.Equal(_crateBase, written.Base.FormKey);
        Assert.True(written.IsPersistent());
        Assert.Equal((true, false), StandsInPersistentAndTemporaryList(patch, written));
    }

    [Fact]
    public void ARestoreOfAnotherBaseFromAPersistentEarlierVersionStaysTemporaryWhereTheObjectNowStandsInTheTemporaryList()
    {
        var original = PlaceOriginal(_baseGame.Exterior, persistent: true);
        var moved = Move(_cleanedEast, original, persistent: false);
        moved.Base.SetTo(_cleaned.AddModeledStatic("C_OtherCrateBase", FixtureMeshes.CrateModel));

        var patch = RestoreTheMovedCrate();

        var written = Assert.Single(patch.EnumerateMajorRecords<IPlacedObjectGetter>());
        Assert.Equal(_crateBase, written.Base.FormKey);
        Assert.False(written.IsPersistent());
        Assert.Equal((false, true), StandsInPersistentAndTemporaryList(patch, written));
    }

    private PlacedObject PlaceOriginal(FixtureCell cell, float? scale = null, bool persistent = false)
    {
        var original = cell.Place("B_Crate", _crateBase, EarlierPosition, persistent);
        _crate = original.FormKey;
        original.Placement!.Rotation = new P3Float(EarlierRotation.X, EarlierRotation.Y, EarlierRotation.Z);
        original.Scale = scale;
        original.SetPersistent(persistent);
        return original;
    }

    /// <summary>The cleaned mod's override of the crate: moved and turned, scaled, renamed and given a radius.</summary>
    private static PlacedObject Move(FixtureCell cell, PlacedObject original, bool persistent = false)
    {
        var moved = cell.PlaceCopyOf(original, persistent);
        moved.Placement!.Position = new P3Float(MovedPosition.X, MovedPosition.Y, MovedPosition.Z);
        moved.Placement.Rotation = new P3Float(MovedRotation.X, MovedRotation.Y, MovedRotation.Z);
        moved.Scale = 1.5f;
        moved.EditorID = CleanedModEditorId;
        moved.Radius = CleanedModRadius;
        moved.SetPersistent(persistent);
        return moved;
    }

    /// <summary>
    /// The earlier patcher of the group has written the crate, unchanged, into its output before this patcher starts, as a real group
    /// runs: the scan sees the patch as the winner, in the cell the earlier patcher left it in. A position the earlier patcher changed
    /// would stay, so it writes the cleaned mod's.
    /// </summary>
    [Fact]
    public void ARestoreMovesTheCopyAnEarlierPatchersOutputHoldsAndTheScanSawAsTheWinner()
    {
        var original = PlaceOriginal(_baseGame.Exterior);
        Move(_cleanedEast, original);

        var patch = RestoreTheMovedCrate(earlierPatcherWrites: key => [new SetPosition(key, MovedPosition)], earlierPatchLoadedBeforeTheScan: true);

        var written = Assert.Single(patch.EnumerateMajorRecords<IPlacedObjectGetter>());
        Assert.Equal(EarlierPosition, PositionOf(written));
        var cells = patch.EnumerateMajorRecords<ICellGetter>().ToList();
        Assert.Equal(_baseGame.Exterior.Key, Assert.Single(cells, cell => cell.Temporary.Any(placed => placed.FormKey == written.FormKey)).FormKey);
        Assert.Empty(cells.Single(cell => cell.FormKey == _eastKey).Temporary);
    }

    /// <summary>Broken data: the record stands in the persistent children of the cleaned mod's cell without its Persistent flag.</summary>
    [Fact]
    public void ARestoreTakesTheCopyAnEarlierPatcherWroteOutOfThePersistentListItStandsIn()
    {
        var original = PlaceOriginal(_baseGame.Exterior);
        Move(_cleanedEast, original, persistent: true).SetPersistent(false);

        var patch = RestoreTheMovedCrate(earlierPatcherWrites: key => [new SetPosition(key, MovedPosition + Vector3.UnitX)]);

        var written = Assert.Single(patch.EnumerateMajorRecords<IPlacedObjectGetter>());
        Assert.Equal((false, true), StandsInPersistentAndTemporaryList(patch, written));
        Assert.Empty(patch.EnumerateMajorRecords<ICellGetter>().Single(cell => cell.FormKey == _eastKey).Persistent);
    }

    [Fact]
    public void ARestoreOfAnotherBaseIntoAnotherCellMovesTheCopyAnEarlierPatchersOutputHoldsSoThePatchHoldsTheRecordOnce()
    {
        var original = PlaceOriginal(_baseGame.Exterior);
        var moved = Move(_cleanedEast, original);
        moved.Base.SetTo(_cleaned.AddModeledStatic("C_OtherCrateBase", FixtureMeshes.CrateModel));

        var patch = RestoreTheMovedCrate(earlierPatcherWrites: key => [new SetPosition(key, MovedPosition)], earlierPatchLoadedBeforeTheScan: true);

        var written = Assert.Single(patch.EnumerateMajorRecords<IPlacedObjectGetter>());
        Assert.Equal(_crateBase, written.Base.FormKey);
        Assert.Equal(EarlierPosition, PositionOf(written));
        var cells = patch.EnumerateMajorRecords<ICellGetter>().ToList();
        Assert.Equal(_baseGame.Exterior.Key, Assert.Single(cells, cell => cell.Temporary.Any(placed => placed.FormKey == written.FormKey)).FormKey);
        Assert.Empty(cells.Single(cell => cell.FormKey == _eastKey).Temporary);
    }

    /// <summary>Collects the cleaned mod's moved crate, restores it in a TooClose round and writes the patch.</summary>
    /// <param name="earlierPatcherWrites">What an earlier patcher of the group already wrote into the patch for the crate; none when the patch starts empty.</param>
    /// <param name="earlierPatchLoadedBeforeTheScan">Whether the earlier patcher's output is the patch this patcher starts from, so the scan sees it; otherwise it is written after the scan.</param>
    /// <param name="earlierPatcherChange">A change an earlier patcher of the group made to its override of the cleaned mod's crate, which the scan sees; none when null.</param>
    private ISkyrimModGetter RestoreTheMovedCrate(
        Func<RecordKey, IReadOnlyList<WriteOrder>>? earlierPatcherWrites = null, bool earlierPatchLoadedBeforeTheScan = false, Action<IPlacedObject>? earlierPatcherChange = null)
    {
        var state = earlierPatchLoadedBeforeTheScan
            ? CreateStateAfterEarlierPatcher(earlierPatcherWrites!)
            : FixturePatcherState.Create([_baseGame.Mod, _cleaned.Mod], _root.FullName, Path.Combine(_root.FullName, FixturePatcherState.PatchModKey.FileName));
        earlierPatcherChange?.Invoke(OverrideInPatch(state));
        using var world = FixturePatcherState.CreateFinalWorld(state);
        var reader = new PluginRecordsFactory(world, Workers).Open();
        var targets = PlacedObjects.Collect(reader, Trust(), Plan).Targets;
        var moved = Assert.Single(targets);
        Assert.Equal(TargetOrigin.Moved, moved.Origin);
        var writing = new PatchWriting(new SynthesisWriteCache(state.LinkCache), state.PatchMod, Program.PatchMarkerText);
        if (earlierPatcherWrites is not null && !earlierPatchLoadedBeforeTheScan) writing.Write(reader.ReadWritableRecords(), earlierPatcherWrites(moved.Key));
        var protection = ObjectsToKeep.Build(targets, [], TestTargets.References(targets.Length), TestBaseFacts.Unresolved);
        var final = RemovalDecisions.Start(protection, [new WhenRemoved.Restore(moved.EarlierVersion!)]).Apply(RoundKind.TooClose, [new ProposedRemoval(moved.Id, Reason)]);

        PatchWriter.Write(new(final, targets, OtherModsObjectsResult.SwitchedOff, RestoreWhereRemovedResult.SwitchedOff(), reader, writing, _cleaned.Key.ToPluginName()));

        return state.PatchMod;
    }

    /// <summary>The state of the patcher after the earlier one: the earlier patcher's output file is imported as the patch.</summary>
    private IPatcherState<ISkyrimMod, ISkyrimModGetter> CreateStateAfterEarlierPatcher(Func<RecordKey, IReadOnlyList<WriteOrder>> earlierPatcherWrites)
    {
        var earlierOutput = Path.Combine(Directory.CreateDirectory(Path.Combine(_root.FullName, "earlier")).FullName, FixturePatcherState.PatchModKey.FileName);
        var earlier = FixturePatcherState.Create([_baseGame.Mod, _cleaned.Mod], _root.FullName, earlierOutput);
        using (var world = FixturePatcherState.CreateFinalWorld(earlier))
        {
            var reader = new PluginRecordsFactory(world, Workers).Open();
            var moved = Assert.Single(PlacedObjects.Collect(reader, Trust(), Plan).Targets);
            new PatchWriting(new SynthesisWriteCache(earlier.LinkCache), earlier.PatchMod, Program.PatchMarkerText).Write(reader.ReadWritableRecords(), earlierPatcherWrites(moved.Key));
        }

        earlier.PatchMod.WriteToBinary(earlierOutput);
        return FixturePatcherState.CreateAfterEarlierPatcher(
            [_baseGame.Mod, _cleaned.Mod], _root.FullName, Path.Combine(_root.FullName, FixturePatcherState.PatchModKey.FileName), earlierOutput);
    }

    /// <summary>The patch's override of the crate, a copy of the cleaned mod's version in the patch's copy of its cell.</summary>
    private IPlacedObject OverrideInPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state) =>
        state.LinkCache.ResolveContext<IPlacedObject, IPlacedObjectGetter>(_crate).GetOrAddAsOverride(state.PatchMod);

    private ModTrustTable Trust() =>
        TestModTrust.Of(
            _cleaned.Key.ToPluginName(),
            FixturePatcherState.PatchModKey.ToPluginName(),
            trusted: [_baseGame.Key.ToPluginName()],
            untrusted: [_cleaned.Key.ToPluginName()]);

    /// <summary>Every placed record of the two plugins, copied, and the number of children of each of their cells.</summary>
    private Dictionary<string, object> SnapshotOfInputs() =>
        new[] { _baseGame.Mod, _cleaned.Mod }
            .SelectMany(mod => mod.EnumerateMajorRecords<IPlacedObjectGetter>().Select(placed => ($"{mod.ModKey} {placed.FormKey}", (object)placed.DeepCopy()))
                .Concat(mod.EnumerateMajorRecords<ICellGetter>().Select(cell => ($"{mod.ModKey} {cell.FormKey} children", (object)(cell.Persistent.Count, cell.Temporary.Count)))))
            .ToDictionary(entry => entry.Item1, entry => entry.Item2);

    private static Vector3 PositionOf(IPlacedObjectGetter placed) => new(placed.Placement!.Position.X, placed.Placement.Position.Y, placed.Placement.Position.Z);

    private static Vector3 RotationOf(IPlacedObjectGetter placed) => new(placed.Placement!.Rotation.X, placed.Placement.Rotation.Y, placed.Placement.Rotation.Z);

    private static (bool Persistent, bool Temporary) StandsInPersistentAndTemporaryList(ISkyrimModGetter patch, IPlacedObjectGetter written)
    {
        var cells = patch.EnumerateMajorRecords<ICellGetter>().ToList();
        return (cells.Any(cell => cell.Persistent.Any(placed => placed.FormKey == written.FormKey)), cells.Any(cell => cell.Temporary.Any(placed => placed.FormKey == written.FormKey)));
    }
}
