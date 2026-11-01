using System.Collections.Immutable;
using AddedObjectRemover.Steps.WriteThePatch;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Tests.EndToEnd;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.RecordParts;

/// <summary>
/// The output check over the navmesh writes, on in-memory plugins. The base game has two square navmeshes side by side in one
/// worldspace cell, West and East, linked over their shared edge, a door leading from West, and a navmesh info map with their
/// entries and preferred paths through both. The cleaned mod edits, deletes or replaces West; the patch writes it back.
/// </summary>
public sealed class NavmeshOutputCheckTests
{
    private const float Size = 100f;
    private const int WestLinkedTriangle = 0;
    private const int EastLinkedTriangle = 1;
    private const int DoorTriangle = 1;
    private const int EastSide = 1;
    private const int WestSide = 2;
    private const int CellGridX = 6;
    private const int CellGridY = 7;

    private readonly FixtureMod _base = new("Base.esm");
    private readonly FixtureMod _cleaned;
    private readonly FormKey _worldspace;
    private readonly Cell _cell;
    private readonly NavigationMesh _west;
    private readonly NavigationMesh _east;
    private readonly PlacedObject _door;
    private readonly NavigationMeshInfoMap _map;
    private readonly Dictionary<ModKey, Cell> _cellCopies = [];
    private readonly Dictionary<RecordKey, RecordVersions<NavmeshVersion?>> _changed = [];
    private readonly Dictionary<RecordKey, NavmeshVersion> _winners = [];
    private readonly Dictionary<RecordKey, PlacedRecordHandle> _placed = [];
    private readonly Dictionary<RecordKey, IPlacedGetter> _earlierPlaced = [];
    private readonly List<FixtureMod> _later = [];

    public NavmeshOutputCheckTests()
    {
        _cleaned = new FixtureMod("Cleaned.esp", _base);
        _worldspace = _base.NextKey();
        _cell = new Cell(_base.NextKey(), FixtureMod.Release) { Grid = new CellGrid { Point = new P2Int(CellGridX, CellGridY) } };
        _west = Square(_base.NextKey(), 0f);
        _east = Square(_base.NextKey(), Size);
        _door = new PlacedObject(_base.NextKey(), FixtureMod.Release)
        {
            Placement = new Placement { Position = new P3Float(Size / 2, Size / 2, 0) },
            NavigationDoorLink = DoorLinkTo(_west.FormKey),
        };
        _door.Base.SetTo(_base.AddMarkerStatic("Door"));
        Link(_west, WestLinkedTriangle, EastSide, _east.FormKey, EastLinkedTriangle);
        Link(_east, EastLinkedTriangle, WestSide, _west.FormKey, WestLinkedTriangle);
        _west.Data!.DoorTriangles.Add(new DoorTriangle { TriangleBeforeDoor = DoorTriangle, Door = new FormLink<IPlacedObjectGetter>(_door.FormKey) });
        _cell.NavigationMeshes.Add(_west);
        _cell.NavigationMeshes.Add(_east);
        _cell.Temporary.Add(_door);
        var worldspace = new Worldspace(_worldspace, FixtureMod.Release);
        worldspace.AddCell(_cell);
        _base.Mod.Worldspaces.Add(worldspace);
        _map = new NavigationMeshInfoMap(_base.NextKey(), FixtureMod.Release) { PreferredPathing = PreferredPathsThrough(_west.FormKey, _east.FormKey) };
        _map.MapInfos.Add(Entry(_west.FormKey, mergedTo: _east.FormKey, door: _door.FormKey));
        _map.MapInfos.Add(Entry(_east.FormKey, mergedTo: null, door: null));
        _base.Mod.NavigationMeshInfoMaps.Add(_map);
        Win(_west);
        Win(_east);
        _placed[DoorKey] = new PlacedRecordHandle(_door, new PlacedRecordLocation(_cell, InPersistentList: false));
    }

    private static IReadOnlySet<RecordKey> NoDoorsLeftRemoved => new HashSet<RecordKey>();

    private PluginName BaseGame => _base.Key.ToPluginName();

    private PluginName Cleaned => _cleaned.Key.ToPluginName();

    private RecordKey WestKey => _west.FormKey.ToRecordKey();

