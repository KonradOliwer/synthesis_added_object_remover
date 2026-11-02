using System.Numerics;
using AddedObjectRemover.Tests.EndToEnd;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.LoadOrder;

/// <summary>
/// Which navmeshes the cleaned mod edited, deleted, replaced or added, and what goes back, read from in-memory plugins by the real
/// reader and collector. The base game has a worldspace with a navmesh in one cell, edge-linked to a neighbour in the next cell, a
/// door linked to the navmesh from both sides and a navmesh info map with an entry for each navmesh; the cleaned mod places an
/// object of its own in a cell of its own, which makes the worldspace a target space.
/// </summary>
public sealed class ChangedNavmeshCollectingTests
{
    private const float Size = 100f;
    private const float RaisedZ = 10f;
    private const short DoorTriangle = 1;
    private const short LinkedTriangle = 0;

    private static readonly WhatToCollect Plan = new(new ScanNeeds(AreaRecordVersions: new AreaRecordReads(new AreaPartTolerances(8f, 8f), Cells: true, Lands: true)), SettingDefaults.MovedDistanceInUnits, ProcessReplaced: false);
    private static readonly PluginName Patch = FixturePatcherState.PatchModKey.ToPluginName();

    private readonly FixtureMod _base = new("Base.esm");
    private readonly FixtureMod _cleaned;
    private readonly FixtureMod _later;
    private readonly FormKey _worldspace;
    private readonly Cell _cell;
    private readonly Cell _nextCell;
    private readonly NavigationMesh _navmesh;
    private readonly NavigationMesh _neighbour;
    private readonly PlacedObject _door;
    private readonly FormKey _doorOnlyInTheEntry;
    private readonly NavigationMeshInfoMap _map;
    private readonly Dictionary<ModKey, Worldspace> _worldspaceCopies = [];
    private readonly Dictionary<(ModKey, FormKey), Cell> _cellCopies = [];
    private readonly Dictionary<ModKey, NavigationMeshInfoMap> _mapCopies = [];

    public ChangedNavmeshCollectingTests()
    {
        _cleaned = new FixtureMod("Cleaned.esp", _base);
        _later = new FixtureMod("Later.esp", _base);
        _worldspace = _base.NextKey();
        _cell = new Cell(_base.NextKey(), FixtureMod.Release) { Grid = new CellGrid { Point = new P2Int(0, 0) } };
        _nextCell = new Cell(_base.NextKey(), FixtureMod.Release) { Grid = new CellGrid { Point = new P2Int(0, 1) } };
        _navmesh = Square(_base.NextKey(), _cell.FormKey, minX: 0f);
        _neighbour = Square(_base.NextKey(), _nextCell.FormKey, minX: Size);
        LinkRightEdge(_navmesh, _neighbour.FormKey);
        LinkRightEdge(_neighbour, _navmesh.FormKey);
        _door = Door(_base, _navmesh.FormKey);
        _navmesh.Data!.DoorTriangles.Add(new DoorTriangle { TriangleBeforeDoor = DoorTriangle, Door = new FormLink<IPlacedObjectGetter>(_door.FormKey) });
        _doorOnlyInTheEntry = _base.NextKey();
        _cell.NavigationMeshes.Add(_navmesh);
        _cell.Temporary.Add(_door);
        _nextCell.NavigationMeshes.Add(_neighbour);
        WorldspaceIn(_base.Mod).AddCell(_cell);
        WorldspaceIn(_base.Mod).AddCell(_nextCell);
        _map = new NavigationMeshInfoMap(_base.NextKey(), FixtureMod.Release)
        {
            PreferredPathing = new PreferredPathing { NavmeshSets = { new NavmeshSet { Navmeshes = { new FormLink<INavigationMeshGetter>(_navmesh.FormKey) } } } },
        };
        var entry = Entry(_navmesh.FormKey, pointX: Size / 2);
        entry.LinkedDoors.Add(new LinkedDoor { Door = new FormLink<IPlacedObjectGetter>(_doorOnlyInTheEntry) });
        _map.MapInfos.Add(entry);
        _map.MapInfos.Add(Entry(_neighbour.FormKey, pointX: Size * 1.5f));
        _base.Mod.NavigationMeshInfoMaps.Add(_map);
        PlaceObjectOfTheCleanedMod();
    }

