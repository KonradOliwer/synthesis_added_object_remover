using AddedObjectRemover.Tests.EndToEnd;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.LoadOrder;

/// <summary>The facts the reader takes from a placed record itself.</summary>
public class PlacedRecordFactReadingTests
{
    private readonly FixtureMod _base = new("Base.esm");
    private readonly FixtureMod _target;
    private readonly Cell _cell;
    private readonly Worldspace _worldspace;
    private readonly FormKey _marker;

    public PlacedRecordFactReadingTests()
    {
        _target = new FixtureMod("Target.esp", _base);
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
        var later = new FixtureMod("Later.esp", _base, _target);
        var laterCell = new Cell(_cell.FormKey, FixtureMod.Release) { Grid = _cell.Grid!.DeepCopy() };
        laterCell.Location.SetTo(AddLocation("Place B", AddLocation("Hold B", tamriel)).FormKey);
        var laterWorldspace = new Worldspace(_worldspace.FormKey, FixtureMod.Release);
        laterWorldspace.AddCell(laterCell);
        later.Mod.Worldspaces.Add(laterWorldspace);
        AddPlaced(_ => { }, persistentList: false);

        var area = Read(workers: 1, later).CellAreas[_cell.FormKey.ToRecordKey()];

        Assert.Equal(new LocationNames("Hold B", "Place B"), area.Location);
    }

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

    private FormKey AddPlaced(Action<PlacedObject> setUp, bool persistentList, Cell? cell = null)
    {
        var placed = new PlacedObject(_target.NextKey(), FixtureMod.Release) { Placement = new Placement() };
        placed.Base.SetTo(_marker);
        setUp(placed);
        var owner = cell ?? _cell;
        (persistentList ? owner.Persistent : owner.Temporary).Add(placed);
        return placed.FormKey;
    }

    private Dictionary<FormKey, PlacedRecordFact> ReadFacts() => Read(Environment.ProcessorCount).Records.ToDictionary(fact => fact.Key.ToFormKey());

    private PlacedRecordFacts Read(int workers, params FixtureMod[] laterMods)
    {
        var state = FixturePatcherState.Create([_base.Mod, _target.Mod, .. laterMods.Select(mod => mod.Mod)], Path.GetTempPath(), Path.GetTempPath());
        var plugins = LoadOrderPluginsReader.Read(state.LoadOrder, state.PatchMod.ModKey);
        var plugin = new PluginRecordsFactory(state.LoadOrder, state.LinkCache, state.PatchMod, plugins.Mods, workers).Open();
        return plugin.ReadPlacedRecords(new PlacedReadScope(_target.Key.ToPluginName(), Terrain: false, Navmeshes: false, TerrainVersions: false, TerrainLayers: false, TestRecordKeys.HoldKeyword));
    }
}
