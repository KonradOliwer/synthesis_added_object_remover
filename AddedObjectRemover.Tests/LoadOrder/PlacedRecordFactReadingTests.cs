using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.EndToEnd;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;

namespace AddedObjectRemover.Tests.LoadOrder;

/// <summary>The facts the reader takes from a placed record itself.</summary>
public class PlacedRecordFactReadingTests
{
    private const string TargetPlugin = "Target.esp";
    private const string MiddlePlugin = "Middle.esp";
    private const string LaterPlugin = "Later.esp";
    private const string LinkerPlugin = "Linker.esp";
    private const string AbovePlugin = "Above.esp";
    private const string BelowPlugin = "Below.esp";
    private const float LaterScale = 2f;

    private static readonly Vector3 Moved = new(10, 20, 30);
    private static readonly Vector3 Turned = new(0, 0, 1);
    private static readonly PresenceFacts Shown = new(Deleted: false, InitiallyDisabled: false, EnableParent: null);

    private readonly FixtureMod _base = new("Base.esm");
    private readonly FixtureMod _target;
    private readonly Cell _cell;
    private readonly Worldspace _worldspace;
    private readonly FormKey _marker;

    public PlacedRecordFactReadingTests()
    {
        _target = new FixtureMod(TargetPlugin, _base);
        _marker = _base.AddMarkerStatic("BaseMarker");
        _cell = new Cell(_target.NextKey(), FixtureMod.Release) { Grid = new CellGrid { Point = new P2Int(0, 0) } };
        _worldspace = new Worldspace(_target.NextKey(), FixtureMod.Release);
        _worldspace.AddCell(_cell);
        _target.Mod.Worldspaces.Add(_worldspace);
    }

    [Fact]
    public void ThePersistentFlagIsRead()
    {
        var persistent = AddPlaced(record => record.MajorRecordFlagsRaw |= (int)PlacedObject.DefaultMajorFlag.Persistent, persistentList: true);
        var temporary = AddPlaced(_ => { }, persistentList: false);

        var facts = ReadFacts();

        Assert.True(facts[persistent].IsPersistent);
        Assert.False(facts[temporary].IsPersistent);
    }

    [Fact]
    public void TheInitiallyDisabledFlagIsRead()
    {
        var disabled = AddPlaced(record => record.MajorRecordFlagsRaw |= (int)SkyrimMajorRecord.SkyrimMajorRecordFlag.InitiallyDisabled, persistentList: false);
        var enabled = AddPlaced(_ => { }, persistentList: false);

        var facts = ReadFacts();

        Assert.True(facts[disabled].Placement.Presence.InitiallyDisabled);
        Assert.False(facts[enabled].Placement.Presence.InitiallyDisabled);
    }

    [Fact]
    public void TheEnableParentAndItsOppositeFlagAreRead()
    {
        var parent = AddPlaced(_ => { }, persistentList: true);
        var opposite = AddPlaced(record => record.EnableParent = EnableParentOf(parent, EnableParent.Flag.SetEnableStateToOppositeOfParent), persistentList: false);
        var sameState = AddPlaced(record => record.EnableParent = EnableParentOf(parent, flags: 0), persistentList: false);
        var withoutParent = AddPlaced(_ => { }, persistentList: false);
        EnableParentFact? oppositeParent = new EnableParentFact(parent.ToRecordKey(), Opposite: true);
        EnableParentFact? sameStateParent = new EnableParentFact(parent.ToRecordKey(), Opposite: false);

        var facts = ReadFacts();

        Assert.Equal(oppositeParent, facts[opposite].Placement.Presence.EnableParent);
        Assert.Equal(sameStateParent, facts[sameState].Placement.Presence.EnableParent);
        Assert.Null(facts[withoutParent].Placement.Presence.EnableParent);
    }

    /// <summary>An enable parent whose reference is empty enables nothing, so an initially disabled record with one is removed.</summary>
    [Fact]
    public void AnEnableParentWithAnEmptyReferenceIsReadAsNone()
    {
        var placed = AddPlaced(
            record =>
            {
                record.MajorRecordFlagsRaw |= (int)SkyrimMajorRecord.SkyrimMajorRecordFlag.InitiallyDisabled;
                record.EnableParent = EnableParentOf(FormKey.Null, flags: 0);
            },
            persistentList: false);

        var presence = ReadFacts()[placed].Placement.Presence;

        Assert.Null(presence.EnableParent);
        Assert.True(RemovedForm.IsRemoved(presence));
    }

