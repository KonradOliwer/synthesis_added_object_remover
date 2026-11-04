using System.Numerics;
using AddedObjectRemover.Steps.RestoreWhereRemoved;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Tests.EndToEnd;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.LoadOrder;

/// <summary>
/// Which navmeshes go back where the cleaned mod was removed, read from in-memory plugins by the real reader and collector, planned
/// with every area cleared and written into the patch, whose output check must pass. The base game has a worldspace cell at grid
/// (6, 7), whose navmesh parents are stored Y first, with flat land and two square navmeshes side by side, West and East, linked over
/// their shared edge; a door leads from West's triangle 1, and the navmesh info map carries both entries and preferred paths through
/// both. The cleaned mod places an object of its own in a cell of its own, which makes the worldspace a target space.
/// </summary>
public sealed class NavmeshUndoPlanTests
{
    private const float Size = 100f;
    private const float HeightTolerance = 8f;
    private const int CellX = 6;
    private const int CellY = 7;
    private const int Side = TerrainGrid.VerticesPerSide;
    private const short DoorTriangle = 1;
    private const int EastSide = 1;
    private const int WestSide = 2;
    private const int CornerOffTheSharedEdge = 3;

    private static readonly WhatToCollect Plan = new(new ScanNeeds(AreaRecordVersions: new AreaRecordReads(new AreaPartTolerances(HeightTolerance, HeightTolerance), Cells: true, Lands: true)), SettingDefaults.MovedDistanceInUnits, ProcessReplaced: false);
    private static readonly PluginName Patch = FixturePatcherState.PatchModKey.ToPluginName();
    private static readonly Vector3 Origin = new(CellX * ExteriorGrid.CellSize, CellY * ExteriorGrid.CellSize, 0f);

    private readonly FixtureMod _base = new("Base.esm");
    private readonly FixtureMod _cleaned;
    private readonly FixtureMod _later;
    private readonly FormKey _worldspace;
    private readonly Cell _cell;
    private readonly NavigationMesh _west;
    private readonly NavigationMesh _east;
    private readonly PlacedObject _door;
    private readonly NavigationMeshInfoMap _map;
    private readonly Dictionary<ModKey, Worldspace> _worldspaceCopies = [];
    private readonly Dictionary<ModKey, Cell> _cellCopies = [];
    private readonly Dictionary<ModKey, NavigationMeshInfoMap> _mapCopies = [];

    public NavmeshUndoPlanTests()
    {
        _cleaned = new FixtureMod("Cleaned.esp", _base);
        _later = new FixtureMod("Later.esp", _base);
        _worldspace = _base.NextKey();
        _cell = new Cell(_base.NextKey(), FixtureMod.Release) { Grid = new CellGrid { Point = new P2Int(CellX, CellY) }, Landscape = FlatLand(_base.NextKey()) };
        _west = Square(_base.NextKey(), 0f);
        _east = Square(_base.NextKey(), Size);
        Link(_west, 0, EastSide, _east.FormKey, 1);
        Link(_east, 1, WestSide, _west.FormKey, 0);
        _door = new PlacedObject(_base.NextKey(), FixtureMod.Release) { Placement = new Placement { Position = ToP3(Origin + new Vector3(Size / 4, Size * 3 / 4, 0)) }, NavigationDoorLink = DoorLinkTo(_west.FormKey) };
        _door.Base.SetTo(_base.AddMarkerStatic("Door"));
        _west.Data!.DoorTriangles.Add(new DoorTriangle { TriangleBeforeDoor = DoorTriangle, Door = new FormLink<IPlacedObjectGetter>(_door.FormKey) });
        _cell.NavigationMeshes.Add(_west);
        _cell.NavigationMeshes.Add(_east);
        _cell.Temporary.Add(_door);
        WorldspaceIn(_base.Mod).AddCell(_cell);
        _map = new NavigationMeshInfoMap(_base.NextKey(), FixtureMod.Release) { PreferredPathing = PreferredPathsThrough(_west.FormKey, _east.FormKey) };
        _map.MapInfos.Add(Entry(_west.FormKey, Size / 2, _door.FormKey));
        _map.MapInfos.Add(Entry(_east.FormKey, Size * 3 / 2, door: null));
        _base.Mod.NavigationMeshInfoMaps.Add(_map);
        PlaceObjectOfTheCleanedMod();
    }

    private PluginName BaseGame => _base.Key.ToPluginName();

    private PluginName Cleaned => _cleaned.Key.ToPluginName();

    private PluginName Later => _later.Key.ToPluginName();

    private RecordKey WestKey => _west.FormKey.ToRecordKey();

    private RecordKey EastKey => _east.FormKey.ToRecordKey();

    private RecordKey DoorKey => _door.FormKey.ToRecordKey();

    [Fact]
    public void AnEditedNavmeshGetsItsGeometryAndEntryBackAndAnEditedNeighbourJoinsItsGroup()
    {
        Raise(NavmeshCopyIn(_cleaned.Mod, _west));
        Raise(NavmeshCopyIn(_cleaned.Mod, _east));
        MapIn(_cleaned.Mod).MapInfos.Add(Entry(_west.FormKey, Size, _door.FormKey));

        var (plan, patch) = Run();

        var group = Assert.Single(plan.Groups());
        Assert.NotNull(group.Write);
        Assert.Equal(new[] { WestKey, EastKey }, group.Members.ToArray());
        Assert.Equal([NavmeshUndo.Geometry, NavmeshUndo.Geometry], plan.Navmeshes().Select(navmesh => navmesh.Undo));
        Assert.All(WrittenNavmeshes(patch), navmesh => Assert.All(navmesh.Data!.Vertices, vertex => Assert.Equal(0f, vertex.Z)));
        Assert.Equal(Size / 2, Assert.Single(Assert.Single(patch.NavigationMeshInfoMaps).MapInfos).Point.X - Origin.X);
    }