    private RecordKey EastKey => _east.FormKey.ToRecordKey();

    private RecordKey DoorKey => _door.FormKey.ToRecordKey();

    private RecordKey MapKey => _map.FormKey.ToRecordKey();

    [Fact]
    public void ARestoredNavmeshPassesWithTheNeighbourItLinksTo()
    {
        var edited = CopyIn(_cleaned, _west);
        edited.Data!.Vertices[3] = new P3Float(0, Size, Size / 2);
        edited.Data.Max = new P3Float(Size, Size, Size / 2);
        Change(_west, [_west, edited], [BaseGame, Cleaned]);

        var check = WriteAndCheck(new CopyEarlierNavmeshGeometry(WestKey), Entries((WestKey, BaseGame, null)));

        Assert.Null(Record.Exception(check));
    }

    [Fact]
    public void ANavmeshStoredAsSevenSixInCellGridSixSevenPassesAndIsFoundInThatCell()
    {
        Assert.Equal(new P2Int16(7, 6), StoredParentCoordinates);
        Change(_west, [_west, Deleted(CopyIn(_cleaned, _west))], [BaseGame, Cleaned]);
        var orders = new WriteOrder[] { new CopyEarlierNavmesh(WestKey), Entries((WestKey, BaseGame, null)) };
        var (_, writing, handles) = Write(orders);

        var world = writing.ReadFinalNavmeshes(handles);

        var view = world.NavmeshOf(WestKey)!;
        Assert.Equal(new NavmeshPlace(_worldspace.ToRecordKey(), new CellGridPoint(6, 7)), view.HeldIn);
        Assert.Equal(view.HeldIn, view.Parent);
        Assert.Equal(view.HeldIn, world.EntryOf(WestKey)!.Parent);
        Assert.Null(Record.Exception(() => NavmeshOutputCheck.ThrowOnFault(orders, NoDoorsLeftRemoved, () => world)));
    }

    /// <summary>
    /// The final world lists the links of the patch's versions, not those of the winners they replace: East's restored link and the
    /// door's restored link name West again, and nothing names the sunk replacement, although the winners link to it.
    /// </summary>
    [Fact]
    public void TheFinalWorldsReverseLinksFollowThePatchsVersionsOverTheWinnersOnes()
    {
        var replacementMesh = ReplaceWest();
        var replacement = replacementMesh.FormKey.ToRecordKey();
        var (_, writing, handles) = Write(ReplacementOrders(replacementMesh));

        var world = writing.ReadFinalNavmeshes(handles);

        Assert.Equal(new[] { EastKey }, world.NavmeshesLinkingTo(WestKey));
        Assert.Equal(new[] { WestKey }, world.NavmeshesLinkingTo(EastKey));
        Assert.Empty(world.NavmeshesLinkingTo(replacement));
        Assert.Equal(new[] { DoorKey }, world.DoorsLinkingTo(WestKey));
        Assert.Empty(world.DoorsLinkingTo(replacement));
    }

    /// <summary>
    /// Two final worlds over the same read, the first over a patch that writes nothing: the winners' links read for the first serve
    /// the second, which lists its own patch's links instead of those of the winners it replaces.
    /// </summary>
    [Fact]
    public void FinalWorldsOverOneReadShareTheWinnersLinksAndListTheirOwnPatchsLinks()
    {
        var replacementMesh = ReplaceWest();
        var replacement = replacementMesh.FormKey.ToRecordKey();
        var handles = Handles();
        var (_, untouched, _) = Write([], handles);
        var before = untouched.ReadFinalNavmeshes(handles);
        Assert.Equal(new[] { replacement }, before.NavmeshesLinkingTo(EastKey));
        Assert.Empty(before.NavmeshesLinkingTo(WestKey));
        var (_, writing, _) = Write(ReplacementOrders(replacementMesh), handles);

        var after = writing.ReadFinalNavmeshes(handles);

        Assert.Equal(new[] { WestKey }, after.NavmeshesLinkingTo(EastKey));
        Assert.Equal(new[] { EastKey }, after.NavmeshesLinkingTo(WestKey));
    }

