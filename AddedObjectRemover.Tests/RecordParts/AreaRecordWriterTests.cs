using System.Collections.Immutable;
using AddedObjectRemover.Steps.WriteThePatch.Contracts;
using AddedObjectRemover.Tests.EndToEnd;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.RecordParts;

/// <summary>
/// The LAND, navmesh, navmesh info map and door link writers on in-memory plugins: the orders are written into the patch and read
/// back. The base game has a worldspace cell holding a LAND record, a navmesh and a door linked to it, and a navmesh info map; the
/// cleaned mod and a later plugin override them.
/// </summary>
public sealed class AreaRecordWriterTests
{
    private const int Side = TerrainHeights.VerticesPerSide;
    private const float Size = 100f;
    private const float RaisedHeight = 80f;
    private const short DoorTriangle = 1;
    private const int CellGridX = 6;
    private const int CellGridY = 7;
    private const ushort OlderFormVersion = 43;
    private const ushort NewerFormVersion = 44;

    private static readonly P3UInt8 EarlierNormal = new(1, 2, 127);
    private static readonly P3UInt8 WinningNormal = new(3, 4, 127);
    private static readonly P3UInt8 CleanedColour = new(9, 8, 7);
    private static readonly byte[] RawBytes = [1, 2, 3, 4];

    private readonly FixtureMod _base = new("Base.esm");
    private readonly FixtureMod _cleaned;
    private readonly FixtureMod _later;
    private readonly FormKey _worldspace;
    private readonly Cell _cell;
    private readonly Landscape _land;
    private readonly NavigationMesh _navmesh;
    private readonly PlacedObject _door;
    private readonly NavigationMeshInfoMap _map;
    private readonly Dictionary<ModKey, Worldspace> _worldspaceCopies = [];
    private readonly Dictionary<ModKey, Cell> _cellCopies = [];

    public AreaRecordWriterTests()
    {
        _cleaned = new FixtureMod("Cleaned.esp", _base);
        _later = new FixtureMod("Later.esp", _base);
        _worldspace = _base.NextKey();
        _cell = new Cell(_base.NextKey(), FixtureMod.Release) { Grid = new CellGrid { Point = new P2Int(CellGridX, CellGridY) } };
        _land = FlatLand(_base.NextKey(), EarlierNormal);
        _cell.Landscape = _land;
        _navmesh = Square(_base.NextKey(), _cell.FormKey);
        _cell.NavigationMeshes.Add(_navmesh);
        _door = new PlacedObject(_base.NextKey(), FixtureMod.Release)
        {
            Placement = new Placement { Position = new P3Float(Size / 2, Size / 2, 0) },
            NavigationDoorLink = new NavigationDoorLink { NavMesh = new FormLink<INavigationMeshGetter>(_navmesh.FormKey), TeleportMarkerTriangle = DoorTriangle },
        };
        _door.Base.SetTo(_base.AddMarkerStatic("Door"));
        _cell.Temporary.Add(_door);
        WorldspaceIn(_base.Mod).AddCell(_cell);
        _map = new NavigationMeshInfoMap(_base.NextKey(), FixtureMod.Release)
        {
            FormVersion = NewerFormVersion,
            PreferredPathing = PreferredPathsThrough(_navmesh.FormKey),
            NVSI = new byte[] { 5, 6 },
            NavMeshVersion = 12,
        };
        _map.MapInfos.Add(Entry(_navmesh.FormKey, z: 0f));
        _base.Mod.NavigationMeshInfoMaps.Add(_map);
    }

    private PluginName BaseGame => _base.Key.ToPluginName();

    private PluginName Cleaned => _cleaned.Key.ToPluginName();

    private PluginName Later => _later.Key.ToPluginName();

    private RecordKey LandKey => _land.FormKey.ToRecordKey();

    private RecordKey NavmeshKey => _navmesh.FormKey.ToRecordKey();

    private RecordKey CellKey => _cell.FormKey.ToRecordKey();

    private NavmeshPlace CellPlace => new(_worldspace.ToRecordKey(), new CellGridPoint(CellGridX, CellGridY));

