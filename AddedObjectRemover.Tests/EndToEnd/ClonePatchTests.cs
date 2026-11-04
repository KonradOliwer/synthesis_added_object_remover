using System.Collections.Immutable;
using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Allocators;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// What the patch holds for a copy the restore adds, read back from in-memory plugins: the base game places two rocks in its interior
/// and the cleaned mod moves both. Each rock goes back with a copy left where the cleaned mod put it.
/// </summary>
public sealed class ClonePatchTests
{
    private const float DisabledDepth = -30000f;

    private static readonly WhatToCollect Plan = new(new ScanNeeds(), SettingDefaults.MovedDistanceInUnits, ProcessReplaced: false);

    /// <summary>A scan that also reads the records the cleaned mod changed, for the restore.</summary>
    private static readonly WhatToCollect ChangedRecordsPlan =
        new(new ScanNeeds(AreaRecordVersions: new AreaRecordReads(new AreaPartTolerances(LandHeight: 8f, WaterHeight: 8f), Cells: true, Lands: true)), SettingDefaults.MovedDistanceInUnits, ProcessReplaced: false);

    private readonly FixtureMod _baseGame = new("Skyrim.esm");
    private readonly FixtureMod _cleaned;
    private readonly FormKey _rockBase;
    private readonly PlacedObject[] _originals;
    private readonly PlacedObject[] _moved;

    public ClonePatchTests()
    {
        _cleaned = new FixtureMod("Cleaned.esp", _baseGame);
        FixtureSpaces.CreateIn(_baseGame).AddOverridesTo(_cleaned);
        _rockBase = _baseGame.AddModeledStatic("B_RockBase", FixtureMeshes.CrateModel);
        _originals = [.. Enumerable.Range(0, 2).Select(index => _baseGame.Interior.Place($"B_Rock{index}", _rockBase, new Vector3(1000 + 300 * index, 1000, 40)))];
        _moved = [.. _originals.Select(original => Move(_cleaned.Interior, original))];
    }

    /// <summary>A rock outdoors, in an exterior cell of the base game's worldspace: its copy stands in the temporary list of that cell.</summary>
    [Fact]
    public void AnOutdoorCopyStandsInTheTemporaryListOfTheExteriorCellItsRecordStandsInNow()
    {
        var eastKey = _baseGame.NextKey();
        var baseEast = _baseGame.AddExteriorCell(eastKey, "AorCloneEast", 1);
        var cleanedEast = _cleaned.AddExteriorCell(eastKey, "AorCloneEast", 1);
        var outdoor = baseEast.Place("B_OutdoorRock", _rockBase, new Vector3(ExteriorGrid.CellSize + 500, 1000, 40));
        var moved = Move(cleanedEast, outdoor);
        var state = CreateState();

        var added = Assert.Single(WriteClones(state, workers: 1, outdoor));

        var cell = Assert.Single(state.PatchMod.EnumerateMajorRecords<ICellGetter>());
        var clone = Assert.Single(cell.Temporary);
        Assert.Equal((eastKey, added, PositionOf(moved)), (cell.FormKey, clone.FormKey.ToRecordKey(), PositionOf((IPlacedObjectGetter)clone)));
        Assert.Empty(cell.Persistent);
    }

