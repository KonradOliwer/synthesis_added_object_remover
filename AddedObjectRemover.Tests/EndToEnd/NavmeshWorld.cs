using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>What the cleaned mod did to the navmeshes of the world <see cref="NavmeshWorld"/> builds.</summary>
internal enum NavmeshScenario
{
    /// <summary>It deleted the west navmesh and cut the east one's link to it.</summary>
    DeletedWithoutReplacement,

    /// <summary>
    /// As <see cref="DeletedWithoutReplacement"/>, and its navmesh info map carries the west navmesh's entry with the point raised. A
    /// plugin's map holds only the entries it changes and the game merges them per navmesh, so a plugin can change an entry but not drop it.
    /// </summary>
    DeletedWithItsMapEntryChanged,

    /// <summary>
    /// It deleted the west navmesh and added another one in its place, linked to the east one; the door of the base game that the west
    /// navmesh named it linked to the new one instead.
    /// </summary>
    Replaced,

    /// <summary>It lifted a corner of the west navmesh; the city mod lifted another corner again.</summary>
    EditedAgainByTheCityMod,

    /// <summary>
    /// As <see cref="DeletedWithoutReplacement"/>, but its only object in the worldspace is a crate that starts disabled, as quest
    /// content does; its crate with the city pad beside it stands in an interior.
    /// </summary>
    DeletedWhereItsOnlyObjectStartsDisabled,
}

/// <summary>
/// An exterior cell at grid (6, 7) of its own worldspace, with flat land and two square navmeshes side by side, West and East, linked over
/// their shared edge, and a navmesh info map with an entry for each and preferred paths through both. The cleaned mod's crate stands
/// 1000 units away from them with a city pad beside it, so the crate is removed and the area around the navmeshes counts as cleared.
/// </summary>
internal sealed class NavmeshWorld
{
    public const string BaseGamePlugin = "Skyrim.esm";
    public const string CityPlugin = "CityMod.esp";

    private const float Size = 100f;
    private const int CellX = 6;
    private const int CellY = 7;
    private const int Side = TerrainGrid.VerticesPerSide;
    private const int EastSide = 1;
    private const int WestSide = 2;
    private const int CornerOffTheSharedEdge = 3;
    private const int OtherCorner = 2;
    private const short DoorTriangleIndex = 1;
    private const float CrateOffset = 1000f;
    private const float PadDistance = 75f;

    private static readonly Vector3 Origin = new(CellX * ExteriorGrid.CellSize, CellY * ExteriorGrid.CellSize, 0f);

    private readonly FixtureMod _baseGame = new(BaseGamePlugin);
    private readonly FixtureMod _target;
    private readonly FixtureMod _city;
    private readonly FormKey _worldspace;
    private readonly Cell _cell;
    private readonly NavigationMesh _west;
    private readonly NavigationMesh _east;
    private readonly NavigationMeshInfoMap _map;
    private readonly Dictionary<ModKey, Worldspace> _worldspaceCopies = [];
    private readonly Dictionary<ModKey, Cell> _cellCopies = [];
    private readonly Dictionary<ModKey, NavigationMeshInfoMap> _mapCopies = [];

    private NavmeshWorld()
    {
        _target = new FixtureMod(FixtureWorld.TargetName, _baseGame);
        _city = new FixtureMod(CityPlugin, _baseGame);
        _worldspace = _baseGame.NextKey();
        _cell = new Cell(_baseGame.NextKey(), FixtureMod.Release) { Grid = new CellGrid { Point = new P2Int(CellX, CellY) }, Landscape = FlatLand(_baseGame.NextKey()) };
        _west = Square(_baseGame.NextKey(), 0f);
        _east = Square(_baseGame.NextKey(), Size);
        Link(_west, 0, EastSide, _east.FormKey, 1);
        Link(_east, 1, WestSide, _west.FormKey, 0);
        _cell.NavigationMeshes.Add(_west);
        _cell.NavigationMeshes.Add(_east);
        WorldspaceIn(_baseGame.Mod).AddCell(_cell);
        _map = new NavigationMeshInfoMap(_baseGame.NextKey(), FixtureMod.Release) { PreferredPathing = PreferredPathsThrough(_west.FormKey, _east.FormKey) };
        _map.MapInfos.Add(Entry(_west.FormKey, Size / 2));
        _map.MapInfos.Add(Entry(_east.FormKey, Size * 3 / 2));
        _baseGame.Mod.NavigationMeshInfoMaps.Add(_map);
    }