    [Fact]
    public void ADeletedNavmeshComesBackWithItsEntryItsNeighboursEarlierLinkAndItsDoor()
    {
        DeleteWestAndUnlinkEastAndDoor();

        var (plan, patch) = Run();

        var group = Assert.Single(plan.Groups());
        Assert.NotNull(group.Write);
        Assert.Equal([NavmeshUndo.BroughtBack, NavmeshUndo.Geometry], plan.Navmeshes().Select(navmesh => navmesh.Undo));
        Assert.False(WrittenNavmesh(patch, _west).IsDeleted);
        Assert.Equal(_west.FormKey, Assert.Single(WrittenNavmesh(patch, _east).Data!.EdgeLinks).Mesh.FormKey);
        Assert.Equal(_west.FormKey, WrittenDoor(patch).NavigationDoorLink!.NavMesh.FormKey);
    }

    [Fact]
    public void AReplacedNavmeshComesBackWhileItsReplacementIsSunkAndTheNeighbourAndDoorAreRelinkedToIt()
    {
        var replacement = ReplaceWest(relinkEast: true);

        var (plan, patch) = Run();

        var group = Assert.Single(plan.Groups());
        Assert.NotNull(group.Write);
        Assert.Equal(new[] { WestKey, EastKey, replacement.ToRecordKey() }, group.Members.ToArray());
        Assert.All(WrittenNavmesh(patch, replacement).Data!.Vertices, vertex => Assert.Equal(RemovalConvention.SunkNavmeshZ, vertex.Z));
        Assert.Equal(_west.FormKey, Assert.Single(WrittenNavmesh(patch, _east).Data!.EdgeLinks).Mesh.FormKey);
        Assert.Equal(_west.FormKey, WrittenDoor(patch).NavigationDoorLink!.NavMesh.FormKey);
    }

    [Fact]
    public void AnAddedNavmeshIsSunkAndTheNavmeshItWasLinkedToGetsItsEarlierLinksBack()
    {
        var added = AddLinkedToEast(relinkEast: true);

        var (plan, patch) = Run();

        var group = Assert.Single(plan.Groups());
        Assert.NotNull(group.Write);
        Assert.Equal(new[] { EastKey, added.ToRecordKey() }, group.Members.ToArray());
        Assert.Empty(WrittenNavmesh(patch, added).Data!.EdgeLinks);
        Assert.Equal([_west.FormKey], WrittenNavmesh(patch, _east).Data!.EdgeLinks.Select(link => link.Mesh.FormKey));
    }

    [Theory]
    [InlineData("geometry")]
    [InlineData("presence")]
    [InlineData("entry")]
    [InlineData("door link")]
    public void AGroupALaterPluginChangedAgainIsRefusedAndLeavesEveryNavmeshEntryAndDoorLinkAsItIs(string changedLater)
    {
        var refusedBy = WestKey;
        switch (changedLater)
        {
            case "geometry":
                Raise(NavmeshCopyIn(_later.Mod, Raise(NavmeshCopyIn(_cleaned.Mod, _west))), corner: 0);
                break;
            case "presence":
                Deleted(NavmeshCopyIn(_cleaned.Mod, _west));
                NavmeshCopyIn(_later.Mod, _west);
                break;
            case "entry":
                NavmeshCopyIn(_cleaned.Mod, _west);
                MapIn(_cleaned.Mod).MapInfos.Add(Entry(_west.FormKey, Size, _door.FormKey));
                MapIn(_later.Mod).MapInfos.Add(Entry(_west.FormKey, Size * 2, _door.FormKey));
                break;
            default:
                Raise(NavmeshCopyIn(_cleaned.Mod, _west));
                DoorCopyIn(_cleaned.Mod).NavigationDoorLink = DoorLinkTo(_east.FormKey);
                DoorCopyIn(_later.Mod).NavigationDoorLink = null;
                refusedBy = DoorKey;
                break;
        }

        var (plan, patch) = Run();

        Assert.Equal(new NavmeshRefusal(NavmeshGuard.ChangedLater, refusedBy, Fault: null), Assert.Single(plan.Groups()).Refused);
        AssertNothingOfTheNavmeshesWritten(patch);
    }

    /// <summary>A later plugin edits West again after the cleaned mod: the fact carries the later version, which is the one the game shows.</summary>
    [Fact]
    public void ANavmeshALaterPluginEditedAgainCarriesTheLaterVersionAsItsWinner()
    {
        Raise(NavmeshCopyIn(_later.Mod, Raise(NavmeshCopyIn(_cleaned.Mod, _west))), corner: 0);
        NavmeshShape? laterWinner = null;

        Run(then: (plan, _, collected) =>
        {
            laterWinner = collected.ChangedNavmeshes.Single(navmesh => navmesh.Navmesh == WestKey).LaterWinner;
            return plan;
        });

        var winner = Assert.IsType<NavmeshShape>(laterWinner);
        Assert.Equal((Later, Size / 4), (winner.Plugin, winner.Vertices[0].Z));
    }

