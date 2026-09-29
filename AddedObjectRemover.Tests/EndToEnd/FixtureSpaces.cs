using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// The interior cell and the worldspace with one exterior cell that every plugin places records in.
/// The base game plugin owns them, including the terrain; the other plugins hold override copies.
/// </summary>
internal sealed class FixtureSpaces
{
    /// <summary>Flat terrain: every vertex of the height map sits this many height steps above zero.</summary>
    private const float TerrainOffsetSteps = 16f;

    /// <summary>A LAND height step in game units.</summary>
    private const float UnitsPerHeightStep = 8f;

    private const string InteriorEditorId = "AorInterior";
    private const string WorldspaceEditorId = "AorWorld";
    private const string ExteriorCellEditorId = "AorExterior";

    private readonly FormKey _interior;
    private readonly FormKey _worldspace;
    private readonly FormKey _exteriorCell;

    private FixtureSpaces(FormKey interior, FormKey worldspace, FormKey exteriorCell)
    {
        _interior = interior;
        _worldspace = worldspace;
        _exteriorCell = exteriorCell;
    }

    /// <summary>Height of the terrain of the exterior cell.</summary>
    public static float TerrainHeight => TerrainOffsetSteps * UnitsPerHeightStep;

    public static FixtureSpaces CreateIn(FixtureMod baseGame)
    {
        var spaces = new FixtureSpaces(baseGame.NextKey(), baseGame.NextKey(), baseGame.NextKey());
        spaces.AddTo(baseGame, CreateTerrain(baseGame));
        return spaces;
    }

    public void AddOverridesTo(FixtureMod mod) => AddTo(mod, landscape: null);

    private void AddTo(FixtureMod mod, Landscape? landscape)
    {
        var interior = new Cell(_interior, FixtureMod.Release) { EditorID = InteriorEditorId, Flags = Cell.Flag.IsInteriorCell };
        mod.Mod.Cells.AddInteriorCell(interior);
        mod.Interior = new FixtureCell(mod, interior);

        var worldspace = new Worldspace(_worldspace, FixtureMod.Release) { EditorID = WorldspaceEditorId };
        var exterior = new Cell(_exteriorCell, FixtureMod.Release)
        {
            EditorID = ExteriorCellEditorId,
            Grid = new CellGrid { Point = new P2Int(0, 0) },
            Landscape = landscape,
        };
        worldspace.AddCell(exterior);
        mod.Mod.Worldspaces.Add(worldspace);
        mod.Exterior = new FixtureCell(mod, exterior);
    }

    private static Landscape CreateTerrain(FixtureMod baseGame) =>
        new(baseGame.NextKey(), FixtureMod.Release) { VertexHeightMap = new LandscapeVertexHeightMap { Offset = TerrainOffsetSteps } };
}