    /// <summary>Builds the mods, lowest priority first, and writes their meshes under the Data folder.</summary>
    public static IReadOnlyList<ISkyrimMod> Create(string dataFolder, NavmeshScenario scenario)
    {
        FixtureMeshes.WriteAll(dataFolder);
        var world = new NavmeshWorld();
        if (scenario == NavmeshScenario.DeletedWhereItsOnlyObjectStartsDisabled) world.PlaceADisabledCrateHereAndTheCrateWithItsPadInAnInterior();
        else world.PlaceTheCleanedModsCrateWithACityPadBesideIt();
        switch (scenario)
        {
            case NavmeshScenario.DeletedWithoutReplacement or NavmeshScenario.DeletedWhereItsOnlyObjectStartsDisabled: world.DeleteWest(); break;
            case NavmeshScenario.DeletedWithItsMapEntryChanged: world.DeleteWest(); world.RaiseWestsMapEntry(); break;
            case NavmeshScenario.Replaced: world.ReplaceWest(world.AddDoorToWest()); break;
            case NavmeshScenario.EditedAgainByTheCityMod: world.EditWestInBothMods(); break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "No such scenario.");
        }

        return [world._baseGame.Mod, world._target.Mod, world._city.Mod];
    }

    private void PlaceTheCleanedModsCrateWithACityPadBesideIt()
    {
        var crate = Origin + new Vector3(CrateOffset, CrateOffset, 0f);
        new FixtureCell(_target, CellCopyIn(_target.Mod)).Place("T_Crate", _target.AddModeledStatic("T_CrateBase", FixtureMeshes.CrateModel), crate);
        new FixtureCell(_city, CellCopyIn(_city.Mod)).Place("C_Pad", _city.AddModeledStatic("C_PadBase", FixtureMeshes.PadModel), crate + new Vector3(PadDistance, 0f, 0f));
    }

    private void PlaceADisabledCrateHereAndTheCrateWithItsPadInAnInterior()
    {
        var crateBase = _target.AddModeledStatic("T_CrateBase", FixtureMeshes.CrateModel);
        var questCrate = new FixtureCell(_target, CellCopyIn(_target.Mod)).Place("T_QuestCrate", crateBase, Origin + new Vector3(CrateOffset, CrateOffset, 0f));
        questCrate.MajorRecordFlagsRaw |= (int)SkyrimMajorRecord.SkyrimMajorRecordFlag.InitiallyDisabled;
        var interior = new Cell(_baseGame.NextKey(), FixtureMod.Release) { EditorID = "AorInterior", Flags = Cell.Flag.IsInteriorCell };
        _baseGame.Mod.Cells.AddInteriorCell(interior);
        new FixtureCell(_target, InteriorCopyIn(_target.Mod, interior)).Place("T_Crate", crateBase, Vector3.Zero);
        new FixtureCell(_city, InteriorCopyIn(_city.Mod, interior)).Place("C_Pad", _city.AddModeledStatic("C_PadBase", FixtureMeshes.PadModel), new Vector3(PadDistance, 0f, 0f));
    }

    private static Cell InteriorCopyIn(ISkyrimMod mod, Cell interior)
    {
        var copy = (Cell)interior.DeepCopy();
        mod.Cells.AddInteriorCell(copy);
        return copy;
    }

    private void DeleteWest()
    {
        Deleted(NavmeshCopyIn(_target.Mod, _west));
        Unlink(NavmeshCopyIn(_target.Mod, _east), triangle: 1, WestSide);
    }

    private void RaiseWestsMapEntry()
    {
        var entry = Entry(_west.FormKey, Size / 2);
        entry.Point = new P3Float(entry.Point.X, entry.Point.Y, Size / 2);
        MapIn(_target.Mod).MapInfos.Add(entry);
    }

    /// <summary>A door of the base game over the west navmesh, linked to its second triangle, which names it; its map entry names the door too.</summary>
    private PlacedObject AddDoorToWest()
    {
        var door = new PlacedObject(_baseGame.NextKey(), FixtureMod.Release)
        {
            Placement = new Placement { Position = Corner(Size / 4, Size * 3 / 4) },
            NavigationDoorLink = DoorLinkTo(_west.FormKey),
        };
        door.Base.SetTo(_baseGame.AddMarkerStatic("B_Door"));
        _cell.Temporary.Add(door);
        _west.Data!.DoorTriangles.Add(DoorTriangleFor(door));
        _map.MapInfos.Single(entry => entry.NavigationMesh.FormKey == _west.FormKey).LinkedDoors.Add(new LinkedDoor { Door = new FormLink<IPlacedObjectGetter>(door.FormKey) });
        return door;
    }

    private void ReplaceWest(PlacedObject door)
    {
        var replacement = Square(_target.NextKey(), 0f);
        Link(replacement, 0, EastSide, _east.FormKey, 1);
        replacement.Data!.DoorTriangles.Add(DoorTriangleFor(door));
        CellCopyIn(_target.Mod).NavigationMeshes.Add(replacement);
        Deleted(NavmeshCopyIn(_target.Mod, _west));
        var entry = Entry(replacement.FormKey, Size / 2);
        entry.LinkedDoors.Add(new LinkedDoor { Door = new FormLink<IPlacedObjectGetter>(door.FormKey) });
        MapIn(_target.Mod).MapInfos.Add(entry);
        NavmeshCopyIn(_target.Mod, _east).Data!.EdgeLinks[0].Mesh = new FormLink<INavigationMeshGetter>(replacement.FormKey);
        var doorCopy = (PlacedObject)door.DeepCopy();
        doorCopy.NavigationDoorLink = DoorLinkTo(replacement.FormKey);
        CellCopyIn(_target.Mod).Temporary.Add(doorCopy);
    }

    private static DoorTriangle DoorTriangleFor(PlacedObject door) => new() { TriangleBeforeDoor = DoorTriangleIndex, Door = new FormLink<IPlacedObjectGetter>(door.FormKey) };

    private static NavigationDoorLink DoorLinkTo(FormKey navmesh) => new() { NavMesh = new FormLink<INavigationMeshGetter>(navmesh), TeleportMarkerTriangle = DoorTriangleIndex };

    private void EditWestInBothMods()
    {
        var cleaned = Raise(NavmeshCopyIn(_target.Mod, _west), CornerOffTheSharedEdge);
        Raise(NavmeshCopyIn(_city.Mod, cleaned), OtherCorner);
    }

    private static NavigationMesh Raise(NavigationMesh navmesh, int corner)
    {
        var data = navmesh.Data!;
        data.Vertices[corner] = new P3Float(data.Vertices[corner].X, data.Vertices[corner].Y, Size / 4 + corner);
        data.Max = new P3Float(data.Max.X, data.Max.Y, Math.Max(data.Max.Z, data.Vertices[corner].Z));
        return navmesh;
    }

    private static void Deleted(NavigationMesh navmesh)
    {
        navmesh.IsDeleted = true;
        navmesh.Data = null;
    }

    private static void Unlink(NavigationMesh navmesh, int triangle, int edge)
    {
        var side = NavmeshTriangleEdges.All[edge];
        side.SetValue(navmesh.Data!.Triangles[triangle], NavmeshTriangleEdges.None);
        navmesh.Data.Triangles[triangle].Flags &= ~side.Flag;
        navmesh.Data.EdgeLinks.Clear();
    }

    /// <summary>
    /// A square from x to x + <see cref="Size"/> in the cell: triangle 0 runs 0-1-2 (edge 1 is the east side), triangle 1 runs 0-2-3
    /// (edge 2 is the west side); the diagonal joins them.
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

    private static P3Float Corner(float x, float y) => new(Origin.X + x, Origin.Y + y, 0f);

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

    private NavigationMapInfo Entry(FormKey navmesh, float pointX) => new()
    {
        NavigationMesh = new FormLink<INavigationMeshGetter>(navmesh),
        Parent = new NavigationMapInfoWorldParent { ParentWorldspace = new FormLink<IWorldspaceGetter>(_worldspace), ParentWorldspaceCoord = StoredParentCoordinates },
        Point = Corner(pointX, Size / 2),
    };

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

    /// <summary>The plugin's copy of a navmesh, in its copy of the cell, made from the given version on first use.</summary>
    private NavigationMesh NavmeshCopyIn(ISkyrimMod mod, NavigationMesh from)
    {
        var cell = CellCopyIn(mod);
        if (cell.NavigationMeshes.FirstOrDefault(navmesh => navmesh.FormKey == from.FormKey) is NavigationMesh existing) return existing;
        var copy = (NavigationMesh)from.DeepCopy();
        cell.NavigationMeshes.Add(copy);
        return copy;
    }

    /// <summary>The plugin's copy of the cell, without its land, navmeshes and placed records, made on first use.</summary>
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

    /// <summary>The plugin's copy of the navmesh info map, with its preferred paths and without entries, made on first use.</summary>
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
}