    [Fact]
    public void ANavmeshALaterPluginDeletesAgainStaysDeleted()
    {
        Raise(NavmeshCopyIn(_cleaned.Mod, _west));
        Deleted(NavmeshCopyIn(_later.Mod, _west));

        var (plan, patch) = Run();

        Assert.Equal(new NavmeshRefusal(NavmeshGuard.ChangedLater, WestKey, Fault: null), Assert.Single(plan.Groups()).Refused);
        AssertNothingOfTheNavmeshesWritten(patch);
    }

    [Fact]
    public void ALaterPluginsNeighbourLinkedToTheReplacementRefusesTheGroup()
    {
        var replacement = ReplaceWest(relinkEast: false);
        var relinked = NavmeshCopyIn(_later.Mod, _east);
        relinked.Data!.EdgeLinks[0].Mesh = new FormLink<INavigationMeshGetter>(replacement);

        var (plan, patch) = Run();

        Assert.Equal(new NavmeshRefusal(NavmeshGuard.EdgeLinkUnmatched, EastKey, Fault: null), Assert.Single(plan.Groups()).Refused);
        AssertNothingOfTheNavmeshesWritten(patch);
    }

    [Fact]
    public void ANeighbourThatStillLinksToTheEarlierNavmeshFitsWithoutBeingWritten()
    {
        var replacement = ReplaceWest(relinkEast: false);

        var (plan, patch) = Run();

        var group = Assert.Single(plan.Groups());
        Assert.NotNull(group.Write);
        Assert.Equal(new[] { WestKey, replacement.ToRecordKey() }, group.Members.ToArray());
        Assert.DoesNotContain(WrittenNavmeshes(patch), navmesh => navmesh.FormKey == _east.FormKey);
    }

    [Fact]
    public void AnEntryThatStaysMergedIntoASunkNavmeshRefusesItsGroup()
    {
        var added = AddLinkedToEast(relinkEast: false);
        var merged = Entry(_east.FormKey, Size * 3 / 2, door: null);
        merged.MergedTo.Add(new FormLink<INavigationMeshGetter>(added));
        MapIn(_later.Mod).MapInfos.Add(merged);

        var (plan, _) = Run();

        Assert.Equal(new NavmeshRefusal(NavmeshGuard.MapEntry, EastKey, Fault: null), Assert.Single(plan.Groups()).Refused);
    }

    [Fact]
    public void ARemovedDoorTheRestoredNavmeshNamesRefusesItsGroup()
    {
        Raise(NavmeshCopyIn(_cleaned.Mod, _west));

        var (plan, _) = Run(stands: record => record != DoorKey);

        Assert.Equal(new NavmeshRefusal(NavmeshGuard.Door, DoorKey, Fault: null), Assert.Single(plan.Groups()).Refused);
    }

    [Fact]
    public void ADoorLinkedToASunkNavmeshRefusesItsGroup()
    {
        var added = AddLinkedToEast(relinkEast: true);
        var ownDoor = new PlacedObject(_cleaned.NextKey(), FixtureMod.Release) { Placement = new Placement { Position = ToP3(Origin + new Vector3(Size * 5 / 2, Size / 2, 0)) }, NavigationDoorLink = DoorLinkTo(added) };
        ownDoor.Base.SetTo(_cleaned.AddMarkerStatic("OwnDoor"));
        CellCopyIn(_cleaned.Mod).Temporary.Add(ownDoor);
        WrittenIn(_cleaned.Mod, added).Data!.DoorTriangles.Add(new DoorTriangle { TriangleBeforeDoor = DoorTriangle, Door = new FormLink<IPlacedObjectGetter>(ownDoor.FormKey) });

        var (plan, _) = Run();

        Assert.Equal(new NavmeshRefusal(NavmeshGuard.Door, ownDoor.FormKey.ToRecordKey(), Fault: null), Assert.Single(plan.Groups()).Refused);
    }

    [Fact]
    public void AnAddedNavmeshIsSunkWithItsOwnRemovedDoorsWhoseLinksAreCleared()
    {
        var (added, ownDoors) = AddWithOwnDoors();

        var (plan, patch) = Run(stands: record => !ownDoors.Contains(record));

        var group = Assert.Single(plan.Groups());
        Assert.NotNull(group.Write);
        Assert.Equal([NavmeshUndo.Sunk], plan.Navmeshes().Select(navmesh => navmesh.Undo));
        Assert.Empty(WrittenNavmesh(patch, added).Data!.DoorTriangles);
        Assert.All(ownDoors, door =>
        {
            var written = Assert.Single(patch.EnumerateMajorRecords<IPlacedObjectGetter>(), placed => placed.FormKey == door.ToFormKey());
            Assert.Null(written.NavigationDoorLink);
            Assert.True(written.IsInitiallyDisabled());
        });
    }