    [Fact]
    public void ANavmeshWhoseWinningCellALaterPluginDeletesIsHeldInADeletedCell()
    {
        DeleteCellInLaterPlugin();
        var (_, writing, handles) = Write([]);

        var world = writing.ReadFinalNavmeshes(handles);

        Assert.Equal(new NavmeshFault(WestKey, NavmeshRule.Identity, NavmeshFaultKind.CellDeleted, null, null), NavmeshConsistency.FirstFault(world, [WestKey], new HashSet<RecordKey>(), NoDoorsLeftRemoved));
    }

    [Fact]
    public void WritingANavmeshIntoACellALaterPluginDeletesThrows()
    {
        Change(_west, [_west, Deleted(CopyIn(_cleaned, _west))], [BaseGame, Cleaned]);
        DeleteCellInLaterPlugin();

        var thrown = Record.Exception(() => Write([new CopyEarlierNavmesh(WestKey)]));

        Assert.Contains("deleted", Assert.IsType<InvalidOperationException>(thrown).Message);
    }

    /// <summary>The cleaned mod disabled the door West's door triangle names, and the door does not come back.</summary>
    [Fact]
    public void ABroughtBackNavmeshNamingADoorTheCleanedModLeavesRemovedThrows()
    {
        Change(_west, [_west, Deleted(CopyIn(_cleaned, _west))], [BaseGame, Cleaned]);
        var orders = new WriteOrder[] { new CopyEarlierNavmesh(WestKey), Entries((WestKey, BaseGame, null)) };
        var (_, writing, handles) = Write(orders);

        var thrown = Record.Exception(() => NavmeshOutputCheck.ThrowOnFault(orders, new HashSet<RecordKey> { DoorKey }, () => writing.ReadFinalNavmeshes(handles)));

        Assert.Contains(nameof(NavmeshFaultKind.DoorDoesNotStand), Assert.IsType<InvalidOperationException>(thrown).Message);
    }

    [Fact]
    public void ABroughtBackNavmeshPassesWithItsNeighbourAndDoorAsTheBaseGameHasThem()
    {
        Change(_west, [_west, Deleted(CopyIn(_cleaned, _west))], [BaseGame, Cleaned]);

        var check = WriteAndCheck(new CopyEarlierNavmesh(WestKey), Entries((WestKey, BaseGame, null)));

        Assert.Null(Record.Exception(check));
    }

    [Fact]
    public void ABroughtBackNavmeshWhoseReplacementIsSunkPassesWithItsNeighbourAndDoorRelinked()
    {
        var replacement = ReplaceWest();

        var check = WriteAndCheck(ReplacementOrders(replacement));

        Assert.Null(Record.Exception(check));
    }

    [Fact]
    public void ASunkNavmeshLeftWithAnEdgeLinkThrowsNamingItAndTheRule()
    {
        var replacement = ReplaceWest();
        var orders = ReplacementOrders(replacement);
        var (patch, writing, handles) = Write(orders);
        var sunk = patch.Worldspaces.SelectMany(worldspace => worldspace.EnumerateMajorRecords<INavigationMesh>()).Single(navmesh => navmesh.FormKey == replacement.FormKey);
        Link(sunk, WestLinkedTriangle, EastSide, _east.FormKey, EastLinkedTriangle);

        var thrown = Assert.Throws<InvalidOperationException>(() => NavmeshOutputCheck.ThrowOnFault(orders, NoDoorsLeftRemoved, () => writing.ReadFinalNavmeshes(handles)));

        Assert.Contains(replacement.FormKey.ToRecordKey().ToString(), thrown.Message);
        Assert.Contains(nameof(NavmeshRule.Links), thrown.Message);
    }

    [Fact]
    public void PreferredPathsThroughANavmeshOutsideTheSpacesReadAreResolvedInTheLoadOrder()
    {
        var far = FarNavmesh();
        _map.PreferredPathing = PreferredPathsThrough(_west.FormKey, _east.FormKey, far.FormKey);
        Change(_west, [_west, Deleted(CopyIn(_cleaned, _west))], [BaseGame, Cleaned]);

        var check = WriteAndCheck(new CopyEarlierNavmesh(WestKey), Entries((WestKey, BaseGame, null)));

        Assert.Null(Record.Exception(check));
    }