    [Fact]
    public void ADeletedWinnerIsKeptAsAFactSoNoLowerVersionStandsInForIt()
    {
        var placed = AddPlaced(_ => { }, persistentList: false);
        var later = new FixtureMod(LaterPlugin, _base, _target);
        AddOverride(later.Mod, placed).IsDeleted = true;

        var records = Read(workers: 1, later).Records;

        var fact = Assert.Single(records, record => record.Key == placed.ToRecordKey());
        Assert.True(fact.Placement.Presence.Deleted);
        Assert.Equal(new PluginName(LaterPlugin), fact.WinningMod);
    }

    [Fact]
    public void ARecordOutsideTheTargetSpacesIsAFactOnlyWhenItLinksIntoTheTarget()
    {
        var linked = AddPlaced(_ => { }, persistentList: false);
        var linker = new FixtureMod(LinkerPlugin, _base, _target);
        var elsewhere = new Cell(linker.NextKey(), FixtureMod.Release) { Flags = Cell.Flag.IsInteriorCell };
        linker.Mod.Cells.AddInteriorCell(elsewhere);
        var linking = AddPlaced(record => record.EnableParent = EnableParentOf(linked, flags: 0), persistentList: false, elsewhere, linker);
        var unlinked = AddPlaced(_ => { }, persistentList: false, elsewhere, linker);

        var facts = Read(workers: 1, linker).Records.ToDictionary(fact => fact.Key.ToFormKey());

        Assert.False(facts[linking].InTargetSpace);
        Assert.False(facts.ContainsKey(unlinked));
    }

    [Fact]
    public void AScriptOnTheRecordNamingABaseGameQuestIsRead()
    {
        var quest = new Quest(_base.NextKey(), FixtureMod.Release) { EditorID = "BaseQuest" };
        _base.Mod.Quests.Add(quest);
        var scripted = AddPlaced(record => record.VirtualMachineAdapter = TestScripts.Naming(_marker, quest.FormKey), persistentList: false);
        var unscripted = AddPlaced(record => record.VirtualMachineAdapter = TestScripts.Naming(_marker), persistentList: false);

        var facts = ReadFacts();

        Assert.Equal(new ScriptNamedRecord(quest.FormKey.ToRecordKey(), "BaseQuest", ScriptNamedKind.Quest), facts[scripted].NamedByScript);
        Assert.Null(facts[unscripted].NamedByScript);
    }

    [Fact]
    public void AnOwnLocationRefTypeIsRead()
    {
        var refType = new LocationReferenceType(_base.NextKey(), FixtureMod.Release);
        _base.Mod.LocationReferenceTypes.Add(refType);
        var typed = AddPlaced(record => record.LocationRefTypes = [new FormLink<ILocationReferenceTypeGetter>(refType.FormKey)], persistentList: true);
        var untyped = AddPlaced(_ => { }, persistentList: true);

        var facts = ReadFacts();

        Assert.True(facts[typed].HasLocationRefType);
        Assert.False(facts[untyped].HasLocationRefType);
    }

    [Fact]
    public void TheCellAreasAreTheSameAtAnyNumberOfWorkers()
    {
        var tamriel = AddLocation("Tamriel", parent: null);
        var holds = Enumerable.Range(0, 3).Select(hold => AddLocation($"Hold {hold}", tamriel)).ToList();
        foreach (var x in Enumerable.Range(1, 40))
        {
            var cell = AddCell(x);
            if (x % 3 != 0) cell.Location.SetTo(AddLocation($"Place {x}", holds[x % holds.Count]).FormKey);
        }
        AddPlaced(_ => { }, persistentList: false);

        var oneWorker = Read(workers: 1).CellAreas;
        var manyWorkers = Read(workers: 8).CellAreas;

        Assert.Equal(41, oneWorker.Count);
        Assert.Equal(oneWorker.OrderBy(cell => cell.Key, RecordKeyOrder.Comparer), manyWorkers.OrderBy(cell => cell.Key, RecordKeyOrder.Comparer));
    }

