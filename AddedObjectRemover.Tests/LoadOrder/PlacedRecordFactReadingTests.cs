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
    private readonly FormKey _marker;

    public PlacedRecordFactReadingTests()
    {
        _target = new FixtureMod("Target.esp", _base);
        _marker = _base.AddMarkerStatic("BaseMarker");
        _cell = new Cell(_target.NextKey(), FixtureMod.Release) { Grid = new CellGrid { Point = new P2Int(0, 0) } };
        var worldspace = new Worldspace(_target.NextKey(), FixtureMod.Release);
        worldspace.AddCell(_cell);
        _target.Mod.Worldspaces.Add(worldspace);
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

    private FormKey AddPlaced(Action<PlacedObject> setUp, bool persistentList)
    {
        var placed = new PlacedObject(_target.NextKey(), FixtureMod.Release) { Placement = new Placement() };
        placed.Base.SetTo(_marker);
        setUp(placed);
        (persistentList ? _cell.Persistent : _cell.Temporary).Add(placed);
        return placed.FormKey;
    }

    private Dictionary<FormKey, PlacedRecordFact> ReadFacts()
    {
        var state = FixturePatcherState.Create([_base.Mod, _target.Mod], Path.GetTempPath(), Path.GetTempPath());
        var plugins = LoadOrderPluginsReader.Read(state.LoadOrder, state.PatchMod.ModKey);
        var plugin = new PluginRecordsFactory(state.LoadOrder, state.LinkCache, state.PatchMod, plugins.Mods).Open();
        var facts = plugin.ReadPlacedRecords(new PlacedReadScope(_target.Key.ToPluginName(), Terrain: false, Navmeshes: false, TerrainVersions: false, TerrainLayers: false));
        return facts.Records.ToDictionary(fact => fact.Key.ToFormKey());
    }
}