    [Fact]
    public void AStandingDoorOfAnotherModLinkedToTheAddedNavmeshRefusesItsGroupAndTheRemovedOwnDoorsKeepTheirLinks()
    {
        var (added, ownDoors) = AddWithOwnDoors();
        var laterDoor = AddDoor(_later, added, triangle: 0, "LaterDoor");

        var (plan, patch) = Run(stands: record => !ownDoors.Contains(record));

        Assert.Equal(new NavmeshRefusal(NavmeshGuard.Door, laterDoor, Fault: null), Assert.Single(plan.Groups()).Refused);
        Assert.All(ownDoors, door =>
        {
            var written = Assert.Single(patch.EnumerateMajorRecords<IPlacedObjectGetter>(), placed => placed.FormKey == door.ToFormKey());
            Assert.True(written.IsInitiallyDisabled());
            Assert.Equal(added, written.NavigationDoorLink?.NavMesh.FormKey);
        });
    }

    /// <summary>A plugin after the cleaned mod deletes the cell holding West, so West is never written into it.</summary>
    [Fact]
    public void ANavmeshInACellALaterPluginDeletesIsNotRestored()
    {
        Raise(NavmeshCopyIn(_cleaned.Mod, _west));
        CellCopyIn(_later.Mod).IsDeleted = true;

        var (plan, patch) = Run();

        Assert.Equal(new NavmeshRefusal(NavmeshGuard.DidNotExistBefore, _cell.FormKey.ToRecordKey(), Fault: null), Assert.Single(plan.Groups()).Refused);
        AssertNothingOfTheNavmeshesWritten(patch);
    }

    /// <summary>
    /// The cleaned mod disabled the door West's door triangle names and edited West; the door stays disabled, so the group that would
    /// bring West's door triangle back is refused, naming the door.
    /// </summary>
    [Fact]
    public void ANavmeshNamingADoorTheCleanedModDisabledIsRefusedWhileTheDoorStaysDisabled()
    {
        Raise(NavmeshCopyIn(_cleaned.Mod, _west));
        DoorCopyIn(_cleaned.Mod).MajorRecordFlagsRaw |= (int)SkyrimMajorRecord.SkyrimMajorRecordFlag.InitiallyDisabled;

        var (plan, _) = Run();

        Assert.Equal(new NavmeshRefusal(NavmeshGuard.Door, DoorKey, Fault: null), Assert.Single(plan.Groups()).Refused);
    }

    [Fact]
    public void ADoorWhoseLinkNamesAnotherTriangleThanTheRestoredDoorTriangleRefusesItsGroup()
    {
        Raise(NavmeshCopyIn(_cleaned.Mod, _west));
        DoorCopyIn(_later.Mod).NavigationDoorLink = new NavigationDoorLink { NavMesh = new FormLink<INavigationMeshGetter>(_west.FormKey), TeleportMarkerTriangle = 0 };

        var (plan, _) = Run();

        Assert.Equal(new NavmeshRefusal(NavmeshGuard.Door, DoorKey, Fault: null), Assert.Single(plan.Groups()).Refused);
    }

    [Theory]
    [InlineData(10f, true)]
    [InlineData(4f, false)]
    public void GroundALaterPluginRaisedUnderTheBroughtBackNavmeshRefusesItsGroup(float raisedBy, bool refused)
    {
        DeleteWestAndUnlinkEastAndDoor();
        var raised = (Landscape)_cell.Landscape!.DeepCopy();
        raised.VertexHeightMap!.Offset = raisedBy / TerrainGrid.UnitsPerHeightStep;
        CellCopyIn(_later.Mod).Landscape = raised;

        var (plan, _) = Run(then: (plan, records, _) =>
            plan.RefuseOnGround(records.FindLandWorldspace, cell => new LandHeights(records.ReadTerrainBelow(Cleaned, cell), records.ReadTerrain(cell)), HeightTolerance));

        Assert.Equal(refused ? new NavmeshRefusal(NavmeshGuard.Ground, WestKey, Fault: null) : null, Assert.Single(plan.Groups()).Refused);
    }

    [Fact]
    public void AnObjectThatStaysInTheBroughtBackNavmeshRefusesItsGroupAndOneBesideItDoesNot()
    {
        DeleteWestAndUnlinkEastAndDoor();
        var inside = new RecordKey(Later, 0x900);
        var beside = new RecordKey(Later, 0x901);

        var (plan, _) = Run(then: (plan, _, _) => plan.RefuseOnObjects(
            [
                new StandingBox(inside, _worldspace.ToRecordKey(), BoxAt(Origin + new Vector3(Size / 4, Size * 3 / 4, 0))),
                new StandingBox(beside, _worldspace.ToRecordKey(), BoxAt(Origin + new Vector3(Size * 3, Size / 2, 0))),
            ],
            plan.IndexOfTrianglesGoingBack()));

        Assert.Equal(new NavmeshRefusal(NavmeshGuard.Object, inside, Fault: null), Assert.Single(plan.Groups()).Refused);
    }

    [Fact]
    public void AWaterLevelThatStaysChangedInTheCellOfTheBroughtBackNavmeshRefusesItsGroup()
    {
        DeleteWestAndUnlinkEastAndDoor();

        var (plan, _) = Run(then: (plan, _, _) => plan.RefuseOnWater(cell => cell != _cell.FormKey.ToRecordKey()));

        Assert.Equal(new NavmeshRefusal(NavmeshGuard.Water, _cell.FormKey.ToRecordKey(), Fault: null), Assert.Single(plan.Groups()).Refused);
    }