    [Fact]
    public void TheWorldspaceDisplayNameIsRead()
    {
        _worldspace.Name = "Display World";
        var placed = AddPlaced(_ => { }, persistentList: false);

        Assert.Equal(new SpaceFact(_worldspace.FormKey.ToRecordKey(), EditorId: null, SpaceKind.Worldspace, "Display World"), ReadFacts()[placed].Space);
    }

    [Fact]
    public void TheInteriorDisplayNameIsReadFromItsCell()
    {
        var interior = new Cell(_target.NextKey(), FixtureMod.Release) { EditorID = "InteriorEditorId", Name = "Display Interior", Flags = Cell.Flag.IsInteriorCell };
        _target.Mod.Cells.AddInteriorCell(interior);
        var placed = AddPlaced(_ => { }, persistentList: false, interior);

        Assert.Equal(new SpaceFact(interior.FormKey.ToRecordKey(), "InteriorEditorId", SpaceKind.Interior, "Display Interior"), ReadFacts()[placed].Space);
    }

    [Fact]
    public void ALaterModsCellOverrideDecidesTheAreaName()
    {
        var tamriel = AddLocation("Tamriel", parent: null);
        _cell.Location.SetTo(AddLocation("Place A", AddLocation("Hold A", tamriel)).FormKey);
        var later = new FixtureMod(LaterPlugin, _base, _target);
        var laterCell = new Cell(_cell.FormKey, FixtureMod.Release) { Grid = _cell.Grid!.DeepCopy() };
        laterCell.Location.SetTo(AddLocation("Place B", AddLocation("Hold B", tamriel)).FormKey);
        var laterWorldspace = new Worldspace(_worldspace.FormKey, FixtureMod.Release);
        laterWorldspace.AddCell(laterCell);
        later.Mod.Worldspaces.Add(laterWorldspace);
        AddPlaced(_ => { }, persistentList: false);

        var area = Read(workers: 1, later).CellAreas[_cell.FormKey.ToRecordKey()];

        Assert.Equal(new LocationNames("Hold B", "Place B"), area.Location);
    }

    [Fact]
    public void ThePatchBeatsThePluginsAboveIt()
    {
        var placed = AddPlaced(_ => { }, persistentList: false);
        var above = new FixtureMod(AbovePlugin, _base, _target);
        AddOverride(above.Mod, placed);
        var state = CreateState(above);
        AddOverride(state.PatchMod, placed);
        using var world = FixturePatcherState.CreateFinalWorld(state);

        var facts = ReadWith(new PluginRecordsFactory(world, Environment.ProcessorCount).Open());

        Assert.Equal(FixturePatcherState.PatchModKey.ToPluginName(), facts.Records.Single(fact => fact.Key.ToFormKey() == placed).WinningMod);
    }

    [Fact]
    public void APluginBelowThePatchBeatsThePatch()
    {
        var placed = AddPlaced(_ => { }, persistentList: false);
        var below = new FixtureMod(BelowPlugin, _base, _target);
        AddOverride(below.Mod, placed);
        var state = CreateState();
        AddOverride(state.PatchMod, placed);
        using var world = FixturePatcherState.CreateFinalWorld(state, [below.Mod]);

        var facts = ReadWith(new PluginRecordsFactory(world, Environment.ProcessorCount).Open());

        Assert.Equal(below.Key.ToPluginName(), facts.Records.Single(fact => fact.Key.ToFormKey() == placed).WinningMod);
    }

    /// <summary>
    /// A link cache indexes a record type when it is first asked for it. The first reader asked for the worldspaces before the
    /// patch held a copy, so only a reader opened after that, with a cache of its own, resolves the copy.
    /// </summary>
    [Fact]
    public void AReaderOpenedAfterThePatchGainedAWorldspaceCopyResolvesThatCopy()
    {
        _worldspace.Name = "Before";
        AddPlaced(_ => { }, persistentList: false);
        var state = CreateState();
        using var world = FixturePatcherState.CreateFinalWorld(state);
        var records = new PluginRecordsFactory(world, Environment.ProcessorCount);
        var first = records.Open();
        Assert.Equal("Before", Assert.Single(ReadWith(first).Records).Space.Name);
        state.PatchMod.Worldspaces.Add(new Worldspace(_worldspace.FormKey, FixtureMod.Release) { Name = "After" });

        var second = records.Open();

        Assert.Equal("After", Assert.Single(ReadWith(second).Records).Space.Name);
        Assert.Equal("Before", Assert.Single(ReadWith(first).Records).Space.Name);
    }