    private RecordKey NavmeshKey => _navmesh.FormKey.ToRecordKey();

    private RecordKey NeighbourKey => _neighbour.FormKey.ToRecordKey();

    private PluginName BaseGame => _base.Key.ToPluginName();

    private PluginName Cleaned => _cleaned.Key.ToPluginName();

    private PluginName Later => _later.Key.ToPluginName();

    [Fact]
    public void AnEditedNavmeshGoesBackWithItsGeometry()
    {
        Raise(NavmeshCopyIn(_cleaned.Mod));

        var changed = ChangedNavmeshOf(Collect());

        Assert.Equal(
            (NavmeshCase.Edited, (PartOutcome)new PartOutcome.Undo(), (PartOutcome)new PartOutcome.Unchanged(), (PartOutcome)new PartOutcome.Unchanged()),
            (changed.Case, changed.Geometry, changed.Presence, changed.MapEntry));
        Assert.Equal((0f, RaisedZ), (changed.Earlier!.Vertices[2].Z, changed.Current.Vertices[2].Z));
        Assert.Equal((BaseGame, Cleaned, Cleaned), (changed.Earlier.Plugin, changed.Current.Plugin, changed.WinningMod));
        Assert.Equal((_worldspace.ToRecordKey(), _cell.FormKey.ToRecordKey()), (changed.SpaceKey, changed.Cell));
    }

    [Fact]
    public void TheShapeHoldsTheTrianglesEdgeLinksDoorTrianglesAndPreferredFlags()
    {
        NavmeshCopyIn(_cleaned.Mod).Data!.Triangles[1].Flags |= NavmeshTriangle.Flag.Preferred;

        var changed = ChangedNavmeshOf(Collect());

        Assert.Equal([new NavmeshTriangleFact(0, 1, 2, false), new NavmeshTriangleFact(0, 2, 3, false)], changed.Earlier!.Triangles.ToList());
        Assert.Equal([false, true], changed.Current.Triangles.Select(triangle => triangle.Preferred));
        Assert.Equal([new NavmeshEdgeLinkFact(0, NeighbourKey, LinkedTriangle, new Vector3(Size, 0, 0), new Vector3(Size, Size, 0))], changed.Earlier.EdgeLinks.ToList());
        Assert.Equal([new NavmeshDoorTriangleFact(DoorTriangle, _door.FormKey.ToRecordKey())], changed.Earlier.DoorTriangles.ToList());
        Assert.Equal(new MeshTriangle(Vector3.Zero, new Vector3(Size, 0, 0), new Vector3(Size, Size, 0)), changed.Earlier.CornersOf(0));
    }

    [Fact]
    public void AnOverrideThatChangesNothingGivesNoChangedNavmesh()
    {
        NavmeshCopyIn(_cleaned.Mod);

        Assert.Empty(Collect().ChangedNavmeshes);
    }

    [Fact]
    public void ANavmeshTheCleanedModDeletesWithNothingAddedOverItIsDeletedWithoutReplacement()
    {
        NavmeshCopyIn(_cleaned.Mod).IsDeleted = true;

        var changed = ChangedNavmeshOf(Collect());

        Assert.Equal((NavmeshCase.Deleted, (PartOutcome)new PartOutcome.Undo()), (changed.Case, changed.Presence));
        Assert.True(changed.Current.IsDeleted);
        Assert.False(changed.Earlier!.IsDeleted);
        Assert.Equal(changed.Earlier.Triangles.ToList(), changed.Current.Triangles.ToList());
        Assert.Empty(changed.ReplacedBy);
    }

    [Fact]
    public void ANavmeshAddedOverADeletedOneInItsCellReplacesIt()
    {
        NavmeshCopyIn(_cleaned.Mod).IsDeleted = true;
        var added = AddNavmeshOfTheCleanedMod(minX: Size / 4);

        var collected = Collect();

        var deleted = ChangedNavmeshOf(collected);
        Assert.Equal((NavmeshCase.Deleted, added.ToRecordKey()), (deleted.Case, Assert.Single(deleted.ReplacedBy)));
        var addedFact = Assert.Single(collected.ChangedNavmeshes, navmesh => navmesh.Navmesh == added.ToRecordKey());
        Assert.Equal((NavmeshCase.Added, (PartOutcome)new PartOutcome.Undo(), (PartOutcome)new PartOutcome.Undo()), (addedFact.Case, addedFact.Geometry, addedFact.Presence));
        Assert.Null(addedFact.Earlier);
    }