    [Fact]
    public void ANavmeshDeletedBeforeTheCleanedModIsNotBroughtBack()
    {
        _west.IsDeleted = true;
        Raise(NavmeshCopyIn(_cleaned.Mod, _west)).IsDeleted = false;

        var (plan, _) = Run();

        Assert.Equal(new NavmeshRefusal(NavmeshGuard.DidNotExistBefore, WestKey, Fault: null), Assert.Single(plan.Groups()).Refused);
    }

    [Fact]
    public void AnEditedNavmeshTheCleanedModMovedIntoAnotherCellIsNotRestored()
    {
        var moved = Raise((NavigationMesh)_west.DeepCopy());
        var otherCell = new Cell(_cleaned.NextKey(), FixtureMod.Release) { Grid = new CellGrid { Point = new P2Int(CellX + 2, CellY + 3) } };
        otherCell.NavigationMeshes.Add(moved);
        WorldspaceIn(_cleaned.Mod).AddCell(otherCell);

        var (plan, _) = Run();

        Assert.Equal(new NavmeshRefusal(NavmeshGuard.DidNotExistBefore, WestKey, Fault: null), Assert.Single(plan.Groups()).Refused);
    }

    [Fact]
    public void AReplacementOutsideTheGroupKeepsTheDeletedNavmeshDeleted()
    {
        var replacement = ReplaceWest(relinkEast: false, linkReplacementToEast: false);

        var (plan, _) = Run(verdictOf: navmesh => navmesh == replacement.ToRecordKey() ? AreaVerdict.StillStanding : AreaVerdict.Cleared);

        Assert.Equal(new NavmeshRefusal(NavmeshGuard.DidNotExistBefore, replacement.ToRecordKey(), Fault: null), Assert.Single(plan.Groups()).Refused);
    }

    [Fact]
    public void ADeletedNavmeshWithoutAnEntryBeforeTheCleanedModIsNotBroughtBack()
    {
        _map.MapInfos.RemoveAll(entry => entry.NavigationMesh.FormKey == _west.FormKey);
        Deleted(NavmeshCopyIn(_cleaned.Mod, _west));

        var (plan, _) = Run();

        Assert.Equal(new NavmeshRefusal(NavmeshGuard.DidNotExistBefore, WestKey, Fault: null), Assert.Single(plan.Groups()).Refused);
    }

    [Fact]
    public void ANavmeshInfoMapOfAPluginAfterThePatchRefusesEveryGroup()
    {
        Raise(NavmeshCopyIn(_cleaned.Mod, _west));
        MapIn(_later.Mod).MapInfos.Add(Entry(_east.FormKey, Size * 3 / 2, door: null));

        var (plan, _) = Run(pluginList: [BaseGame, Cleaned, Patch, Later]);

        Assert.Equal(new NavmeshRefusal(NavmeshGuard.PreferredPaths, WestKey, Fault: null), Assert.Single(plan.Groups()).Refused);
    }

    [Fact]
    public void PreferredPathsThroughANavmeshWhosePreferredTrianglesWouldChangeRefuseItsGroup()
    {
        Raise(NavmeshCopyIn(_cleaned.Mod, _west)).Data!.Triangles[0].Flags |= NavmeshTriangle.Flag.Preferred;

        var (plan, _) = Run();

        Assert.Equal(new NavmeshRefusal(NavmeshGuard.PreferredPaths, WestKey, Fault: null), Assert.Single(plan.Groups()).Refused);
    }

    [Fact]
    public void InconsistentDataBeforeTheCleanedModRefusesTheGroupWithTheChecksFault()
    {
        _west.Data!.Max = new P3Float(_west.Data.Max.X - Size / 2, _west.Data.Max.Y, _west.Data.Max.Z);
        Raise(NavmeshCopyIn(_cleaned.Mod, _west));

        var (plan, patch) = Run();

        var refused = Assert.Single(plan.Groups()).Refused!;
        Assert.Equal((NavmeshGuard.EarlierDataInconsistent, WestKey, NavmeshRule.Spatial), (refused.Guard, refused.By, refused.Fault!.Rule));
        AssertNothingOfTheNavmeshesWritten(patch);
    }

    [Fact]
    public void AnInconsistentNeighbourThatStaysRefusesTheGroupWithTheChecksFault()
    {
        _east.Data!.Max = new P3Float(_east.Data.Max.X, _east.Data.Max.Y - Size / 2, _east.Data.Max.Z);
        Raise(NavmeshCopyIn(_cleaned.Mod, _west));

        var (plan, _) = Run();

        var refused = Assert.Single(plan.Groups()).Refused!;
        Assert.Equal((NavmeshGuard.DataAsItStandsInconsistent, EastKey, NavmeshRule.Spatial), (refused.Guard, refused.By, refused.Fault!.Rule));
    }

    [Fact]
    public void ThePlanDoesNotDependOnTheThreadCount()
    {
        ReplaceWest(relinkEast: true);

        Assert.Equal(Describe(Run(threads: 1).Plan), Describe(Run(threads: 8).Plan));
    }

    private static string Describe(NavmeshUndoPlan plan) => string.Join(
        Environment.NewLine,
        plan.Navmeshes().Select(navmesh => $"{navmesh.Change.Navmesh} {navmesh.Undo}")
            .Concat(plan.Groups().Select(group => $"{string.Join(",", group.Members)} {group.Refused} {NavmeshWriteDescription.Of(group.Write)}")));