    [Theory]
    [InlineData(TargetPlugin, new[] { TargetPlugin, MiddlePlugin, LaterPlugin })]
    [InlineData(MiddlePlugin, new[] { MiddlePlugin, LaterPlugin })]
    [InlineData(LaterPlugin, new[] { LaterPlugin })]
    public void VersionsAreKeptFromTheWinnerDownToTheFirstVersionWhosePluginStopsTheWalk(string stoppingPlugin, string[] expected)
    {
        var (placed, middle, later) = AddRecordWithTwoOverrides();

        var versions = ReadVersions(placed, plugin => plugin == new PluginName(stoppingPlugin), middle, later);

        Assert.Equal(expected.Select(name => new PluginName(name)), versions.Select(version => version.Plugin));
    }

    [Fact]
    public void WithoutAStoppingPluginEveryVersionIsKept()
    {
        var (placed, middle, later) = AddRecordWithTwoOverrides();

        var versions = ReadVersions(placed, TestVersionWalks.NoStop, middle, later);

        Assert.Equal(
            [new PluginName(TargetPlugin), new PluginName(MiddlePlugin), new PluginName(LaterPlugin)],
            versions.Select(version => version.Plugin));
    }

    [Fact]
    public void EachVersionHoldsItsPluginsPlacementScaleBaseAndPresence()
    {
        var placed = AddPlaced(_ => { }, persistentList: false);
        var otherBase = _base.AddMarkerStatic("OtherMarker");
        var later = new FixtureMod(LaterPlugin, _base, _target);
        var laterVersion = AddOverride(later.Mod, placed);
        laterVersion.Placement = new Placement { Position = ToP3Float(Moved), Rotation = ToP3Float(Turned) };
        laterVersion.Scale = LaterScale;
        laterVersion.Base.SetTo(otherBase);
        laterVersion.MajorRecordFlagsRaw |= (int)SkyrimMajorRecord.SkyrimMajorRecordFlag.InitiallyDisabled;
        List<PlacedVersionFact> expected =
        [
            new(new PluginName(TargetPlugin), new PlacementFacts(Shown, Vector3.Zero, Vector3.Zero), Scale: null, PlaceableBase(_marker)),
            new(new PluginName(LaterPlugin), new PlacementFacts(Shown with { InitiallyDisabled = true }, Moved, Turned), LaterScale, PlaceableBase(otherBase)),
        ];

        var versions = ReadVersions(placed, TestVersionWalks.NoStop, later);

        Assert.Equal(expected, versions.ToList());
    }

    /// <summary>The patch's version is kept like any other, and the walk goes on below it into the plugins after the patch.</summary>
    [Fact]
    public void ThePatchVersionAndAPluginBelowThePatchAreVersions()
    {
        var (placed, createdWorld) = CreateWorldWithOverridesAroundThePatch();
        using var world = createdWorld;

        var facts = ReadWith(new PluginRecordsFactory(world, Environment.ProcessorCount).Open(), plugin => plugin == new PluginName(AbovePlugin));

        Assert.Equal(
            [new PluginName(AbovePlugin), FixturePatcherState.PatchModKey.ToPluginName(), new PluginName(BelowPlugin)],
            VersionsOf(facts, placed).Select(version => version.Plugin));
    }