    [Fact]
    public void TheHeightsGoInWithTheEarlierNormalWhereTheGroundAroundIsEarlierANewOneBetweenAndTheWinnersWhereNothingGoesBack()
    {
        var raised = LandCopyIn(_cleaned.Mod);
        raised.VertexHeightMap!.Offset = RaisedHeight / LandHeightEncoder.UnitsPerStep;
        Fill(raised.VertexNormals!, WinningNormal);
        raised.VertexColors![4, 4] = CleanedColour;
        // West of x = 11 the earlier ground, east of it the raised ground, at x = 11 halfway.
        var heights = Heights((x, _) => x < 11 ? 0f : x > 11 ? RaisedHeight : RaisedHeight / 2);

        var written = WrittenLand(Write(LandHandles(raised), new SetLandHeights(LandKey, heights, PointsWhere((x, _) => x <= 11), CellEdgeHeights.Unknown)));

        Assert.Equal(heights, LandHeightReader.Read(written));
        Assert.Equal(
            (EarlierNormal, EarlierNormal, new P3UInt8(StoredNegative(38), 0, 121), WinningNormal, WinningNormal),
            (written.VertexNormals![9, 5], written.VertexNormals[0, 0], written.VertexNormals[11, 5], written.VertexNormals[13, 5], written.VertexNormals[Side - 1, Side - 1]));
        Assert.Equal(CleanedColour, written.VertexColors![4, 4]);
        Assert.Equal(Landscape.Flag.VertexNormalsHeightMap | Landscape.Flag.VertexColors, written.Flags);
    }

    [Fact]
    public void HeightsTooSteepToStoreAreNotWritten()
    {
        var raised = LandCopyIn(_cleaned.Mod);
        var heights = Heights((x, _) => x == 3 ? LandHeightEncoder.UnitsPerStep * 200 : 0f);

        Assert.Throws<InvalidOperationException>(() => Write(LandHandles(raised), new SetLandHeights(LandKey, heights, PointsWhere((_, _) => true), CellEdgeHeights.Unknown)));
    }

    /// <summary>The winner's normal at x = 12 is its own (a later plugin's); x = 11 beside it goes back halfway, so the heights around x = 12 change.</summary>
    [Fact]
    public void ThePointsThatDoNotGoBackKeepTheWinnersNormalsBesideARestoredSlope()
    {
        var raised = LandCopyIn(_cleaned.Mod);
        raised.VertexHeightMap!.Offset = RaisedHeight / LandHeightEncoder.UnitsPerStep;
        Fill(raised.VertexNormals!, WinningNormal);
        var laterNormal = new P3UInt8(5, 6, 127);
        raised.VertexNormals![12, 5] = laterNormal;
        var heights = Heights((x, _) => x < 11 ? 0f : x > 11 ? RaisedHeight : RaisedHeight / 2);

        var written = WrittenLand(Write(LandHandles(raised), new SetLandHeights(LandKey, heights, PointsWhere((x, _) => x <= 11), CellEdgeHeights.Unknown)));

        Assert.Equal((laterNormal, WinningNormal), (written.VertexNormals![12, 5], written.VertexNormals[12, 6]));
        Assert.NotEqual(WinningNormal, written.VertexNormals[11, 5]);
    }

    [Fact]
    public void UndoingLandPartsBringsBackOnlyTheListedColoursAndQuarters()
    {
        _land.Layers.Add(Layer(Quadrant.BottomLeft, 1));
        _land.Layers.Add(Layer(Quadrant.TopRight, 1));
        var repainted = LandCopyIn(_cleaned.Mod);
        repainted.VertexColors![5, 0] = CleanedColour;
        repainted.VertexColors[6, 0] = CleanedColour;
        repainted.Layers.Clear();
        repainted.Layers.Add(Layer(Quadrant.BottomLeft, 2));
        repainted.Layers.Add(Layer(Quadrant.TopRight, 3));

        var written = WrittenLand(Write(LandHandles(repainted), new UndoLandParts(LandKey, PointSet.Of([5]), QuarterSet.Of([QuarterOf(Quadrant.BottomLeft)]), Rest: false)));

        Assert.Equal((default(P3UInt8), CleanedColour), (written.VertexColors![5, 0], written.VertexColors[6, 0]));
        Assert.Equal(
            [(Quadrant.BottomLeft, (ushort)1), (Quadrant.TopRight, (ushort)3)],
            written.Layers.Select(layer => (layer.Header!.Quadrant, layer.Header.LayerNumber)).OrderBy(layer => layer.Quadrant));
        Assert.Equal(LandHeightReader.Read(repainted), LandHeightReader.Read(written));
    }