    /// <param name="verdictOf">The verdict of each navmesh's area; every area is cleared when null.</param>
    /// <param name="stands">Whether a placed record stands in the final world; every one does when null. A target that does not is removed in the patch.</param>
    /// <param name="pluginList">The game's plugin list; Base, Cleaned, Later and the patch, in this order, when null.</param>
    /// <param name="then">The guards against the other kinds' plans, run on the plan before it is written.</param>
    private (NavmeshUndoPlan Plan, ISkyrimModGetter Patch) Run(
        int threads = 1,
        Func<RecordKey, AreaVerdict>? verdictOf = null,
        Func<RecordKey, bool>? stands = null,
        PluginName[]? pluginList = null,
        Func<NavmeshUndoPlan, IPluginRecords, CollectedObjects, NavmeshUndoPlan>? then = null)
    {
        var state = FixturePatcherState.Create([_base.Mod, _cleaned.Mod, _later.Mod], Path.GetTempPath(), Path.GetTempPath());
        using var world = FixturePatcherState.CreateFinalWorld(state);
        var records = new PluginRecordsFactory(world, threads).Open();
        var collected = PlacedObjects.Collect(records, TestModTrust.Of(Cleaned, Patch, trusted: [BaseGame, Later], untrusted: []), Plan);
        var writing = new PatchWriting(new SynthesisWriteCache(state.LinkCache), state.PatchMod, Program.PatchMarkerText);
        PluginName[] list = pluginList ?? [BaseGame, Cleaned, Later, Patch];
        var plugins = TestLoadOrderPlugins.Of(new KnownPluginNames(list), Patch, [.. list.Select(plugin => new PluginListing(plugin, Loaded: true, []))], list, []);
        var judged = collected.ChangedNavmeshes
            .Select(navmesh => (Navmesh: navmesh, Verdict: verdictOf?.Invoke(navmesh.Navmesh) ?? AreaVerdict.Cleared))
            .Select(entry => new NavmeshChangeJudged(
                entry.Navmesh, Never: null, TestAreaShares.Of(entry.Verdict), Undo: null, ChangeJudge.VerdictKeeping([entry.Verdict]), NothingGoesBack: false))
            .ToList();
        var changed = collected.ChangedNavmeshes.ToDictionary(navmesh => navmesh.Navmesh);
        var changedRecords = collected.ChangedRecords.ToDictionary(record => record.Key);
        var removedByRun = collected.Targets.Select(target => target.Key)
            .Concat(collected.ChangedNavmeshes.SelectMany(navmesh => navmesh.Doors).Select(door => door.Door))
            .Where(record => stands?.Invoke(record) == false)
            .ToHashSet();
        var doors = new DoorLinks(collected.ChangedRecords, []);
        var doorsAsTheyEnd = new DoorsAsTheyEnd(FinalPresence.Of(collected, removedByRun, []), doors, _ => true);
        var plan = new NavmeshUndoPlan(
            judged,
            sunk => new NavmeshGroupGuards(changed, sunk, changedRecords, removedByRun.Contains, doors, doorsAsTheyEnd, plugins, Cleaned),
            PatchWriter.CreateNavmeshCheck(records, writing).FindFaultIfWritten);
        plan = then?.Invoke(plan, records, collected) ?? plan;
        var protection = ObjectsToKeep.Build(collected.Targets, [], TestTargets.References(collected.Targets.Length), TestBaseFacts.Unresolved);
        int[] removed = [.. collected.Targets.Where(target => stands?.Invoke(target.Key) == false).Select(target => target.Id.Index)];
        var final = removed.Length == 0
            ? TestSeededDecisions.StartDisablingAll(protection, collected.Targets.Length)
            : TestSeededDecisions.Seed(protection, collected.Targets.Length, removed);
        var result = new RestoreWhereRemovedResult(StepOutcome.Performed, [], [], [], plan.Navmeshes(), plan.Groups(), Passes: 1, DoorsLeftRemoved: [], Failures: [], RestoreWhereRemovedTimes.None);
        PatchWriter.Write(new(final, collected.Targets, OtherModsObjectsResult.SwitchedOff, result, records, writing, Cleaned));
        return (plan, state.PatchMod);
    }

    /// <summary>The cleaned mod deletes West, and unlinks East and the door from it.</summary>
    private void DeleteWestAndUnlinkEastAndDoor()
    {
        Deleted(NavmeshCopyIn(_cleaned.Mod, _west));
        Unlink(NavmeshCopyIn(_cleaned.Mod, _east), triangle: 1, WestSide);
        DoorCopyIn(_cleaned.Mod).NavigationDoorLink = null;
    }

    /// <summary>
    /// The cleaned mod deletes West and adds a replacement in its place, with the door relinked to it and its own map entry; the
    /// replacement links to East, and East to it, as asked.
    /// </summary>
    private FormKey ReplaceWest(bool relinkEast, bool linkReplacementToEast = true)
    {
        var replacement = Square(_cleaned.NextKey(), 0f);
        if (linkReplacementToEast) Link(replacement, 0, EastSide, _east.FormKey, 1);
        replacement.Data!.DoorTriangles.Add(new DoorTriangle { TriangleBeforeDoor = DoorTriangle, Door = new FormLink<IPlacedObjectGetter>(_door.FormKey) });
        CellCopyIn(_cleaned.Mod).NavigationMeshes.Add(replacement);
        Deleted(NavmeshCopyIn(_cleaned.Mod, _west));
        DoorCopyIn(_cleaned.Mod).NavigationDoorLink = DoorLinkTo(replacement.FormKey);
        MapIn(_cleaned.Mod).MapInfos.Add(Entry(replacement.FormKey, Size / 2, _door.FormKey));
        if (relinkEast) NavmeshCopyIn(_cleaned.Mod, _east).Data!.EdgeLinks[0].Mesh = new FormLink<INavigationMeshGetter>(replacement.FormKey);
        return replacement.FormKey;
    }