    /// <summary>The collector's stop rule looks every visited plugin up in the trust table and throws for one it lacks.</summary>
    [Fact]
    public void TheTrustTableHoldsEveryPluginOfTheFinalWorldAlsoThoseBelowThePatch()
    {
        using var world = CreateWorldWithOverridesAroundThePatch().World;
        var table = ModTrustClassifier.Classify(
            world.Plugins,
            new ModTrustSettings(_target.Key.ToPluginName(), [], [], [], TrustRequiringMods: false, MaxOtherMastersOfRequiringMod: 0, TrustLoadingBefore: false));
        var plugins = world.Order.ListedOrder.Select(listing => listing.ModKey.ToPluginName()).Where(plugin => !table.IsPatch(plugin));

        var collected = PlacedObjectCollector.Collect(
            new PluginRecordsFactory(world, Environment.ProcessorCount).Open(),
            table,
            new WhatToCollect(SupportOnlyObjects: false, Terrain: false, Navmesh: false, OtherModDetails: false, TerrainVersions: false, TerrainLayers: false, Water: false));

        Assert.All(plugins, plugin => Assert.True(table.Plugins.ContainsKey(plugin)));
        Assert.Contains(new PluginName(BelowPlugin), table.Plugins.Keys);
        Assert.Equal(1, collected.Counts.TargetsOverriddenLater);
    }

    [Fact]
    public void ADeletedWinnerKeepsTheVersionsBelowIt()
    {
        var placed = AddPlaced(_ => { }, persistentList: false);
        var later = new FixtureMod(LaterPlugin, _base, _target);
        AddOverride(later.Mod, placed).IsDeleted = true;

        var versions = ReadVersions(placed, TestVersionWalks.NoStop, later);

        Assert.Equal([new PluginName(TargetPlugin), new PluginName(LaterPlugin)], versions.Select(version => version.Plugin));
        Assert.Equal([false, true], versions.Select(version => version.Placement.Presence.Deleted));
    }

    /// <summary>The persistent list is walked before the temporary one, so the persistent copy is the plugin's version, as for a winner.</summary>
    [Fact]
    public void APluginHoldingTheRecordTwiceGivesOneVersionTheFirstFound()
    {
        var placed = AddPlaced(_ => { }, persistentList: false);
        var later = new FixtureMod(LaterPlugin, _base, _target);
        var laterCell = AddCellCopy(later.Mod);
        var firstFound = CopyOf(placed);
        firstFound.Placement = new Placement { Position = ToP3Float(Moved) };
        laterCell.Persistent.Add(firstFound);
        laterCell.Temporary.Add(CopyOf(placed));

        var versions = ReadVersions(placed, TestVersionWalks.NoStop, later);

        Assert.Equal([new PluginName(TargetPlugin), new PluginName(LaterPlugin)], versions.Select(version => version.Plugin));
        Assert.Equal(Moved, versions[^1].Placement.Position);
    }

    /// <summary>A record of the target plugin that a plugin above the patch, the patch and then a plugin below the patch override.</summary>
    private (FormKey Record, FinalWorld World) CreateWorldWithOverridesAroundThePatch()
    {
        var placed = AddPlaced(_ => { }, persistentList: false);
        var above = new FixtureMod(AbovePlugin, _base, _target);
        AddOverride(above.Mod, placed);
        var state = CreateState(above);
        AddOverride(state.PatchMod, placed);
        var below = new FixtureMod(BelowPlugin, _base, _target);
        AddOverride(below.Mod, placed);
        return (placed, FixturePatcherState.CreateFinalWorld(state, [below.Mod]));
    }

    /// <summary>A record of the target plugin that the middle and then the later plugin override.</summary>
    private (FormKey Record, FixtureMod Middle, FixtureMod Later) AddRecordWithTwoOverrides()
    {
        var placed = AddPlaced(_ => { }, persistentList: false);
        var middle = new FixtureMod(MiddlePlugin, _base, _target);
        AddOverride(middle.Mod, placed);
        var later = new FixtureMod(LaterPlugin, _base, _target);
        AddOverride(later.Mod, placed);
        return (placed, middle, later);
    }

    private ImmutableArray<PlacedVersionFact> ReadVersions(FormKey record, Func<PluginName, bool> stopsTheWalk, params FixtureMod[] laterMods)
    {
        var state = CreateState(laterMods);
        using var world = FixturePatcherState.CreateFinalWorld(state);
        return VersionsOf(ReadWith(new PluginRecordsFactory(world, Environment.ProcessorCount).Open(), stopsTheWalk), record);
    }

