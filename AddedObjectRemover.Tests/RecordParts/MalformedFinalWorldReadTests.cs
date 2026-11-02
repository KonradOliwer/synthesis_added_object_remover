using System.Collections.Immutable;
using AddedObjectRemover.Tests.EndToEnd;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.RecordParts;

/// <summary>
/// The final navmesh world reads a navmesh info map entry (its parent included) and a door's navmesh link from winning records no
/// earlier step may have parsed, so what the game library raises for malformed bytes there fails as <see cref="MalformedRecordException"/>,
/// which the restore step's failure boundary stops on.
/// </summary>
public sealed class MalformedFinalWorldReadTests
{
    private const string Malformed = "The bytes cannot be parsed.";

    private static readonly ModKey Plugin = ModKey.FromNameAndExtension("Broken.esp");
    private static readonly FormKey NavmeshKey = new(Plugin, 0x800);
    private static readonly FormKey DoorKey = new(Plugin, 0x801);

    [Fact]
    public void ReadingAMapEntryWhoseParentCannotBeParsedFailsAsMalformedUserData()
    {
        var world = WorldWithMapEntryParent(() => throw new MalformedDataException(Malformed));

        var failure = Assert.Throws<MalformedRecordException>(() => world.EntryOf(NavmeshKey.ToRecordKey()));

        Assert.True(Failures.IsRecoverable(failure));
    }

    [Fact]
    public void ReadingAMapEntryWhoseWorldspaceParentCannotBeParsedFailsAsMalformedUserData()
    {
        var world = WorldWithMapEntryParent(() => GetterProxy.Create<INavigationMapInfoWorldParentGetter>(new Dictionary<string, Func<object?>>
        {
            ["ParentWorldspace"] = () => throw new MalformedDataException(Malformed),
        }));

        var failure = Assert.Throws<MalformedRecordException>(() => world.EntryOf(NavmeshKey.ToRecordKey()));

        Assert.True(Failures.IsRecoverable(failure));
    }

    [Fact]
    public void ReadingAMapEntryWhoseCellParentCannotBeParsedFailsAsMalformedUserData()
    {
        var world = WorldWithMapEntryParent(() => GetterProxy.Create<INavigationMapInfoCellParentGetter>(new Dictionary<string, Func<object?>>
        {
            ["ParentCell"] = () => throw new MalformedDataException(Malformed),
        }));

        var failure = Assert.Throws<MalformedRecordException>(() => world.EntryOf(NavmeshKey.ToRecordKey()));

        Assert.True(Failures.IsRecoverable(failure));
    }

    [Fact]
    public void ReadingAMalformedDoorLinkFailsAsMalformedUserData()
    {
        var door = GetterProxy.Create<IPlacedObjectGetter>(new Dictionary<string, Func<object?>>
        {
            ["IsDeleted"] = () => false,
            ["NavigationDoorLink"] = () => throw new MalformedDataException(Malformed),
        });
        var cell = new Cell(new FormKey(Plugin, 0x802), FixtureMod.Release);
        var world = WorldOver(maps: [], doors: new Dictionary<RecordKey, PlacedRecordHandle> { [DoorKey.ToRecordKey()] = new(door, new PlacedRecordLocation(cell, InPersistentList: false)) });

        var failure = Assert.Throws<MalformedRecordException>(() => world.DoorOf(DoorKey.ToRecordKey()));

        Assert.True(Failures.IsRecoverable(failure));
    }

    private static FinalNavmeshWorld WorldWithMapEntryParent(Func<object?> parent)
    {
        var entry = GetterProxy.Create<INavigationMapInfoGetter>(new Dictionary<string, Func<object?>>
        {
            ["NavigationMesh"] = () => new FormLink<INavigationMeshGetter>(NavmeshKey),
            ["Parent"] = parent,
            ["MergedTo"] = () => new List<IFormLinkGetter<INavigationMeshGetter>>(),
            ["PreferredMerges"] = () => new List<IFormLinkGetter<INavigationMeshGetter>>(),
            ["LinkedDoors"] = () => new List<ILinkedDoorGetter>(),
        });
        var map = GetterProxy.Create<INavigationMeshInfoMapGetter>(new Dictionary<string, Func<object?>>
        {
            ["MapInfos"] = () => new List<INavigationMapInfoGetter> { entry },
        });
        return WorldOver(maps: [new NavmeshMapVersion(0, Plugin.ToPluginName(), map)], doors: new Dictionary<RecordKey, PlacedRecordHandle>());
    }

    private static FinalNavmeshWorld WorldOver(ImmutableArray<NavmeshMapVersion> maps, IReadOnlyDictionary<RecordKey, PlacedRecordHandle> doors)
    {
        var patch = new SkyrimMod(FixturePatcherState.PatchModKey, FixtureMod.Release);
        var synthesis = new SynthesisWriteCache(new LoadOrder<IModListing<ISkyrimModGetter>>([]).ToMutableLinkCache<ISkyrimMod, ISkyrimModGetter>(patch));
        var navmeshes = new NavmeshRecordHandles(
            new Dictionary<RecordKey, RecordVersions<NavmeshVersion?>>(),
            maps,
            new Dictionary<RecordKey, NavmeshVersion>(),
            new Dictionary<RecordKey, NavDoorLinkFact>());
        var handles = new RecordHandles(
            doors,
            new Dictionary<RecordKey, IPlacedGetter>(),
            new PlacedRecordParts(new PlacedPartTolerances()),
            new AreaRecordHandles(
                new Dictionary<RecordKey, RecordVersions<ICellGetter>>(),
                new Dictionary<RecordKey, RecordVersions<ILandscapeGetter>>(),
                navmeshes,
                new CellRecordParts(0f),
                new LandRecordParts(0f)));
        return new FinalNavmeshWorld(patch, handles, synthesis);
    }
}