    [Fact]
    public void PreferredPathsThroughADeletedNavmeshOutsideTheSpacesReadThrow()
    {
        var far = FarNavmesh();
        far.IsDeleted = true;
        _map.PreferredPathing = PreferredPathsThrough(_west.FormKey, _east.FormKey, far.FormKey);
        Change(_west, [_west, Deleted(CopyIn(_cleaned, _west))], [BaseGame, Cleaned]);

        var check = WriteAndCheck(new CopyEarlierNavmesh(WestKey), Entries((WestKey, BaseGame, null)));

        Assert.Contains(nameof(NavmeshRule.PathData), Assert.IsType<InvalidOperationException>(Record.Exception(check)).Message);
    }

    [Fact]
    public void OrdersWritingNoNavmeshNeverReadTheFinalWorld() =>
        NavmeshOutputCheck.ThrowOnFault([new SetInitiallyDisabled(DoorKey)], NoDoorsLeftRemoved, () => throw new InvalidOperationException("read"));

    /// <summary>
    /// The cleaned mod deletes West and adds a replacement in its place, linked to East and to the door, which it relinks to the
    /// replacement; its map carries the replacement's entry.
    /// </summary>
    private NavigationMesh ReplaceWest()
    {
        var replacement = Square(_cleaned.NextKey(), 0f);
        Link(replacement, WestLinkedTriangle, EastSide, _east.FormKey, EastLinkedTriangle);
        replacement.Data!.DoorTriangles.Add(new DoorTriangle { TriangleBeforeDoor = DoorTriangle, Door = new FormLink<IPlacedObjectGetter>(_door.FormKey) });
        CellCopyIn(_cleaned).NavigationMeshes.Add(replacement);
        var relinkedEast = CopyIn(_cleaned, _east);
        relinkedEast.Data!.EdgeLinks[0].Mesh = new FormLink<INavigationMeshGetter>(replacement.FormKey);
        var relinkedDoor = (PlacedObject)_door.DeepCopy();
        relinkedDoor.NavigationDoorLink = DoorLinkTo(replacement.FormKey);
        CellCopyIn(_cleaned).Temporary.Add(relinkedDoor);
        var cleanedMap = (NavigationMeshInfoMap)_map.DeepCopy();
        cleanedMap.MapInfos.Clear();
        cleanedMap.MapInfos.Add(Entry(replacement.FormKey, mergedTo: _east.FormKey, door: _door.FormKey));
        _cleaned.Mod.NavigationMeshInfoMaps.Add(cleanedMap);

        Change(_west, [_west, Deleted(CopyIn(_cleaned, _west))], [BaseGame, Cleaned]);
        Change(_east, [_east, relinkedEast], [BaseGame, Cleaned]);
        _changed[replacement.FormKey.ToRecordKey()] = new RecordVersions<NavmeshVersion?>([Cleaned, Cleaned], [null, Version(replacement)]);
        Win(replacement);
        _placed[DoorKey] = new PlacedRecordHandle(relinkedDoor, new PlacedRecordLocation(CellCopyIn(_cleaned), InPersistentList: false));
        _earlierPlaced[DoorKey] = _door;
        return replacement;
    }

    private WriteOrder[] ReplacementOrders(NavigationMesh replacement) =>
    [
        new CopyEarlierNavmesh(WestKey),
        new FlattenAndDetachNavmesh(replacement.FormKey.ToRecordKey(), RemovalConvention.SunkNavmeshZ),
        new CopyEarlierNavmeshGeometry(EastKey),
        new CopyEarlierPlacedParts(DoorKey, PlacedParts.NavDoorLink),
        Entries((WestKey, BaseGame, null), (EastKey, BaseGame, null), (replacement.FormKey.ToRecordKey(), Cleaned, RemovalConvention.SunkNavmeshZ)),
    ];

    /// <summary>A navmesh of the base game in another worldspace, which the read of the target spaces does not hold.</summary>
    private NavigationMesh FarNavmesh()
    {
        var far = Square(_base.NextKey(), 0f);
        var cell = new Cell(_base.NextKey(), FixtureMod.Release) { Grid = new CellGrid { Point = new P2Int(5, 5) } };
        cell.NavigationMeshes.Add(far);
        var worldspace = new Worldspace(_base.NextKey(), FixtureMod.Release);
        worldspace.AddCell(cell);
        _base.Mod.Worldspaces.Add(worldspace);
        return far;
    }