    [Fact]
    public void ANavmeshAddedBesideADeletedOneLeavesTheDeletionWithoutReplacement()
    {
        NavmeshCopyIn(_cleaned.Mod).IsDeleted = true;
        var added = AddNavmeshOfTheCleanedMod(minX: Size);

        var collected = Collect();

        Assert.Empty(ChangedNavmeshOf(collected).ReplacedBy);
        Assert.Equal(NavmeshCase.Added, Assert.Single(collected.ChangedNavmeshes, navmesh => navmesh.Navmesh == added.ToRecordKey()).Case);
    }

    [Fact]
    public void TheMapEntryTheCleanedModChangesGoesBackWhenALaterPluginOnlyCarriesItOver()
    {
        NavmeshCopyIn(_cleaned.Mod);
        MapIn(_cleaned.Mod).MapInfos.Add(Entry(_navmesh.FormKey, pointX: Size));
        MapIn(_later.Mod).MapInfos.Add(Entry(_navmesh.FormKey, pointX: Size));

        var changed = ChangedNavmeshOf(Collect());

        Assert.Equal(((PartOutcome)new PartOutcome.Undo(), (PartOutcome)new PartOutcome.Unchanged()), (changed.MapEntry, changed.Geometry));
        Assert.Equal((BaseGame, Cleaned), (changed.Earlier!.MapEntry!.Plugin, changed.Current.MapEntry!.Plugin));
    }

    [Fact]
    public void AMapEntryALaterPluginChangesAgainIsLeftToIt()
    {
        NavmeshCopyIn(_cleaned.Mod);
        MapIn(_cleaned.Mod).MapInfos.Add(Entry(_navmesh.FormKey, pointX: Size));
        MapIn(_later.Mod).MapInfos.Add(Entry(_navmesh.FormKey, pointX: Size * 2));

        Assert.Equal(new PartOutcome.LeftToLaterPlugin(Later), ChangedNavmeshOf(Collect()).MapEntry);
    }

    /// <summary>The cleaned mod raises the navmesh and no later plugin overrides its record, but a later plugin's map carries its entry.</summary>
    [Fact]
    public void TheWinningMapEntryIsTheLastOneCarriedWhoeverWinsTheNavmeshRecord()
    {
        Raise(NavmeshCopyIn(_cleaned.Mod));
        MapIn(_later.Mod).MapInfos.Add(Entry(_navmesh.FormKey, pointX: Size));

        var changed = ChangedNavmeshOf(Collect());

        Assert.Equal((null, BaseGame, Later), (changed.LaterWinner, changed.Winner.MapEntry?.Plugin, changed.WinningMapEntry?.Plugin));
    }

    /// <summary>The cleaned mod disables the door its navmesh names, and a later plugin carries the disable forward, so no part of the door goes back.</summary>
    [Fact]
    public void ADoorALaterPluginKeepsDisabledIsRemovedInTheWinningPresenceWithoutAChangedRecordFact()
    {
        Raise(NavmeshCopyIn(_cleaned.Mod));
        foreach (var mod in new[] { _cleaned.Mod, _later.Mod })
        {
            var door = (PlacedObject)_door.DeepCopy();
            door.MajorRecordFlagsRaw |= (int)SkyrimMajorRecord.SkyrimMajorRecordFlag.InitiallyDisabled;
            CellCopyIn(mod, _cell).Temporary.Add(door);
        }

        var collected = Collect();

        Assert.Equal(new PlacedPresence(Removed: true, Parent: null, OppositeOfParent: false), collected.WinningPresence[_door.FormKey.ToRecordKey()]);
        Assert.DoesNotContain(collected.ChangedRecords, record => record.Key == _door.FormKey.ToRecordKey());
    }