    private static ImmutableArray<PlacedVersionFact> VersionsOf(PlacedRecordFacts facts, FormKey record) =>
        facts.Records.Single(fact => fact.Key.ToFormKey() == record).Versions;

    private static BaseKey PlaceableBase(FormKey record) => new(record.ToRecordKey(), BaseLinkKind.PlaceableObject);

    private static P3Float ToP3Float(Vector3 value) => new(value.X, value.Y, value.Z);

    private Location AddLocation(string name, Location? parent)
    {
        var location = new Location(_base.NextKey(), FixtureMod.Release) { Name = name };
        if (parent is not null) location.ParentLocation.SetTo(parent.FormKey);
        _base.Mod.Locations.Add(location);
        return location;
    }

    private Cell AddCell(int x)
    {
        var cell = new Cell(_target.NextKey(), FixtureMod.Release) { Grid = new CellGrid { Point = new P2Int(x, 0) } };
        _worldspace.AddCell(cell);
        return cell;
    }

    /// <param name="plugin">The plugin that creates the record; the target plugin when none is given.</param>
    private FormKey AddPlaced(Action<PlacedObject> setUp, bool persistentList, Cell? cell = null, FixtureMod? plugin = null)
    {
        var placed = new PlacedObject((plugin ?? _target).NextKey(), FixtureMod.Release) { Placement = new Placement() };
        placed.Base.SetTo(_marker);
        setUp(placed);
        var owner = cell ?? _cell;
        (persistentList ? owner.Persistent : owner.Temporary).Add(placed);
        return placed.FormKey;
    }

    private Dictionary<FormKey, PlacedRecordFact> ReadFacts() => Read(Environment.ProcessorCount).Records.ToDictionary(fact => fact.Key.ToFormKey());

    private PlacedRecordFacts Read(int workers, params FixtureMod[] laterMods)
    {
        var state = CreateState(laterMods);
        using var world = FixturePatcherState.CreateFinalWorld(state);
        return ReadWith(new PluginRecordsFactory(world, workers).Open());
    }

    private PlacedRecordFacts ReadWith(IPluginRecords plugin) => ReadWith(plugin, TestVersionWalks.NoStop);

    private PlacedRecordFacts ReadWith(IPluginRecords plugin, Func<PluginName, bool> stopsTheWalk) =>
        plugin.ReadPlacedRecords(
            new PlacedReadScope(_target.Key.ToPluginName(), Terrain: false, Navmeshes: false, TerrainVersions: false, TerrainLayers: false, Water: false, TestRecordKeys.HoldKeyword),
            stopsTheWalk);

    /// <param name="abovePatch">Plugins loading after the target plugin and above the patch.</param>
    private IPatcherState<ISkyrimMod, ISkyrimModGetter> CreateState(params FixtureMod[] abovePatch) =>
        FixturePatcherState.Create([_base.Mod, _target.Mod, .. abovePatch.Select(mod => mod.Mod)], Path.GetTempPath(), Path.GetTempPath());

    /// <summary>The plugin's copy of the target's worldspace and cell, holding its version of the record.</summary>
    /// <returns>The plugin's version of the record.</returns>
    private PlacedObject AddOverride(ISkyrimMod mod, FormKey record)
    {
        var version = CopyOf(record);
        AddCellCopy(mod).Temporary.Add(version);
        return version;
    }

    /// <summary>The plugin's copy of the target's worldspace and cell, without records.</summary>
    private Cell AddCellCopy(ISkyrimMod mod)
    {
        var cell = new Cell(_cell.FormKey, FixtureMod.Release) { Grid = _cell.Grid!.DeepCopy() };
        var worldspace = new Worldspace(_worldspace.FormKey, FixtureMod.Release);
        worldspace.AddCell(cell);
        mod.Worldspaces.Add(worldspace);
        return cell;
    }

    /// <summary>A copy of the target's version of a record from the target cell's temporary list.</summary>
    private PlacedObject CopyOf(FormKey record) => (PlacedObject)_cell.Temporary.Single(placed => placed.FormKey == record).DeepCopy();

    private static EnableParent EnableParentOf(FormKey parent, EnableParent.Flag flags)
    {
        var enableParent = new EnableParent { Flags = flags };
        enableParent.Reference.SetTo(parent);
        return enableParent;
    }
}