    private Action WriteAndCheck(params WriteOrder[] orders)
    {
        var (_, writing, handles) = Write(orders);
        return () => NavmeshOutputCheck.ThrowOnFault(orders, NoDoorsLeftRemoved, () => writing.ReadFinalNavmeshes(handles));
    }

    /// <param name="handles">The read to write from; a new one when null.</param>
    private (ISkyrimMod Patch, PatchWriting Writing, RecordHandles Handles) Write(WriteOrder[] orders, RecordHandles? handles = null)
    {
        var state = FixturePatcherState.Create([_base.Mod, _cleaned.Mod, .. _later.Select(later => later.Mod)], Path.GetTempPath(), Path.GetTempPath());
        var writing = new PatchWriting(new SynthesisWriteCache(state.LinkCache), state.PatchMod);
        handles ??= Handles();
        writing.Write(handles, orders);
        return (state.PatchMod, writing, handles);
    }

    /// <summary>A plugin after the cleaned mod deletes the base game's cell.</summary>
    private void DeleteCellInLaterPlugin()
    {
        var later = new FixtureMod("Later.esp", _base);
        _later.Add(later);
        CellCopyIn(later).IsDeleted = true;
    }

    private RecordHandles Handles() => new(
        _placed,
        _earlierPlaced,
        new PlacedRecordParts(new PlacedPartTolerances()),
        new AreaRecordHandles(
            new Dictionary<RecordKey, RecordVersions<ICellGetter>>(),
            new Dictionary<RecordKey, RecordVersions<ILandscapeGetter>>(),
            new NavmeshRecordHandles(_changed, Maps(), _winners, WinningDoorLinks()),
            new CellRecordParts(0f),
            new LandRecordParts(0f)));

    private ImmutableArray<NavmeshMapVersion> Maps() =>
    [
        new NavmeshMapVersion(0, BaseGame, _map),
        .. _cleaned.Mod.NavigationMeshInfoMaps.Select(map => new NavmeshMapVersion(1, Cleaned, map)),
    ];

    private Dictionary<RecordKey, NavDoorLinkFact> WinningDoorLinks()
    {
        var link = ((IPlacedObjectGetter)_placed[DoorKey].Record).NavigationDoorLink!;
        return new Dictionary<RecordKey, NavDoorLinkFact> { [DoorKey] = new(link.NavMesh.FormKey.ToRecordKey(), link.TeleportMarkerTriangle) };
    }

    private WriteNavmeshMapEntries Entries(params (RecordKey Navmesh, PluginName From, float? SetZ)[] entries) =>
        new(MapKey, [.. entries.Select(entry => new NavmeshEntryOrder(entry.Navmesh, entry.From, entry.SetZ))]);

    private void Change(NavigationMesh navmesh, NavigationMesh[] versions, PluginName[] plugins)
    {
        _changed[navmesh.FormKey.ToRecordKey()] = new RecordVersions<NavmeshVersion?>([.. plugins], [.. versions.Select(Version)]);
        _winners[navmesh.FormKey.ToRecordKey()] = Version(versions[^1]);
    }

    private void Win(NavigationMesh navmesh) => _winners[navmesh.FormKey.ToRecordKey()] = Version(navmesh);

    private NavmeshVersion Version(NavigationMesh navmesh) =>
        new(navmesh, _cell.FormKey.ToRecordKey(), CellPlace);

    private NavmeshPlace CellPlace => new(_worldspace.ToRecordKey(), new CellGridPoint(CellGridX, CellGridY));

    /// <summary>The cell's grid point as the game stores a navmesh parent's coordinates: Y first.</summary>
    private static P2Int16 StoredParentCoordinates => new(CellGridY, CellGridX);

    private static NavigationMesh Deleted(NavigationMesh navmesh)
    {
        navmesh.IsDeleted = true;
        navmesh.Data = null;
        return navmesh;
    }

    private NavigationMesh CopyIn(FixtureMod mod, NavigationMesh navmesh)
    {
        var copy = (NavigationMesh)navmesh.DeepCopy();
        CellCopyIn(mod).NavigationMeshes.Add(copy);
        return copy;
    }