    [Fact]
    public void TheEntriesAreThoseOfThePluginsWhoseMapCarriesOneAndAMapWithoutTheEntryChangesNothing()
    {
        Raise(NavmeshCopyIn(_cleaned.Mod));
        MapIn(_cleaned.Mod).MapInfos.Add(Entry(_neighbour.FormKey, pointX: 0));

        var changed = ChangedNavmeshOf(Collect());

        Assert.Equal(new PartOutcome.Unchanged(), changed.MapEntry);
        Assert.Equal((BaseGame, BaseGame), (changed.Earlier!.MapEntry!.Plugin, changed.Current.MapEntry!.Plugin));
        Assert.Equal([_doorOnlyInTheEntry.ToRecordKey()], changed.Current.MapEntry.LinkedDoors.ToList());
    }

    [Fact]
    public void AGeometryALaterPluginChangesAgainIsLeftToIt()
    {
        var raised = NavmeshCopyIn(_cleaned.Mod);
        Raise(raised);
        NavmeshCopyIn(_later.Mod, from: raised).Data!.Vertices[0] = new P3Float(0, 0, RaisedZ);

        var changed = ChangedNavmeshOf(Collect());

        Assert.Equal((new PartOutcome.LeftToLaterPlugin(Later), Later), (changed.Geometry, changed.WinningMod));
    }

    /// <summary>The neighbour stays edge-linked to the navmesh, but a navmesh a later plugin changed again never goes back.</summary>
    [Fact]
    public void ANavmeshALaterPluginChangedAgainKeepsNoNeighbourShapes()
    {
        var raised = NavmeshCopyIn(_cleaned.Mod);
        Raise(raised);
        NavmeshCopyIn(_later.Mod, from: raised).Data!.Vertices[0] = new P3Float(0, 0, RaisedZ);

        Assert.Empty(ChangedNavmeshOf(Collect()).Neighbours);
    }

    /// <summary>The cleaned mod changes only a cell's music; the base game's navmesh info map is not read.</summary>
    [Fact]
    public void WithoutANavmeshTheCleanedModChangesNoNavmeshInfoMapIsRead()
    {
        CellCopyIn(_cleaned.Mod, _cell).Music.SetTo(FormKey.Factory("000D01:Base.esm"));
        RecordHandles? handles = null;

        var collected = Collect(afterCollect: records => handles = (RecordHandles)records.ReadWritableRecords());

        Assert.Empty(collected.ChangedNavmeshes);
        Assert.Empty(handles!.NavmeshMaps);
    }

    /// <summary>The cleaned mod copies the neighbour unchanged and raises the navmesh.</summary>
    [Fact]
    public void TheHandlesKeepOnlyTheNavmeshesWithAChange()
    {
        Raise(NavmeshCopyIn(_cleaned.Mod));
        NavmeshCopyIn(_cleaned.Mod, from: _neighbour);
        RecordHandles? handles = null;

        Collect(afterCollect: records => handles = (RecordHandles)records.ReadWritableRecords());

        Assert.Equal([BaseGame, Cleaned], handles!.NavmeshOf(NavmeshKey).Plugins.ToList());
        Assert.Throws<KeyNotFoundException>(() => handles.NavmeshOf(NeighbourKey));
    }

    [Fact]
    public void NothingGoesBackInANavmeshALaterPluginDeletes()
    {
        var raised = NavmeshCopyIn(_cleaned.Mod);
        Raise(raised);
        NavmeshCopyIn(_later.Mod, from: raised).IsDeleted = true;

        Assert.Empty(Collect().ChangedNavmeshes);
    }

    /// <summary>The door is linked from both sides, a second door names the navmesh only from its own link, a third only in the map entry.</summary>
    [Fact]
    public void DoorLinksAreFoundFromTheNavmeshAndFromTheDoor()
    {
        Raise(NavmeshCopyIn(_cleaned.Mod));
        var doorLinkedOneWay = Door(_base, _navmesh.FormKey);
        _nextCell.Temporary.Add(doorLinkedOneWay);

        var doors = ChangedNavmeshOf(Collect()).Doors;

        var link = new NavDoorLinkFact(NavmeshKey, DoorTriangle);
        Assert.Equal(
            [
                new DoorLinkChange(_door.FormKey.ToRecordKey(), new PartOutcome.Unchanged(), link, link, InDeletedCell: false),
                new DoorLinkChange(_doorOnlyInTheEntry.ToRecordKey(), new PartOutcome.Unchanged(), null, null, InDeletedCell: false),
                new DoorLinkChange(doorLinkedOneWay.FormKey.ToRecordKey(), new PartOutcome.Unchanged(), link, link, InDeletedCell: false),
            ],
            doors.OrderBy(door => door.Door.Id));
    }