    /// <summary>A later plugin deletes the cell holding the LAND record; writing into it is a bug, since the restore never lets a part go back there.</summary>
    [Fact]
    public void NoLandPartIsWrittenIntoACellAPluginDeletes()
    {
        var repainted = LandCopyIn(_cleaned.Mod);
        repainted.VertexColors![5, 0] = CleanedColour;
        CellCopyIn(_later.Mod).IsDeleted = true;

        Assert.Throws<InvalidOperationException>(() => Write(LandHandles(repainted), new UndoLandParts(LandKey, PointSet.Of([5]), QuarterSet.Of([]), Rest: false)));
    }

    [Fact]
    public void UndoingTheRestOfALandRecordBringsBackWhichGridsItHoldsAndTheFlagsFollow()
    {
        _land.VertexColors = null;
        _land.Flags = Landscape.Flag.VertexNormalsHeightMap;
        var coloured = LandCopyIn(_cleaned.Mod);
        coloured.VertexColors = new Array2d<P3UInt8>(new P2Int(Side, Side), CleanedColour);
        coloured.Flags = Landscape.Flag.VertexNormalsHeightMap | Landscape.Flag.VertexColors;

        var written = WrittenLand(Write(LandHandles(coloured), new UndoLandParts(LandKey, PointSet.Of([]), QuarterSet.Of([]), Rest: true)));

        Assert.Null(written.VertexColors);
        Assert.Equal(Landscape.Flag.VertexNormalsHeightMap, written.Flags);
    }

    [Fact]
    public void CopyingTheGeometryWritesTheEarlierGeometryAndKeepsTheWinnersParentAndDeletedState()
    {
        var edited = NavmeshCopyIn(_cleaned.Mod);
        edited.Data!.Vertices[2] = new P3Float(Size, Size, RaisedHeight);
        edited.Data.Triangles[0].Flags |= NavmeshTriangle.Flag.Preferred;
        var otherParent = new CellNavmeshParent { Parent = new FormLink<ICellGetter>(_cleaned.NextKey()) };
        edited.Data.Parent = otherParent;
        edited.IsDeleted = true;

        var written = WrittenNavmesh(Write(NavmeshHandles(_navmesh, edited), new CopyNavmeshGeometry(NavmeshKey)));

        Assert.Equal(_navmesh.Data!.Vertices.ToList(), written.Data!.Vertices.ToList());
        Assert.Equal(_navmesh.Data.Triangles.Select(triangle => triangle.Flags), written.Data.Triangles.Select(triangle => triangle.Flags));
        Assert.True(otherParent.Equals(written.Data.Parent));
        Assert.True(written.IsDeleted);
    }

    [Fact]
    public void GeometryHoldingRawBytesInAnotherRecordFormatIsNotWritten()
    {
        _navmesh.FormVersion = OlderFormVersion;
        _navmesh.NNAM = RawBytes;
        var edited = NavmeshCopyIn(_cleaned.Mod);
        edited.FormVersion = NewerFormVersion;
        edited.Data!.Vertices[2] = new P3Float(Size, Size, RaisedHeight);

        Assert.Throws<InvalidOperationException>(() => Write(NavmeshHandles(_navmesh, edited), new CopyNavmeshGeometry(NavmeshKey)));
    }

    [Fact]
    public void GeometryHoldingRawBytesInTheSameRecordFormatIsWritten()
    {
        _navmesh.FormVersion = OlderFormVersion;
        _navmesh.NNAM = RawBytes;
        var edited = NavmeshCopyIn(_cleaned.Mod);
        edited.NNAM = null;
        edited.Data!.Vertices[2] = new P3Float(Size, Size, RaisedHeight);

        var written = WrittenNavmesh(Write(NavmeshHandles(_navmesh, edited), new CopyNavmeshGeometry(NavmeshKey)));

        Assert.Equal(RawBytes, written.NNAM!.Value.ToArray());
        Assert.Equal(_navmesh.Data!.Vertices.ToList(), written.Data!.Vertices.ToList());
    }