    /// <summary>The cleaned mod adds a navmesh east of East, linked to it, with its own map entry; East links to it as asked.</summary>
    private FormKey AddLinkedToEast(bool relinkEast)
    {
        var added = Square(_cleaned.NextKey(), Size * 2);
        Link(added, 1, WestSide, _east.FormKey, 0);
        CellCopyIn(_cleaned.Mod).NavigationMeshes.Add(added);
        MapIn(_cleaned.Mod).MapInfos.Add(Entry(added.FormKey, Size * 5 / 2, door: null));
        if (relinkEast) Link(NavmeshCopyIn(_cleaned.Mod, _east), 0, EastSide, added.FormKey, 1);
        return added.FormKey;
    }

    /// <summary>
    /// The cleaned mod adds a navmesh east of East, linked to nothing, with its own map entry and a door of its own on each triangle,
    /// each door linked back to its triangle.
    /// </summary>
    private (FormKey Added, RecordKey[] OwnDoors) AddWithOwnDoors()
    {
        var added = Square(_cleaned.NextKey(), Size * 2);
        CellCopyIn(_cleaned.Mod).NavigationMeshes.Add(added);
        MapIn(_cleaned.Mod).MapInfos.Add(Entry(added.FormKey, Size * 5 / 2, door: null));
        RecordKey[] ownDoors = [AddDoor(_cleaned, added.FormKey, triangle: 0, "OwnDoorA"), AddDoor(_cleaned, added.FormKey, triangle: 1, "OwnDoorB")];
        for (short triangle = 0; triangle < ownDoors.Length; triangle++)
            added.Data!.DoorTriangles.Add(new DoorTriangle { TriangleBeforeDoor = triangle, Door = new FormLink<IPlacedObjectGetter>(ownDoors[triangle].ToFormKey()) });
        return (added.FormKey, ownDoors);
    }

    /// <summary>A door of the plugin's own in its copy of the base game's cell, over the added navmesh, linked to the given triangle.</summary>
    private RecordKey AddDoor(FixtureMod mod, FormKey navmesh, short triangle, string name)
    {
        var door = new PlacedObject(mod.NextKey(), FixtureMod.Release)
        {
            Placement = new Placement { Position = ToP3(Origin + new Vector3(Size * 5 / 2, Size / 2, 0)) },
            NavigationDoorLink = new NavigationDoorLink { NavMesh = new FormLink<INavigationMeshGetter>(navmesh), TeleportMarkerTriangle = triangle },
        };
        door.Base.SetTo(mod.AddMarkerStatic(name));
        CellCopyIn(mod.Mod).Temporary.Add(door);
        return door.FormKey.ToRecordKey();
    }

    private static void AssertNothingOfTheNavmeshesWritten(ISkyrimModGetter patch)
    {
        Assert.Empty(WrittenNavmeshes(patch));
        Assert.Empty(patch.NavigationMeshInfoMaps);
        Assert.Empty(patch.EnumerateMajorRecords<IPlacedObjectGetter>());
    }

    private static List<INavigationMeshGetter> WrittenNavmeshes(ISkyrimModGetter patch) => [.. patch.EnumerateMajorRecords<INavigationMeshGetter>()];

    private static INavigationMeshGetter WrittenNavmesh(ISkyrimModGetter patch, NavigationMesh navmesh) => WrittenNavmesh(patch, navmesh.FormKey);

    private static INavigationMeshGetter WrittenNavmesh(ISkyrimModGetter patch, FormKey navmesh) => Assert.Single(WrittenNavmeshes(patch), written => written.FormKey == navmesh);

    private IPlacedObjectGetter WrittenDoor(ISkyrimModGetter patch) => Assert.Single(patch.EnumerateMajorRecords<IPlacedObjectGetter>(), placed => placed.FormKey == _door.FormKey);

    private static OrientedBox BoxAt(Vector3 centre) => new(centre, Mat3.Identity, new Vector3(Size / 10));

    private NavigationMesh WrittenIn(ISkyrimMod mod, FormKey navmesh) => (NavigationMesh)CellCopyIn(mod).NavigationMeshes.Single(written => written.FormKey == navmesh);

    /// <summary>Lifts one corner (by default one off the shared edge) and the bounds' top with it.</summary>
    private static NavigationMesh Raise(NavigationMesh navmesh, int corner = CornerOffTheSharedEdge)
    {
        var data = navmesh.Data!;
        data.Vertices[corner] = new P3Float(data.Vertices[corner].X, data.Vertices[corner].Y, Size / 4 + corner);
        data.Max = new P3Float(data.Max.X, data.Max.Y, Math.Max(data.Max.Z, data.Vertices[corner].Z));
        return navmesh;
    }

    private static NavigationMesh Deleted(NavigationMesh navmesh)
    {
        navmesh.IsDeleted = true;
        navmesh.Data = null;
        return navmesh;
    }