    [Fact]
    public void ADoorLinkTheCleanedModChangesGoesBackWithItsNavmesh()
    {
        Raise(NavmeshCopyIn(_cleaned.Mod));
        var door = (PlacedObject)_door.DeepCopy();
        door.NavigationDoorLink!.TeleportMarkerTriangle = 0;
        CellCopyIn(_cleaned.Mod, _cell).Temporary.Add(door);

        var doorLink = Assert.Single(ChangedNavmeshOf(Collect()).Doors, fact => fact.Door == _door.FormKey.ToRecordKey());

        Assert.Equal(
            new DoorLinkChange(_door.FormKey.ToRecordKey(), new PartOutcome.Undo(), new NavDoorLinkFact(NavmeshKey, DoorTriangle), new NavDoorLinkFact(NavmeshKey, 0), InDeletedCell: false),
            doorLink);
    }

    /// <summary>The cleaned mod drops the door's link and a later plugin links it to another triangle: the earlier link is the base game's, the current one the later plugin's.</summary>
    [Fact]
    public void ADoorsEarlierLinkIsItsVersionBelowTheCleanedModsAndItsCurrentLinkItsWinners()
    {
        Raise(NavmeshCopyIn(_cleaned.Mod));
        var dropped = (PlacedObject)_door.DeepCopy();
        dropped.NavigationDoorLink = null;
        CellCopyIn(_cleaned.Mod, _cell).Temporary.Add(dropped);
        var relinked = (PlacedObject)_door.DeepCopy();
        relinked.NavigationDoorLink!.TeleportMarkerTriangle = LinkedTriangle;
        CellCopyIn(_later.Mod, _cell).Temporary.Add(relinked);

        var doorLink = Assert.Single(ChangedNavmeshOf(Collect()).Doors, fact => fact.Door == _door.FormKey.ToRecordKey());

        Assert.Equal(((NavDoorLinkFact?)new NavDoorLinkFact(NavmeshKey, DoorTriangle), (NavDoorLinkFact?)new NavDoorLinkFact(NavmeshKey, LinkedTriangle)), (doorLink.Earlier, doorLink.Current));
    }

    /// <summary>
    /// The door's winner loads after the cleaned mod; the cleaned mod's version, neither the winner nor the one below the cleaned mod,
    /// is passed by before its link is read: a version whose link getter answers nothing is never asked for it.
    /// </summary>
    [Fact]
    public void ADoorVersionBetweenItsWinnerAndItsVersionBelowTheCleanedModIsPassedWithoutReadingItsLink()
    {
        var collector = new NavmeshVersionCollector(
            Cleaned, new PluginFootprint([], [], [], [], [], [], []), new PluginNames(new KnownPluginNames([BaseGame, Cleaned, Later])));
        var (door, cell) = (_door.FormKey.ToRecordKey(), _cell.FormKey.ToRecordKey());
        collector.SeeDoorVersion(door, _door, cell, isWinner: true, belowNamedPlugin: false);
        var unreadable = GetterProxy.Create<IPlacedObjectGetter>(new Dictionary<string, Func<object?>>());

        var failure = Record.Exception(() => collector.SeeDoorVersion(door, unreadable, cell, isWinner: false, belowNamedPlugin: false));

        Assert.Null(failure);
    }

    /// <summary>A later plugin raises the neighbour; another base navmesh links into the changed one without a link back.</summary>
    [Fact]
    public void NeighboursAreKeptWithTheirWinningAndEarlierVersions()
    {
        Raise(NavmeshCopyIn(_cleaned.Mod));
        Raise(NavmeshCopyIn(_later.Mod, from: _neighbour));
        var linkingOneWay = Square(_base.NextKey(), _nextCell.FormKey, minX: Size * 2);
        LinkRightEdge(linkingOneWay, _navmesh.FormKey);
        _nextCell.NavigationMeshes.Add(linkingOneWay);

        var neighbours = ChangedNavmeshOf(Collect()).Neighbours;

        Assert.Equal([NeighbourKey, linkingOneWay.FormKey.ToRecordKey()], neighbours.Select(neighbour => neighbour.Navmesh));
        var neighbour = neighbours[0];
        Assert.Equal((Later, BaseGame), (neighbour.Winner.Plugin, neighbour.Earlier!.Plugin));
        Assert.Equal((RaisedZ, 0f), (neighbour.Winner.Vertices[2].Z, neighbour.Earlier.Vertices[2].Z));
        Assert.Equal((BaseGame, BaseGame), (neighbour.Winner.MapEntry!.Plugin, neighbour.Earlier.MapEntry!.Plugin));
    }