    [Fact]
    public void ABroughtBackNavmeshIsItsEarlierVersionWholeAndNotDeletedInItsEarlierCellInItsOwnRecordFormat()
    {
        _navmesh.FormVersion = OlderFormVersion;
        _navmesh.NNAM = RawBytes;
        var deleted = NavmeshCopyIn(_cleaned.Mod);
        deleted.FormVersion = NewerFormVersion;
        deleted.IsDeleted = true;
        deleted.Data = null;
        deleted.NNAM = null;

        var patch = Write(NavmeshHandles(_navmesh, deleted), new BringBackNavmesh(NavmeshKey));

        var written = WrittenNavmesh(patch);
        Assert.True(_navmesh.Equals(written));
        Assert.False(written.IsDeleted);
        Assert.Equal(OlderFormVersion, written.FormVersion);
        var cell = Assert.Single(patch.EnumerateMajorRecords<ICellGetter>());
        Assert.Equal((_cell.FormKey, true), (cell.FormKey, cell.NavigationMeshes.Any(navmesh => navmesh.FormKey == _navmesh.FormKey)));
    }

    [Fact]
    public void ASunkNavmeshLiesFlatAtTheGivenDepthCutOffWhileItsTrianglesInnerEdgesAndGridStay()
    {
        var added = AddedNavmesh();

        var written = WrittenNavmesh(Write(AddedNavmeshHandles(added), new SinkNavmesh(added.FormKey.ToRecordKey(), RemovalConvention.SunkNavmeshZ)), added.FormKey);

        var data = written.Data!;
        const float z = RemovalConvention.SunkNavmeshZ;
        Assert.Equal(added.Data!.Vertices.Select(vertex => new P3Float(vertex.X, vertex.Y, z)), data.Vertices);
        Assert.Equal((new P3Float(0, 0, z), new P3Float(Size, Size, z)), (data.Min, data.Max));
        Assert.Equal(added.Data.Triangles.Select(triangle => triangle.Vertices), data.Triangles.Select(triangle => triangle.Vertices));
        Assert.Equal(
            [((short)-1, (short)1, (short)-1, (NavmeshTriangle.Flag)0), ((short)-1, (short)-1, (short)0, NavmeshTriangle.Flag.Water)],
            data.Triangles.Select(triangle => (triangle.EdgeLink_0_1, triangle.EdgeLink_1_2, triangle.EdgeLink_2_0, triangle.Flags)));
        Assert.All(data.Triangles, triangle => Assert.Equal(((ushort)0, false), (triangle.CoverFlags, triangle.IsCover)));
        Assert.Empty(data.EdgeLinks);
        Assert.Empty(data.DoorTriangles);
        Assert.Equal(RawBytes, data.NavmeshGrid.ToArray());
        Assert.True(added.Data.Parent.Equals(data.Parent));
        Assert.False(written.IsDeleted);
    }

    [Fact]
    public void ThePatchsNavmeshInfoMapHoldsOnlyItsEntriesInKeyOrderWithTheWinnersPathData()
    {
        var added = _cleaned.NextKey();
        var cleanedMap = MapIn(_cleaned.Mod);
        cleanedMap.MapInfos.Add(Entry(_navmesh.FormKey, z: RaisedHeight));
        cleanedMap.MapInfos.Add(AddedEntry(added));
        var laterMap = MapIn(_later.Mod);
        laterMap.FormVersion = OlderFormVersion;
        laterMap.PreferredPathing = PreferredPathsThrough(added);
        laterMap.NVSI = new byte[] { 9, 9 };
        laterMap.NavMeshVersion = 13;
        laterMap.MapInfos.Add(Entry(_later.NextKey(), z: 0f));
        var order = new WriteNavmeshMapEntries(
            _map.FormKey.ToRecordKey(),
            [new NavmeshEntryOrder(added.ToRecordKey(), Cleaned, RemovalConvention.SunkNavmeshZ), new NavmeshEntryOrder(NavmeshKey, BaseGame, SinkTo: null)]);

        var map = Assert.Single(Write(MapHandles(cleanedMap, laterMap), order).EnumerateMajorRecords<INavigationMeshInfoMapGetter>());

        Assert.Equal([_navmesh.FormKey, added], map.MapInfos.Select(entry => entry.NavigationMesh.FormKey));
        Assert.True(_map.MapInfos[0].Equals(map.MapInfos[0]));
        Assert.True(laterMap.PreferredPathing.Equals(map.PreferredPathing));
        Assert.Equal([9, 9], map.NVSI!.Value.ToArray());
        Assert.Equal(((uint?)13, OlderFormVersion), (map.NavMeshVersion, map.FormVersion));
    }