    private static void Unlink(NavigationMesh navmesh, int triangle, int edge)
    {
        var side = NavmeshTriangleEdges.All[edge];
        side.SetValue(navmesh.Data!.Triangles[triangle], NavmeshTriangleEdges.None);
        navmesh.Data.Triangles[triangle].Flags &= ~side.Flag;
        navmesh.Data.EdgeLinks.Clear();
    }

    /// <summary>
    /// A square from x to x + <see cref="Size"/> in the base game's cell: triangle 0 runs 0-1-2 (edge 1 is the east side), triangle 1
    /// runs 0-2-3 (edge 2 is the west side); the diagonal joins them.
    /// </summary>
    private NavigationMesh Square(FormKey key, float x) => new(key, FixtureMod.Release)
    {
        Data = new NavigationMeshData
        {
            Parent = new WorldspaceNavmeshParent { Parent = new FormLink<IWorldspaceGetter>(_worldspace), Coordinates = StoredParentCoordinates },
            Vertices = { Corner(x, 0), Corner(x + Size, 0), Corner(x + Size, Size), Corner(x, Size) },
            Triangles = { Triangle(0, 1, 2, nextTriangle: 1, edge: 2), Triangle(0, 2, 3, nextTriangle: 0, edge: 0) },
            Min = Corner(x, 0),
            Max = Corner(x + Size, Size),
        },
    };

    private static P3Float Corner(float x, float y) => ToP3(Origin + new Vector3(x, y, 0));

    private static P3Float ToP3(Vector3 point) => new(point.X, point.Y, point.Z);

    /// <summary>The cell's grid point as the game stores a navmesh parent's coordinates: Y first.</summary>
    private static P2Int16 StoredParentCoordinates => new(CellY, CellX);

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

    private NavigationMapInfo Entry(FormKey navmesh, float pointX, FormKey? door)
    {
        var entry = new NavigationMapInfo
        {
            NavigationMesh = new FormLink<INavigationMeshGetter>(navmesh),
            Parent = new NavigationMapInfoWorldParent { ParentWorldspace = new FormLink<IWorldspaceGetter>(_worldspace), ParentWorldspaceCoord = StoredParentCoordinates },
            Point = Corner(pointX, Size / 2),
        };
        if (door is { } linked) entry.LinkedDoors.Add(new LinkedDoor { Door = new FormLink<IPlacedObjectGetter>(linked) });
        return entry;
    }

    private static NavigationDoorLink DoorLinkTo(FormKey navmesh) => new() { NavMesh = new FormLink<INavigationMeshGetter>(navmesh), TeleportMarkerTriangle = DoorTriangle };

    private static PreferredPathing PreferredPathsThrough(params FormKey[] navmeshes)
    {
        var set = new NavmeshSet();
        set.Navmeshes.AddRange(navmeshes.Select(navmesh => new FormLink<INavigationMeshGetter>(navmesh)));
        return new PreferredPathing { NavmeshSets = { set } };
    }

    private static Landscape FlatLand(FormKey key) => new(key, FixtureMod.Release)
    {
        VertexHeightMap = new LandscapeVertexHeightMap { Offset = 0f, HeightMap = new Array2d<sbyte>(new P2Int(Side, Side), default) },
        VertexNormals = new Array2d<P3UInt8>(new P2Int(Side, Side), default),
        VertexColors = new Array2d<P3UInt8>(new P2Int(Side, Side), default),
    };

    /// <summary>The plugin's copy of a base game navmesh, in its copy of the cell, made from the given version on first use.</summary>
    private NavigationMesh NavmeshCopyIn(ISkyrimMod mod, NavigationMesh from)
    {
        var cell = CellCopyIn(mod);
        if (cell.NavigationMeshes.FirstOrDefault(navmesh => navmesh.FormKey == from.FormKey) is NavigationMesh existing) return existing;
        var copy = (NavigationMesh)from.DeepCopy();
        cell.NavigationMeshes.Add(copy);
        return copy;
    }

    /// <summary>The plugin's copy of the base game's door, in its copy of the cell, made on first use.</summary>
    private PlacedObject DoorCopyIn(ISkyrimMod mod)
    {
        var cell = CellCopyIn(mod);
        if (cell.Temporary.FirstOrDefault(placed => placed.FormKey == _door.FormKey) is PlacedObject existing) return existing;
        var copy = (PlacedObject)_door.DeepCopy();
        cell.Temporary.Add(copy);
        return copy;
    }

    /// <summary>The plugin's copy of the base game's cell, without its land, navmeshes and placed records, made on first use.</summary>
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

    /// <summary>The cleaned mod's own object, in a cell of its own, which makes the worldspace a target space.</summary>
    private void PlaceObjectOfTheCleanedMod()
    {
        var ownCell = new Cell(_cleaned.NextKey(), FixtureMod.Release) { Grid = new CellGrid { Point = new P2Int(2, 3) } };
        var placed = new PlacedObject(_cleaned.NextKey(), FixtureMod.Release) { Placement = new Placement { Position = new P3Float(2 * ExteriorGrid.CellSize + 100, 3 * ExteriorGrid.CellSize + 100, 0) } };
        placed.Base.SetTo(_cleaned.AddMarkerStatic("OwnStatic"));
        ownCell.Temporary.Add(placed);
        WorldspaceIn(_cleaned.Mod).AddCell(ownCell);
    }
}