    /// <summary>
    /// The cleaned mod moves the door and drops its navmesh link, and changes no navmesh: the door's record changed by the cleaned
    /// mod still carries its link before the cleaned mod and as it wins, and whether a later plugin deletes the door's cell.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADoorTheCleanedModChangesCarriesItsNavmeshLinksThoughNoNavmeshChanges(bool cellDeletedLater)
    {
        var door = (PlacedObject)_door.DeepCopy();
        door.Placement!.Position = new P3Float(Size, Size, 0);
        door.NavigationDoorLink = null;
        CellCopyIn(_cleaned.Mod, _cell).Temporary.Add(door);
        if (cellDeletedLater) CellCopyIn(_later.Mod, _cell).IsDeleted = true;

        var collected = Collect();

        Assert.Empty(collected.ChangedNavmeshes);
        var changed = Assert.Single(collected.ChangedRecords, record => record.Key == _door.FormKey.ToRecordKey());
        Assert.Equal(new DoorLinkChange(changed.Key, new PartOutcome.Undo(), new NavDoorLinkFact(NavmeshKey, DoorTriangle), Current: null, cellDeletedLater), changed.DoorLink);
        Assert.Equal(cellDeletedLater, changed.InDeletedCell);
    }

    /// <summary>
    /// The cleaned mod turns the door and changes no navmesh, so no navmesh's edge links and no map entry are read: here a base game
    /// navmesh's edge link or a map entry names a navmesh of a plugin outside the load order, whose name reading it would fail on. A
    /// turned door is no target, so the links from other records are read only from the cleaned mod.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WithoutANavmeshTheCleanedModChangesNoEdgeLinkAndNoMapEntryIsRead(bool edgeLink)
    {
        var door = (PlacedObject)_door.DeepCopy();
        door.Placement!.Rotation = new P3Float(0, 0, 1);
        CellCopyIn(_cleaned.Mod, _cell).Temporary.Add(door);
        var elsewhere = FormKey.Factory("000D01:Elsewhere.esm");
        if (edgeLink) LinkRightEdge(_neighbour, elsewhere);
        else _map.MapInfos.Add(Entry(elsewhere, pointX: Size * 3));

        var collected = Collect();

        Assert.Empty(collected.ChangedNavmeshes);
        Assert.NotNull(Assert.Single(collected.ChangedRecords, record => record.Key == _door.FormKey.ToRecordKey()).DoorLink);
    }

    [Fact]
    public void OnlyThePreferredPathsOfTheWinningMapAreRead()
    {
        Raise(NavmeshCopyIn(_cleaned.Mod));
        MapIn(_cleaned.Mod).PreferredPathing = new PreferredPathing();

        var changed = ChangedNavmeshOf(Collect());

        Assert.False(changed.NamedInPreferredPaths);
    }

    [Fact]
    public void WithoutTheRestoreGroupNoNavmeshIsRead()
    {
        NavmeshCopyIn(_cleaned.Mod).IsDeleted = true;
        AddNavmeshOfTheCleanedMod(minX: Size / 4);

        Assert.Empty(Collect(plan: Plan with { Needs = Plan.Needs with { AreaRecordVersions = null } }).ChangedNavmeshes);
    }

    [Fact]
    public void TheHandlesHoldEachNavmeshVersionAndEveryPluginsMap()
    {
        var raised = NavmeshCopyIn(_cleaned.Mod);
        Raise(raised);
        MapIn(_cleaned.Mod);
        var added = AddNavmeshOfTheCleanedMod(minX: Size / 4);
        RecordHandles? handles = null;

        Collect(patch => NavmeshCopyIn(patch, from: raised), records => handles = (RecordHandles)records.ReadWritableRecords());

        Assert.Equal([BaseGame, Cleaned, Patch], handles!.NavmeshOf(NavmeshKey).Plugins.ToList());
        Assert.Equal((0f, RaisedZ), (handles.NavmeshOf(NavmeshKey).Earlier!.Record.Data!.Vertices[2].Z, handles.NavmeshOf(NavmeshKey).Winner!.Record.Data!.Vertices[2].Z));
        Assert.Null(handles.NavmeshOf(added.ToRecordKey()).Earlier);
        Assert.Equal([BaseGame, Cleaned], handles.NavmeshMaps.Select(map => map.Plugin));
    }