    [Fact]
    public void ASunkEntryIsLoweredWithItsIslandAndHasNoMergesOrDoors()
    {
        var added = _cleaned.NextKey();
        var cleanedMap = MapIn(_cleaned.Mod);
        cleanedMap.MapInfos.Add(AddedEntry(added));
        var order = new WriteNavmeshMapEntries(_map.FormKey.ToRecordKey(), [new NavmeshEntryOrder(added.ToRecordKey(), Cleaned, RemovalConvention.SunkNavmeshZ)]);

        var entry = Assert.Single(Assert.Single(Write(MapHandles(cleanedMap), order).EnumerateMajorRecords<INavigationMeshInfoMapGetter>()).MapInfos);

        const float z = RemovalConvention.SunkNavmeshZ;
        var island = entry.Island!;
        Assert.Equal((new P3Float(Size / 2, Size / 2, z), new P3Float(0, 0, z), new P3Float(Size, Size, z)), (entry.Point, island.Min, island.Max));
        Assert.Equal([new P3Float(0, 0, z), new P3Float(Size, 0, z)], island.Vertices);
        Assert.Equal(AddedEntry(added).Island!.Triangles.ToList(), island.Triangles.ToList());
        Assert.Equal((0, 0, 0), (entry.MergedTo.Count, entry.PreferredMerges.Count, entry.LinkedDoors.Count));
        Assert.True(AddedEntry(added).Parent.Equals(entry.Parent));
    }

    [Fact]
    public void ALaterOrderAddsItsEntriesToThoseThePatchsMapAlreadyHolds()
    {
        var added = _cleaned.NextKey();
        var cleanedMap = MapIn(_cleaned.Mod);
        cleanedMap.MapInfos.Add(AddedEntry(added));
        var mapKey = _map.FormKey.ToRecordKey();

        var map = Assert.Single(Write(
            MapHandles(cleanedMap),
            new WriteNavmeshMapEntries(mapKey, [new NavmeshEntryOrder(added.ToRecordKey(), Cleaned, SinkTo: null)]),
            new WriteNavmeshMapEntries(mapKey, [new NavmeshEntryOrder(NavmeshKey, BaseGame, SinkTo: null)])).EnumerateMajorRecords<INavigationMeshInfoMapGetter>());

        Assert.Equal([_navmesh.FormKey, added], map.MapInfos.Select(entry => entry.NavigationMesh.FormKey));
    }

    [Fact]
    public void ADoorsNavmeshLinkIsWrittenAloneWhileItsOtherFieldsStay()
    {
        var door = (PlacedObject)_door.DeepCopy();
        door.NavigationDoorLink = new NavigationDoorLink { NavMesh = new FormLink<INavigationMeshGetter>(_cleaned.NextKey()), TeleportMarkerTriangle = 0 };
        door.Scale = 2f;
        CellCopyIn(_cleaned.Mod).Temporary.Add(door);
        var key = _door.FormKey.ToRecordKey();
        var handles = new RecordHandles(
            new Dictionary<RecordKey, PlacedRecordHandle> { [key] = new(door, new PlacedRecordLocation(CellCopyIn(_cleaned.Mod), InPersistentList: false)) },
            new Dictionary<RecordKey, IPlacedGetter> { [key] = _door },
            new PlacedRecordParts(new PlacedPartTolerances()),
            areaRecords: null);

        var written = Assert.Single(Write(handles, new UndoPlacedParts(key, PlacedParts.NavDoorLink)).EnumerateMajorRecords<IPlacedObjectGetter>());

        Assert.True(_door.NavigationDoorLink!.Equals(written.NavigationDoorLink));
        Assert.Equal(2f, written.Scale);
    }