    /// <summary>
    /// An earlier step of the group already wrote the moved rock, unchanged, into the patch, so the rock the game shows now is the
    /// patch's record. The restore sets that record back to the rock's old place; the copy, written first, stands where the cleaned mod
    /// put the rock.
    /// </summary>
    [Fact]
    public void InABatchStepWhoseRockIsAlreadyInThePatchTheCopyStandsWhereTheCleanedModPutTheRock()
    {
        var state = CreateState();
        state.LinkCache.ResolveContext<IPlacedObject, IPlacedObjectGetter>(_originals[0].FormKey).GetOrAddAsOverride(state.PatchMod);
        using var world = FixturePatcherState.CreateFinalWorld(state);
        var reader = new PluginRecordsFactory(world, 1).Open();
        var collected = PlacedObjects.Collect(reader, Trust(), ChangedRecordsPlan);
        var change = Assert.Single(collected.ChangedRecords, record => record.Key == Key(_originals[0]));
        Assert.True(change.Earlier.Undo.HasFlag(PlacedParts.Position));
        var cleared = TestAreaShares.Of(AreaVerdict.Cleared);
        var whereRemoved = new RestoreWhereRemovedResult(
            StepOutcome.Performed,
            [
                new PlacedChangeJudged(
                    change, null, cleared, cleared, Refused: null, IsRestored: true, ChangeJudge.KindOf(change), KeptForArea: null, PlacedBack: null, new CloneJudged(CloneNeed.AreaStillStands, RefusedBy: null)),
            ],
            [], [], [], [], Passes: 1, DoorsLeftRemoved: [], Failures: [], RestoreWhereRemovedTimes.None);
        var protection = ObjectsToKeep.Build(collected.Targets, [], TestTargets.References(collected.Targets.Length), TestBaseFacts.Unresolved);
        var final = RemovalDecisions.Start(protection, [.. collected.Targets.Select(_ => (WhenRemoved)new WhenRemoved.Disable())]);

        var written = PatchWriter.Write(new(final, collected.Targets, OtherModsObjectsResult.SwitchedOff, whereRemoved, reader, Writing(state), _cleaned.Key.ToPluginName()));

        var copy = Assert.Single(written.Clones);
        var records = state.PatchMod.EnumerateMajorRecords<IPlacedObjectGetter>().ToDictionary(record => record.FormKey.ToRecordKey());
        Assert.Equal(2, records.Count);
        Assert.Equal(PositionOf(_originals[0]), PositionOf(records[Key(_originals[0])]));
        Assert.Equal(PositionOf(_moved[0]), PositionOf(records[copy.Copy]));
    }

    /// <summary>The copy is the record the game shows now, the cleaned mod's, with a new FormKey of the patch and the marker, in the temporary list of its cell; the input is not changed.</summary>
    [Fact]
    public void ACopyIsTheWholeRecordTheGameShowsWithANewFormKeyAndTheMarkerInTheTemporaryListOfItsCell()
    {
        var state = CreateState();
        var before = _moved[0].DeepCopy();

        var added = WriteClones(state, workers: 1, _originals[0]);

        var clone = Assert.Single(state.PatchMod.EnumerateMajorRecords<IPlacedObjectGetter>());
        Assert.Equal([clone.FormKey.ToRecordKey()], added.ToArray());
        Assert.Equal(FixturePatcherState.PatchModKey, clone.FormKey.ModKey);
        var expected = _moved[0].Duplicate(clone.FormKey);
        expected.EditorID = MarkerOf(_originals[0]);
        Assert.True(expected.Equals(clone));
        var cell = Assert.Single(state.PatchMod.EnumerateMajorRecords<ICellGetter>());
        Assert.Equal((_cleaned.Interior.Key, true, false), (cell.FormKey, cell.Temporary.Any(placed => placed.FormKey == clone.FormKey), cell.Persistent.Any(placed => placed.FormKey == clone.FormKey)));
        Assert.True(before.Equals(_moved[0]));
    }

    [Fact]
    public void TheCopiesGetTheSameFormKeysWithOneOrEightWorkers()
    {
        var one = WriteClones(CreateState(), workers: 1, _originals);
        var eight = WriteClones(CreateState(), workers: 8, _originals);

        Assert.Equal(2, one.Length);
        Assert.Equal(one.ToArray(), eight.ToArray());
    }

    /// <summary>A FormKey allocator that keeps its keys by EditorID across runs, as Synthesis persistence does, gives a copy the same FormKey in a second run.</summary>
    [Fact]
    public void ThePatchHandsOutACopysFormKeyForItsMarkerSoAPersistentAllocatorKeepsItAcrossRuns()
    {
        Dictionary<string, FormKey> persisted = [];
        var first = CreateState();
        first.PatchMod.SetAllocator(new KeptByEditorId(first.PatchMod, persisted));
        var second = CreateState();
        second.PatchMod.SetAllocator(new KeptByEditorId(second.PatchMod, persisted));
        second.PatchMod.GetNextFormKey();

        var inFirst = WriteClones(first, workers: 1, _originals[1]);
        var inSecond = WriteClones(second, workers: 1, _originals[1]);

        Assert.Equal([MarkerOf(_originals[1])], persisted.Keys);
        Assert.Equal(inFirst.ToArray(), inSecond.ToArray());
    }