    [Fact]
    public void TheFactsDoNotDependOnTheThreadCount()
    {
        NavmeshCopyIn(_cleaned.Mod).IsDeleted = true;
        AddNavmeshOfTheCleanedMod(minX: Size / 4);
        MapIn(_cleaned.Mod).MapInfos.Add(Entry(_navmesh.FormKey, pointX: Size));
        Raise(NavmeshCopyIn(_later.Mod, from: _neighbour));

        Assert.Equal(Describe(Collect(threads: 1)), Describe(Collect(threads: 8)));
    }

    private static string Describe(CollectedObjects collected) => string.Join(
        Environment.NewLine,
        collected.ChangedNavmeshes.Select(navmesh =>
            $"{navmesh.Navmesh} {navmesh.Case} {navmesh.Geometry} {navmesh.Presence} {navmesh.MapEntry} {string.Join(",", navmesh.ReplacedBy)} "
            + $"{string.Join(",", navmesh.Neighbours.Select(neighbour => $"{neighbour.Navmesh}/{neighbour.Winner.Plugin}/{neighbour.Earlier?.Plugin}"))} "
            + $"{string.Join(",", navmesh.Doors)} {string.Join(",", navmesh.Current.Vertices)} {navmesh.NamedInPreferredPaths}"));

    private static NavigationMesh Square(FormKey key, FormKey cell, float minX) => new(key, FixtureMod.Release)
    {
        Data = new NavigationMeshData
        {
            Parent = new CellNavmeshParent { Parent = new FormLink<ICellGetter>(cell) },
            Vertices = { new P3Float(minX, 0, 0), new P3Float(minX + Size, 0, 0), new P3Float(minX + Size, Size, 0), new P3Float(minX, Size, 0) },
            Triangles = { Triangle(0, 1, 2), Triangle(0, 2, 3) },
        },
    };

    private static NavmeshTriangle Triangle(short first, short second, short third) =>
        new() { Vertices = new P3Int16(first, second, third), EdgeLink_0_1 = -1, EdgeLink_1_2 = -1, EdgeLink_2_0 = -1 };

    /// <summary>Links the second edge of the first triangle, the square's right side for the navmesh, to the first triangle of another navmesh.</summary>
    private static void LinkRightEdge(NavigationMesh from, FormKey to)
    {
        var data = from.Data!;
        data.Triangles[0].Flags |= NavmeshTriangle.Flag.EdgeLink_1_2;
        data.Triangles[0].EdgeLink_1_2 = (short)data.EdgeLinks.Count;
        data.EdgeLinks.Add(new EdgeLink { Mesh = new FormLink<INavigationMeshGetter>(to), TriangleIndex = LinkedTriangle });
    }

    private static void Raise(NavigationMesh navmesh) => navmesh.Data!.Vertices[2] = new P3Float(navmesh.Data.Vertices[2].X, navmesh.Data.Vertices[2].Y, RaisedZ);

    private static NavigationMapInfo Entry(FormKey navmesh, float pointX) => new()
    {
        NavigationMesh = new FormLink<INavigationMeshGetter>(navmesh),
        Parent = new NavigationMapInfoCellParent { ParentCell = new FormLink<ICellGetter>(FormKey.Factory("000A00:Base.esm")) },
        Point = new P3Float(pointX, Size / 2, 0),
    };

    private static PlacedObject Door(FixtureMod mod, FormKey navmesh)
    {
        var door = new PlacedObject(mod.NextKey(), FixtureMod.Release)
        {
            Placement = new Placement { Position = new P3Float(Size / 2, Size / 2, 0) },
            NavigationDoorLink = new NavigationDoorLink { NavMesh = new FormLink<INavigationMeshGetter>(navmesh), TeleportMarkerTriangle = DoorTriangle },
        };
        door.Base.SetTo(mod.AddMarkerStatic($"Door{door.FormKey.ID:X}"));
        return door;
    }