    /// <summary>A later plugin deletes the cell holding the door; a part going back there is a bug, since the restore never lets one go back into it.</summary>
    [Fact]
    public void NoPlacedPartIsWrittenIntoACellAPluginDeletes()
    {
        var door = (PlacedObject)_door.DeepCopy();
        door.Scale = 2f;
        CellCopyIn(_cleaned.Mod).Temporary.Add(door);
        CellCopyIn(_later.Mod).IsDeleted = true;
        var key = _door.FormKey.ToRecordKey();
        var handles = new RecordHandles(
            new Dictionary<RecordKey, PlacedRecordHandle> { [key] = new(door, new PlacedRecordLocation(CellCopyIn(_cleaned.Mod), InPersistentList: false)) },
            new Dictionary<RecordKey, IPlacedGetter> { [key] = _door },
            new PlacedRecordParts(new PlacedPartTolerances()),
            areaRecords: null);

        Assert.Throws<InvalidOperationException>(() => Write(handles, new UndoPlacedParts(key, PlacedParts.Scale)));
    }

    /// <remarks>
    /// Every record of the input plugins is compared afterwards with a copy taken before the writing, so a writer that changed an input
    /// record in place fails here, and the expected values the tests take from the input records are the ones the writer read.
    /// </remarks>
    private ISkyrimModGetter Write(RecordHandles handles, params WriteOrder[] orders)
    {
        List<IMajorRecordGetter> inputs = [.. new[] { _base.Mod, _cleaned.Mod, _later.Mod }.SelectMany(mod => mod.EnumerateMajorRecords())];
        var before = inputs.Select(record => record.DeepCopy()).ToList();
        var state = FixturePatcherState.Create([_base.Mod, _cleaned.Mod, _later.Mod], Path.GetTempPath(), Path.GetTempPath());
        new PatchWriting(new SynthesisWriteCache(state.LinkCache), state.PatchMod).Write(handles, orders);
        Assert.All(inputs.Zip(before), input => Assert.True(input.First.Equals(input.Second), $"Input record {input.First.FormKey} was changed by the writing."));
        return state.PatchMod;
    }

    private ILandscapeGetter WrittenLand(ISkyrimModGetter patch) =>
        Assert.Single(patch.EnumerateMajorRecords<ILandscapeGetter>(), land => land.FormKey == _land.FormKey);

    private INavigationMeshGetter WrittenNavmesh(ISkyrimModGetter patch, FormKey? navmesh = null) =>
        Assert.Single(patch.EnumerateMajorRecords<INavigationMeshGetter>(), written => written.FormKey == (navmesh ?? _navmesh.FormKey));

    private RecordHandles LandHandles(Landscape winner) => AreaHandles(
        new Dictionary<RecordKey, RecordVersions<ILandscapeGetter>> { [LandKey] = new([BaseGame, Cleaned], [_land, winner]) },
        new Dictionary<RecordKey, RecordVersions<NavmeshVersion?>>(),
        []);

    private RecordHandles NavmeshHandles(NavigationMesh earlier, NavigationMesh winner) => AreaHandles(
        new Dictionary<RecordKey, RecordVersions<ILandscapeGetter>>(),
        new Dictionary<RecordKey, RecordVersions<NavmeshVersion?>> { [NavmeshKey] = new([BaseGame, Cleaned], [new NavmeshVersion(earlier, CellKey, CellPlace), new NavmeshVersion(winner, CellKey, CellPlace)]) },
        []);

    private RecordHandles AddedNavmeshHandles(NavigationMesh added) => AreaHandles(
        new Dictionary<RecordKey, RecordVersions<ILandscapeGetter>>(),
        new Dictionary<RecordKey, RecordVersions<NavmeshVersion?>> { [added.FormKey.ToRecordKey()] = new([Cleaned, Cleaned], [null, new NavmeshVersion(added, CellKey, CellPlace)]) },
        []);

    private RecordHandles MapHandles(NavigationMeshInfoMap cleanedMap, NavigationMeshInfoMap? laterMap = null) => AreaHandles(
        new Dictionary<RecordKey, RecordVersions<ILandscapeGetter>>(),
        new Dictionary<RecordKey, RecordVersions<NavmeshVersion?>>(),
        [
            new NavmeshMapVersion(0, BaseGame, _map),
            new NavmeshMapVersion(1, Cleaned, cleanedMap),
            .. laterMap is null ? (NavmeshMapVersion[])[] : [new NavmeshMapVersion(2, Later, laterMap)],
        ]);