    /// <summary>
    /// A later step for the same cleaned mod, writing into the same patch, reads the copy as one of the cleaned mod's objects and
    /// removes it: the patch's copy is disabled and sunk where it stands, and stays the only record with its FormKey.
    /// </summary>
    [Fact]
    public void ALaterStepsRemovalOfACopyInThisGroupsPatchDisablesAndSinksItInPlace()
    {
        var state = CreateState();
        var added = Assert.Single(WriteClones(state, workers: 1, _originals[0]));
        using var world = FixturePatcherState.CreateFinalWorld(state);
        var reader = new PluginRecordsFactory(world, 1).Open();
        var clone = Assert.Single(PlacedObjects.Collect(reader, Trust(), Plan).Targets, target => target.CloneOf is not null);

        Writing(state).Write(reader.ReadWritableRecords(), [.. RemovalWrite.OrdersFor(clone)]);

        Assert.Equal((added, TargetOrigin.CleanedMod, (RecordKey?)Key(_originals[0])), (clone.Key, clone.Origin, clone.CloneOf));
        var written = Assert.Single(state.PatchMod.EnumerateMajorRecords<IPlacedObjectGetter>());
        Assert.Equal(added, written.FormKey.ToRecordKey());
        Assert.NotEqual(0, written.MajorRecordFlagsRaw & (int)SkyrimMajorRecord.SkyrimMajorRecordFlag.InitiallyDisabled);
        Assert.Equal(DisabledDepth, written.Placement!.Position.Z);
    }

    /// <summary>The cleaned mod's override of the rock, moved 2000 units east.</summary>
    private static PlacedObject Move(FixtureCell cell, PlacedObject original)
    {
        var moved = cell.PlaceCopyOf(original);
        moved.Placement!.Position = new P3Float(original.Placement!.Position.X + 2000, original.Placement.Position.Y, original.Placement.Position.Z);
        return moved;
    }

    /// <summary>Collects the targets, so the moved rocks are read, and writes a copy of each given rock in the order given.</summary>
    private ImmutableArray<RecordKey> WriteClones(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, int workers, params PlacedObject[] originals)
    {
        using var world = FixturePatcherState.CreateFinalWorld(state);
        var reader = new PluginRecordsFactory(world, workers).Open();
        PlacedObjects.Collect(reader, Trust(), Plan);
        return Writing(state).Write(reader.ReadWritableRecords(), [.. originals.Select(original => new AddClone(Key(original), MarkerOf(original)))]);
    }

    private static PatchWriting Writing(IPatcherState<ISkyrimMod, ISkyrimModGetter> state) => new(new SynthesisWriteCache(state.LinkCache), state.PatchMod, Program.PatchMarkerText);

    private IPatcherState<ISkyrimMod, ISkyrimModGetter> CreateState() =>
        FixturePatcherState.Create([_baseGame.Mod, _cleaned.Mod], Path.GetTempPath(), Path.GetTempPath());

    private string MarkerOf(PlacedObject original) => CloneMarker.Of(Key(original), _cleaned.Key.ToPluginName());

    private ModTrustTable Trust() =>
        TestModTrust.Of(
            _cleaned.Key.ToPluginName(),
            FixturePatcherState.PatchModKey.ToPluginName(),
            trusted: [_baseGame.Key.ToPluginName()],
            untrusted: [_cleaned.Key.ToPluginName()]);

    private static RecordKey Key(PlacedObject record) => record.FormKey.ToRecordKey();

    private static Vector3 PositionOf(IPlacedObjectGetter placed) => new(placed.Placement!.Position.X, placed.Placement.Position.Y, placed.Placement.Position.Z);

    /// <summary>Hands out each EditorID's FormKey from the given store, adding the plugin's next one for a new EditorID, as a persistent allocator does.</summary>
    private sealed class KeptByEditorId(IMod mod, Dictionary<string, FormKey> persisted) : IFormKeyAllocator
    {
        public FormKey GetNextFormKey() => new(mod.ModKey, mod.NextFormID++);

        public FormKey GetNextFormKey(string? editorID)
        {
            if (editorID is null) return GetNextFormKey();
            if (!persisted.TryGetValue(editorID, out var key)) persisted[editorID] = key = GetNextFormKey();
            return key;
        }
    }
}