    /// <summary>A square navmesh of the cleaned mod's own in its copy of the base game's navmesh cell.</summary>
    private FormKey AddNavmeshOfTheCleanedMod(float minX)
    {
        var added = Square(_cleaned.NextKey(), _cell.FormKey, minX);
        CellCopyIn(_cleaned.Mod, _cell).NavigationMeshes.Add(added);
        return added.FormKey;
    }

    /// <summary>The plugin's copy of a base game navmesh, in its copy of the navmesh's cell, made from the given version on first use.</summary>
    private NavigationMesh NavmeshCopyIn(ISkyrimMod mod, NavigationMesh? from = null)
    {
        var source = from ?? _navmesh;
        var cell = CellCopyIn(mod, source.FormKey == _neighbour.FormKey ? _nextCell : _cell);
        if (cell.NavigationMeshes.FirstOrDefault(navmesh => navmesh.FormKey == source.FormKey) is NavigationMesh existing) return existing;
        var copy = (NavigationMesh)source.DeepCopy();
        cell.NavigationMeshes.Add(copy);
        return copy;
    }

    /// <summary>The plugin's copy of a base game cell, without its navmeshes and placed records, made on first use.</summary>
    private Cell CellCopyIn(ISkyrimMod mod, Cell cell)
    {
        if (_cellCopies.TryGetValue((mod.ModKey, cell.FormKey), out var copy)) return copy;
        copy = (Cell)cell.DeepCopy();
        copy.NavigationMeshes.Clear();
        copy.Temporary.Clear();
        copy.Persistent.Clear();
        WorldspaceIn(mod).AddCell(copy);
        _cellCopies.Add((mod.ModKey, cell.FormKey), copy);
        return copy;
    }

    /// <summary>The plugin's copy of the base game's navmesh info map, with its preferred paths and without entries, made on first use.</summary>
    private NavigationMeshInfoMap MapIn(ISkyrimMod mod)
    {
        if (_mapCopies.TryGetValue(mod.ModKey, out var map)) return map;
        map = (NavigationMeshInfoMap)_map.DeepCopy();
        map.MapInfos.Clear();
        mod.NavigationMeshInfoMaps.Add(map);
        _mapCopies.Add(mod.ModKey, map);
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

    /// <summary>The cleaned mod's own object, in a cell of its own next to the base game's cells, which makes the worldspace a target space.</summary>
    private void PlaceObjectOfTheCleanedMod()
    {
        var ownCell = new Cell(_cleaned.NextKey(), FixtureMod.Release) { Grid = new CellGrid { Point = new P2Int(1, 0) } };
        var placed = new PlacedObject(_cleaned.NextKey(), FixtureMod.Release) { Placement = new Placement { Position = new P3Float(5000, 100, 0) } };
        placed.Base.SetTo(_cleaned.AddMarkerStatic("OwnStatic"));
        ownCell.Temporary.Add(placed);
        WorldspaceIn(_cleaned.Mod).AddCell(ownCell);
    }

    /// <param name="writeIntoPatch">What an earlier patcher of the group wrote into the patch; nothing when null.</param>
    /// <param name="afterCollect">Reads the plugin records the collector read, before they are dropped.</param>
    private CollectedObjects Collect(
        Action<ISkyrimMod>? writeIntoPatch = null, Action<IPluginRecords>? afterCollect = null, WhatToCollect? plan = null, int threads = 1)
    {
        var state = FixturePatcherState.Create([_base.Mod, _cleaned.Mod, _later.Mod], Path.GetTempPath(), Path.GetTempPath());
        writeIntoPatch?.Invoke(state.PatchMod);
        using var world = FixturePatcherState.CreateFinalWorld(state);
        var records = new PluginRecordsFactory(world, threads).Open();
        var trust = TestModTrust.Of(Cleaned, Patch, trusted: [BaseGame, Later], untrusted: []);
        var collected = PlacedObjects.Collect(records, trust, plan ?? Plan);
        afterCollect?.Invoke(records);
        return collected;
    }

    private NavmeshChangedByCleanedMod ChangedNavmeshOf(CollectedObjects collected) =>
        Assert.Single(collected.ChangedNavmeshes, changed => changed.Navmesh == NavmeshKey);
}
