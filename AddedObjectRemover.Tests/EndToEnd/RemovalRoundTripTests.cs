using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// What the patch writing writes for a removal, read back from the patch, is a removed record by the collector's rule, with and
/// without an enable parent on the removed record. In one interior, three crates of the cleaned mod each link to a pad of a
/// second mod; one crate has a base game lever as enable parent. The second mod's run sees the crates as other mods' objects.
/// A move written into the patch is read the same way: each run sees the crate where the patch put it.
/// </summary>
public sealed class RemovalRoundTripTests
{
    private const int Workers = 1;
    private const float Spacing = 300f;

    private static readonly Vector3 MovedTo = new(Spacing, Spacing, 0);

    private static readonly WhatToCollect Plan = new(Navmesh: false, OtherModDetails: false, TerrainVersions: false, TerrainLayers: false, Water: false, MovedDistance: SettingDefaults.MovedDistanceInUnits);

    private readonly FixtureMod _baseGame = new("Skyrim.esm");
    private readonly FixtureMod _second;
    private readonly FixtureMod _cleaned;
    private readonly PlacedObject _plain;
    private readonly PlacedObject _withEnableParent;
    private readonly PlacedObject _kept;

    public RemovalRoundTripTests()
    {
        _second = new FixtureMod("Second.esp", _baseGame);
        _cleaned = new FixtureMod("Cleaned.esp", _baseGame, _second);
        var spaces = FixtureSpaces.CreateIn(_baseGame);
        spaces.AddOverridesTo(_second);
        spaces.AddOverridesTo(_cleaned);

        var lever = _baseGame.Interior.Place("Lever", _baseGame.AddModeledStatic("LeverBase", FixtureMeshes.PadModel), new Vector3(0, -Spacing, 0));
        var pad = _second.Interior.Place("Pad", _second.AddModeledStatic("PadBase", FixtureMeshes.PadModel), Vector3.Zero);
        var crateBase = _cleaned.AddModeledStatic("CrateBase", FixtureMeshes.CrateModel);
        _plain = PlaceCrateLinkedTo(pad, "Plain", crateBase, row: 1);
        _withEnableParent = PlaceCrateLinkedTo(pad, "WithEnableParent", crateBase, row: 2);
        _withEnableParent.EnableParent = FixtureScenery.EnableParentOf(lever);
        _kept = PlaceCrateLinkedTo(pad, "Kept", crateBase, row: 3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AWrittenRemovalReadsBackFromThePatchAsRemoved(bool withEnableParent)
    {
        var crate = withEnableParent ? _withEnableParent : _plain;
        EnableParentFact? writtenParent = withEnableParent ? new EnableParentFact(RemovalConvention.PlayerRef, RemovalConvention.OppositeOfParent) : null;
        var state = CreateState();
        using var world = FixturePatcherState.CreateFinalWorld(state);
        var records = new PluginRecordsFactory(world, Workers);

        WriteRemovals(state, records, [crate]);
        var facts = records.Open().ReadPlacedRecords(ScopeOf(_cleaned), TestVersionWalks.NoStop).Records;

        var readBack = Assert.Single(facts, fact => fact.Key == Key(crate));
        Assert.Equal(state.PatchMod.ModKey.ToPluginName(), readBack.WinningMod);
        Assert.True(RemovedForm.IsRemoved(readBack.Placement.Presence));
        Assert.Equal(writtenParent, readBack.Placement.Presence.EnableParent);
        Assert.False(RemovedForm.IsRemoved(Assert.Single(facts, fact => fact.Key == Key(_kept)).Placement.Presence));
    }

    [Fact]
    public void ThePatchsRemovalsAreNoObjectsAndKeepNothingInTheSecondModsRun()
    {
        var state = CreateState();
        using var world = FixturePatcherState.CreateFinalWorld(state);
        var records = new PluginRecordsFactory(world, Workers);
        var removed = new HashSet<RecordKey> { Key(_plain), Key(_withEnableParent) };

        WriteRemovals(state, records, [_plain, _withEnableParent]);
        var collected = PlacedObjectCollector.Collect(records.Open(), ModsOf(_second), Plan);

        Assert.Equal([Key(_kept)], collected.OtherModObjects.Select(other => other.Key));
        Assert.DoesNotContain(collected.SupportOnlyObjects, support => removed.Contains(support.Key));
        Assert.Contains(collected.Links, link => link.Source == Key(_kept));
        Assert.DoesNotContain(collected.Links, link => removed.Contains(link.Source));
    }

    [Fact]
    public void ACrateThePatchMovedIsAnotherModsObjectAtItsNewPlaceInTheSecondModsRun()
    {
        var state = CreateState();
        using var world = FixturePatcherState.CreateFinalWorld(state);
        var records = new PluginRecordsFactory(world, Workers);

        WriteMove(state, records, _kept, MovedTo);
        var collected = PlacedObjectCollector.Collect(records.Open(), ModsOf(_second), Plan);

        var moved = Assert.Single(collected.OtherModObjects, other => other.Key == Key(_kept));
        Assert.Equal(MovedTo, moved.Position);
        Assert.Equal(state.PatchMod.ModKey.ToPluginName(), moved.WinningMod);
    }

    [Fact]
    public void ACrateOfTheCleanedModThePatchMovedIsJudgedAtItsNewPlace()
    {
        var state = CreateState();
        using var world = FixturePatcherState.CreateFinalWorld(state);
        var records = new PluginRecordsFactory(world, Workers);

        WriteMove(state, records, _kept, MovedTo);
        var collected = PlacedObjectCollector.Collect(records.Open(), ModsOf(_cleaned), Plan);

        Assert.Equal(MovedTo, Assert.Single(collected.Targets, target => target.Key == Key(_kept)).Transform.Position);
        Assert.Equal(0, collected.Counts.TargetsOverriddenLater);
    }

    [Fact]
    public void ACrateOfTheCleanedModThePatchRemovedCountsAsAlreadyRemovedByEarlierPatchers()
    {
        var state = CreateState();
        using var world = FixturePatcherState.CreateFinalWorld(state);
        var records = new PluginRecordsFactory(world, Workers);

        WriteRemovals(state, records, [_withEnableParent]);
        var collected = PlacedObjectCollector.Collect(records.Open(), ModsOf(_cleaned), Plan);

        Assert.Equal([Key(_plain), Key(_kept)], collected.Targets.Select(target => target.Key));
        Assert.Equal(new CleanedModCounts(Objects: 3, RemovedByEarlierPatchers: 1, RemovedByOtherPlugins: 0), CountsOf(collected));
    }

    private sealed record CleanedModCounts(int Objects, int RemovedByEarlierPatchers, int RemovedByOtherPlugins);

    private static CleanedModCounts CountsOf(CollectedObjects collected) =>
        new(collected.Counts.CleanedModObjects, collected.Counts.RemovedByEarlierPatchers, collected.Counts.RemovedByOtherPlugins);

    private IPatcherState<ISkyrimMod, ISkyrimModGetter> CreateState() =>
        FixturePatcherState.Create([_baseGame.Mod, _second.Mod, _cleaned.Mod], Path.GetTempPath(), Path.GetTempPath());

    /// <summary>Collects the cleaned mod's crates as targets and writes the removal of the given ones into the patch.</summary>
    private void WriteRemovals(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, PluginRecordsFactory records, IReadOnlyList<PlacedObject> crates)
    {
        var keys = crates.Select(Key).ToHashSet();
        var reader = records.Open();
        var targets = PlacedObjectCollector.Collect(reader, ModsOf(_cleaned), Plan).Targets;
        var orders = targets
            .Where(target => keys.Contains(target.Key))
            .SelectMany(RemovalWrite.OrdersFor)
            .ToList();
        Assert.Equal(keys.Count, orders.OfType<SetInitiallyDisabled>().Count());

        Write(state, reader, orders);
    }

    /// <summary>Writes the crate's move into the patch, as a kept marker's move is written.</summary>
    private void WriteMove(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, PluginRecordsFactory records, PlacedObject crate, Vector3 to)
    {
        var reader = records.Open();
        reader.ReadPlacedRecords(ScopeOf(_cleaned), TestVersionWalks.NoStop);
        Write(state, reader, [new SetPosition(Key(crate), to)]);
    }

    private static void Write(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, IPluginRecords reader, IReadOnlyList<WriteOrder> orders) =>
        new PatchWriting(new SynthesisWriteCache(state.LinkCache), state.PatchMod).Write(reader.ReadWritableRecords(), orders);

    private PlacedObject PlaceCrateLinkedTo(PlacedObject pad, string name, FormKey crateBase, int row)
    {
        var crate = _cleaned.Interior.Place(name, crateBase, new Vector3(0, row * Spacing, 0));
        var link = new LinkedReferences();
        link.Reference.SetTo(pad.FormKey);
        crate.LinkedReferences.Add(link);
        return crate;
    }

    private ModTrustTable ModsOf(FixtureMod cleaned) =>
        TestModTrust.Of(
            cleaned.Key.ToPluginName(),
            FixturePatcherState.PatchModKey.ToPluginName(),
            trusted: [_baseGame.Key.ToPluginName()],
            untrusted: [_second.Key.ToPluginName(), _cleaned.Key.ToPluginName()]);

    private static PlacedReadScope ScopeOf(FixtureMod cleaned) =>
        new(cleaned.Key.ToPluginName(), Terrain: false, Navmeshes: false, TerrainVersions: false, TerrainLayers: false, Water: false, TestRecordKeys.HoldKeyword);

    private static RecordKey Key(PlacedObject record) => record.FormKey.ToRecordKey();
}