    private static RecordHandles AreaHandles(
        IReadOnlyDictionary<RecordKey, RecordVersions<ILandscapeGetter>> lands,
        IReadOnlyDictionary<RecordKey, RecordVersions<NavmeshVersion?>> navmeshes,
        ImmutableArray<NavmeshMapVersion> maps) => new(
        new Dictionary<RecordKey, PlacedRecordHandle>(),
        new Dictionary<RecordKey, IPlacedGetter>(),
        new PlacedRecordParts(new PlacedPartTolerances()),
        new AreaRecordHandles(
            new Dictionary<RecordKey, RecordVersions<ICellGetter>>(),
            lands,
            new NavmeshRecordHandles(navmeshes, maps, new Dictionary<RecordKey, NavmeshVersion>(), new Dictionary<RecordKey, NavDoorLinkFact>()),
            new CellRecordParts(0f),
            new LandRecordParts(0f)));

    /// <summary>
    /// A navmesh of the cleaned mod's own in its copy of the cell: its first triangle's first edge is an edge link and its second edge
    /// borders the second triangle, which is preferred, a door, water and cover, and has a door triangle.
    /// </summary>
    private NavigationMesh AddedNavmesh()
    {
        var added = Square(_cleaned.NextKey(), _cell.FormKey);
        var data = added.Data!;
        data.Vertices[2] = new P3Float(Size, Size, 3f);
        data.Min = new P3Float(0, 0, -5f);
        data.Max = new P3Float(Size, Size, 7f);
        data.NavmeshGrid = RawBytes;
        data.Triangles[0].Flags |= NavmeshTriangle.Flag.EdgeLink_0_1;
        data.Triangles[0].EdgeLink_0_1 = 0;
        data.Triangles[0].EdgeLink_1_2 = 1;
        data.EdgeLinks.Add(new EdgeLink { Mesh = new FormLink<INavigationMeshGetter>(_navmesh.FormKey), TriangleIndex = 0 });
        data.Triangles[1].EdgeLink_2_0 = 0;
        data.Triangles[1].Flags |= NavmeshTriangle.Flag.Preferred | NavmeshTriangle.Flag.Door | NavmeshTriangle.Flag.Water;
        data.Triangles[1].CoverFlags = 5;
        data.Triangles[1].IsCover = true;
        data.DoorTriangles.Add(new DoorTriangle { TriangleBeforeDoor = DoorTriangle, Door = new FormLink<IPlacedObjectGetter>(_door.FormKey) });
        CellCopyIn(_cleaned.Mod).NavigationMeshes.Add(added);
        return added;
    }

    private static NavigationMesh Square(FormKey key, FormKey cell) => new(key, FixtureMod.Release)
    {
        Data = new NavigationMeshData
        {
            Parent = new CellNavmeshParent { Parent = new FormLink<ICellGetter>(cell) },
            Vertices = { new P3Float(0, 0, 0), new P3Float(Size, 0, 0), new P3Float(Size, Size, 0), new P3Float(0, Size, 0) },
            Triangles = { Triangle(0, 1, 2), Triangle(0, 2, 3) },
            Min = new P3Float(0, 0, 0),
            Max = new P3Float(Size, Size, 0),
        },
    };

    private static NavmeshTriangle Triangle(short first, short second, short third) =>
        new() { Vertices = new P3Int16(first, second, third), EdgeLink_0_1 = -1, EdgeLink_1_2 = -1, EdgeLink_2_0 = -1 };

    private static NavigationMapInfo Entry(FormKey navmesh, float z) => new()
    {
        NavigationMesh = new FormLink<INavigationMeshGetter>(navmesh),
        Parent = new NavigationMapInfoCellParent { ParentCell = new FormLink<ICellGetter>(FormKey.Factory("000A00:Base.esm")) },
        Point = new P3Float(Size / 2, Size / 2, z),
    };

    /// <summary>The cleaned mod's entry of a navmesh it added: merged into the base game's navmesh, linked to the door, with an island.</summary>
    private NavigationMapInfo AddedEntry(FormKey added)
    {
        var entry = Entry(added, z: 4f);
        entry.Island = new IslandData
        {
            Min = new P3Float(0, 0, 1f),
            Max = new P3Float(Size, Size, 5f),
            Vertices = { new P3Float(0, 0, 2f), new P3Float(Size, 0, 3f) },
            Triangles = { new P3Int16(0, 1, 0) },
        };
        entry.MergedTo.Add(new FormLink<INavigationMeshGetter>(_navmesh.FormKey));
        entry.PreferredMerges.Add(new FormLink<INavigationMeshGetter>(_navmesh.FormKey));
        entry.LinkedDoors.Add(new LinkedDoor { Door = new FormLink<IPlacedObjectGetter>(_door.FormKey) });
        return entry;
    }