    /// <summary>The plugin's copy of the base game's cell, in its own copy of the worldspace, without children; made on first use.</summary>
    private Cell CellCopyIn(FixtureMod mod)
    {
        if (_cellCopies.TryGetValue(mod.Key, out var copy)) return copy;
        copy = (Cell)_cell.DeepCopy();
        copy.NavigationMeshes.Clear();
        copy.Temporary.Clear();
        var worldspace = new Worldspace(_worldspace, FixtureMod.Release);
        worldspace.AddCell(copy);
        mod.Mod.Worldspaces.Add(worldspace);
        _cellCopies.Add(mod.Key, copy);
        return copy;
    }

    /// <summary>
    /// A square from <paramref name="x"/> to <paramref name="x"/> + <see cref="Size"/> in the base game's cell: triangle 0 runs 0-1-2
    /// (edge 1 is the east side), triangle 1 runs 0-2-3 (edge 2 is the west side); the diagonal joins them.
    /// </summary>
    private NavigationMesh Square(FormKey key, float x)
    {
        var navmesh = new NavigationMesh(key, FixtureMod.Release)
        {
            Data = new NavigationMeshData
            {
                Parent = new WorldspaceNavmeshParent { Parent = new FormLink<IWorldspaceGetter>(_worldspace), Coordinates = StoredParentCoordinates },
                Vertices = { new P3Float(x, 0, 0), new P3Float(x + Size, 0, 0), new P3Float(x + Size, Size, 0), new P3Float(x, Size, 0) },
                Triangles = { Triangle(0, 1, 2, nextTriangle: 1, edge: 2), Triangle(0, 2, 3, nextTriangle: 0, edge: 0) },
                Min = new P3Float(x, 0, 0),
                Max = new P3Float(x + Size, Size, 0),
            },
        };
        return navmesh;
    }

    private static NavmeshTriangle Triangle(short first, short second, short third, short nextTriangle, int edge)
    {
        var triangle = new NavmeshTriangle { Vertices = new P3Int16(first, second, third) };
        foreach (var each in NavmeshTriangleEdges.All) each.SetValue(triangle, NavmeshTriangleEdges.None);
        NavmeshTriangleEdges.All[edge].SetValue(triangle, nextTriangle);
        return triangle;
    }

    private static void Link(INavigationMesh navmesh, int triangle, int edge, FormKey target, int targetTriangle)
    {
        var data = navmesh.Data!;
        var side = NavmeshTriangleEdges.All[edge];
        side.SetValue(data.Triangles[triangle], (short)data.EdgeLinks.Count);
        data.Triangles[triangle].Flags |= side.Flag;
        data.EdgeLinks.Add(new EdgeLink { Mesh = new FormLink<INavigationMeshGetter>(target), TriangleIndex = (short)targetTriangle });
    }

    private NavigationMapInfo Entry(FormKey navmesh, FormKey? mergedTo, FormKey? door)
    {
        var entry = new NavigationMapInfo
        {
            NavigationMesh = new FormLink<INavigationMeshGetter>(navmesh),
            Parent = new NavigationMapInfoWorldParent { ParentWorldspace = new FormLink<IWorldspaceGetter>(_worldspace), ParentWorldspaceCoord = StoredParentCoordinates },
            Point = new P3Float(Size / 2, Size / 2, 0),
        };
        if (mergedTo is { } merged) entry.MergedTo.Add(new FormLink<INavigationMeshGetter>(merged));
        if (door is { } linked) entry.LinkedDoors.Add(new LinkedDoor { Door = new FormLink<IPlacedObjectGetter>(linked) });
        return entry;
    }

    private static NavigationDoorLink DoorLinkTo(FormKey navmesh) =>
        new() { NavMesh = new FormLink<INavigationMeshGetter>(navmesh), TeleportMarkerTriangle = DoorTriangle };

    private static PreferredPathing PreferredPathsThrough(params FormKey[] navmeshes)
    {
        var set = new NavmeshSet();
        set.Navmeshes.AddRange(navmeshes.Select(navmesh => new FormLink<INavigationMeshGetter>(navmesh)));
        return new PreferredPathing { NavmeshSets = { set } };
    }
}