    private static PreferredPathing PreferredPathsThrough(FormKey navmesh) =>
        new() { NavmeshSets = { new NavmeshSet { Navmeshes = { new FormLink<INavigationMeshGetter>(navmesh) } } } };

    private static Landscape FlatLand(FormKey key, P3UInt8 normal) => new(key, FixtureMod.Release)
    {
        VertexHeightMap = new LandscapeVertexHeightMap { Offset = 0f, HeightMap = new Array2d<sbyte>(new P2Int(Side, Side), default) },
        VertexNormals = new Array2d<P3UInt8>(new P2Int(Side, Side), normal),
        VertexColors = new Array2d<P3UInt8>(new P2Int(Side, Side), default),
        Flags = Landscape.Flag.VertexNormalsHeightMap | Landscape.Flag.VertexColors,
    };

    private static AlphaLayer Layer(Quadrant quadrant, ushort layerNumber) => new()
    {
        Header = new LayerHeader { Quadrant = quadrant, Texture = new FormLink<ILandscapeTextureGetter>(FormKey.Factory("000F01:Base.esm")), LayerNumber = layerNumber },
    };

    private static int QuarterOf(Quadrant quadrant)
    {
        var (_, x, y) = LandLayerReader.QuadrantPlaces.Single(place => place.Quadrant == quadrant);
        return TerrainQuadrants.IndexOf(x, y);
    }

    private static void Fill(IArray2d<P3UInt8> grid, P3UInt8 value)
    {
        for (var y = 0; y < Side; y++)
        {
            for (var x = 0; x < Side; x++) grid[x, y] = value;
        }
    }

    private static float[] Heights(Func<int, int, float> heightAt) =>
        [.. Enumerable.Range(0, Side * Side).Select(point => heightAt(point % Side, point / Side))];

    private static PointSet PointsWhere(Func<int, int, bool> include) =>
        PointSet.Of(Enumerable.Range(0, Side * Side).Where(point => include(point % Side, point / Side)));

    /// <summary>A negative normal component as the vertex normals store it, as a signed byte.</summary>
    private static byte StoredNegative(int magnitude) => unchecked((byte)(sbyte)-magnitude);

    private NavigationMesh NavmeshCopyIn(ISkyrimMod mod)
    {
        var copy = (NavigationMesh)_navmesh.DeepCopy();
        CellCopyIn(mod).NavigationMeshes.Add(copy);
        return copy;
    }

    private Landscape LandCopyIn(ISkyrimMod mod) => CellCopyIn(mod).Landscape ??= (Landscape)_land.DeepCopy();

    /// <summary>The plugin's copy of the base game's cell, without its children, made on first use.</summary>
    private Cell CellCopyIn(ISkyrimMod mod)
    {
        if (_cellCopies.TryGetValue(mod.ModKey, out var copy)) return copy;
        copy = (Cell)_cell.DeepCopy();
        copy.Landscape = null;
        copy.NavigationMeshes.Clear();
        copy.Temporary.Clear();
        WorldspaceIn(mod).AddCell(copy);
        _cellCopies.Add(mod.ModKey, copy);
        return copy;
    }

    private NavigationMeshInfoMap MapIn(ISkyrimMod mod)
    {
        var map = (NavigationMeshInfoMap)_map.DeepCopy();
        map.MapInfos.Clear();
        mod.NavigationMeshInfoMaps.Add(map);
        return map;
    }

    private Worldspace WorldspaceIn(ISkyrimMod mod)
    {
        if (_worldspaceCopies.TryGetValue(mod.ModKey, out var worldspace)) return worldspace;
        worldspace = new Worldspace(_worldspace, FixtureMod.Release);
        mod.Worldspaces.Add(worldspace);
        _worldspaceCopies.Add(mod.ModKey, worldspace);
        return worldspace;
    }
}
