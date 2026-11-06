using System.Numerics;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// Two flat exterior cells in a row, (0, 0) and (1, 0), which the base game paints with grass and leaves dry. Before the cleaned mod
/// the ground textures of cell (0, 0) are a road and its water stands 100 units above the ground: set by <see cref="EarlierPlugin"/>,
/// which loads before the cleaned mod, or by the base game itself. The cleaned mod paints cell (0, 0) with grass again and lowers its
/// water 100 units under the ground. It has five crates in cell (0, 0), one at each quarter's centre and one at the cell's centre,
/// each with a pad of <see cref="PatchPlugin"/> beside it, so all are removed and the cell's areas are cleared; and one crate in cell
/// (1, 0), 300 units east of the edge, which stays and whose changed-terrain area reaches the east quarters of cell (0, 0).
/// Optionally a second mod to clean up, loading last, places a tree in cell (1, 0) as far east of the edge, in the north half.
/// </summary>
internal static class TexturesAndWaterWorld
{
    public const string EarlierPlugin = "Earlier.esp";

    /// <summary>A plugin that needs the cleaned mod, carries its copy of cell (0, 0) and places the pads.</summary>
    public const string PatchPlugin = "PatchOfTheCleanedMod.esp";
    public const string StandingCrate = "T_StandingCrate";

    /// <summary>The second mod to clean up, which needs nothing but the base game.</summary>
    public const string SecondCleanedPlugin = "ForestMod.esp";

    public const string SecondCleanedModsTree = "F_Tree";

    /// <summary>How far around the standing crate the changed-terrain step checks the ground, in game units.</summary>
    public const float CheckDistance = 1024f;

    /// <summary>The restore's cleared-area search radius: the quarters of cell (0, 0) do not see the standing crate.</summary>
    public const float SearchRadius = 1024f;

    /// <summary>The base game's boulder that the earlier plugin and the cleaned mod move.</summary>
    public const string Boulder = "B_Boulder";

    /// <summary>How high the earlier plugin raises the ground of cell (0, 0) when it moves the boulder.</summary>
    public const float RaisedGround = 64f;

    /// <summary>The base game's barrel that the earlier plugin moves.</summary>
    public const string Barrel = "B_Barrel";

    /// <summary>Where the barrel stands before the earlier plugin moves it: on the flat ground of cell (0, 0), away from every crate.</summary>
    public static readonly Vector3 BarrelOldPlace = new(ExteriorGrid.CellSize / 4, ExteriorGrid.CellSize / 2, 0);

    private const float BoulderX = ExteriorGrid.CellSize / 4;
    private const float BoulderY = 1400f;
    private const float BoulderStep = 100f;

    private const string BaseGamePlugin = "Skyrim.esm";
    /// <summary>The water height of cell (0, 0) before the cleaned mod.</summary>
    public const float WaterAboveTheGround = 100f;
    private const float WaterUnderTheGround = -100f;
    private const float DefaultLandHeight = -2048f;
    private const float PadDistance = 75f;
    private const float StandingCrateEastOfTheEdge = 300f;
    private const int Side = TerrainGrid.VerticesPerSide;

    /// <param name="earlierFromTheBaseGame">The road and the high water are the base game's; otherwise <see cref="EarlierPlugin"/>'s.</param>
    /// <param name="withSecondCleanedMod">Whether <see cref="SecondCleanedPlugin"/> and its tree are in the load order.</param>
    /// <param name="withRaisedGroundAndMovedBoulder">
    /// Whether the plugin that sets the road and the high water also raises the ground of cell (0, 0) by <see cref="RaisedGround"/> and
    /// moves the base game's boulder there onto it, which the cleaned mod lowers again and moves elsewhere; so a restore gives back
    /// heights and a placed record's position too.
    /// </param>
    /// <param name="withBarrelTheEarlierPluginMoves">
    /// Whether a base game barrel stands on the flat ground of cell (0, 0), where the earlier plugin's high water covers it, and the
    /// earlier plugin moves it beside the second mod's tree. Needs the earlier plugin and the second mod to clean up.
    /// </param>
    public static IReadOnlyList<ISkyrimMod> Create(
        string dataFolder, bool earlierFromTheBaseGame, bool withSecondCleanedMod = false, bool withRaisedGroundAndMovedBoulder = false, bool withBarrelTheEarlierPluginMoves = false)
    {
        FixtureMeshes.WriteAll(dataFolder);
        var world = new World();
        var baseGame = world.BaseGame;
        var target = new FixtureMod(FixtureWorld.TargetName, baseGame);
        var earlier = new FixtureMod(EarlierPlugin, baseGame);
        var grass = world.AddTexture();
        var road = world.AddTexture();

        var keepsTheRoadAndHighWater = earlierFromTheBaseGame ? baseGame : earlier;
        world.Paint(baseGame, 0, grass);
        world.Paint(baseGame, 1, grass);
        world.Paint(keepsTheRoadAndHighWater, 0, road, withRaisedGroundAndMovedBoulder ? RaisedGround : 0f);
        world.Flood(keepsTheRoadAndHighWater, 0, WaterAboveTheGround);
        if (withRaisedGroundAndMovedBoulder) MoveTheBoulder(world, keepsTheRoadAndHighWater, target);
        world.Paint(target, 0, grass);
        world.Flood(target, 0, WaterUnderTheGround);

        var patch = new FixtureMod(PatchPlugin, baseGame, target);
        world.Flood(patch, 0, WaterUnderTheGround);
        var crate = target.AddModeledStatic("T_CrateBase", FixtureMeshes.CrateModel);
        var pad = patch.AddModeledStatic("P_PadBase", FixtureMeshes.PadModel);
        var quarter = ExteriorGrid.CellSize / 4;
        Vector3[] removed = [new(quarter, quarter, 0), new(3 * quarter, quarter, 0), new(quarter, 3 * quarter, 0), new(3 * quarter, 3 * quarter, 0), new(2 * quarter, 2 * quarter, 0)];
        foreach (var (at, index) in removed.Select((at, index) => (at, index)))
        {
            world.CellOf(target, 0).Place($"T_Crate{index}", crate, at);
            world.CellOf(patch, 0).Place($"P_Pad{index}", pad, at + new Vector3(PadDistance, 0, 0));
        }
        world.CellOf(target, 1).Place(StandingCrate, crate, new Vector3(ExteriorGrid.CellSize + StandingCrateEastOfTheEdge, quarter, 0));
        IReadOnlyList<ISkyrimMod> mods = earlierFromTheBaseGame ? [baseGame.Mod, target.Mod, patch.Mod] : [baseGame.Mod, earlier.Mod, target.Mod, patch.Mod];
        if (!withSecondCleanedMod) return mods;

        var forest = new FixtureMod(SecondCleanedPlugin, baseGame);
        var tree = forest.AddModeledStatic("F_TreeBase", FixtureMeshes.CrateModel);
        var treeAt = new Vector3(ExteriorGrid.CellSize + StandingCrateEastOfTheEdge, 3 * quarter, 0);
        world.CellOf(forest, 1).Place(SecondCleanedModsTree, tree, treeAt);
        if (withBarrelTheEarlierPluginMoves) MoveTheBarrel(world, earlier, treeAt - new Vector3(PadDistance, 0, 0));
        return [.. mods, forest.Mod];
    }

    /// <summary>The base game's barrel at <see cref="BarrelOldPlace"/>, which the earlier plugin moves to <paramref name="to"/>.</summary>
    private static void MoveTheBarrel(World world, FixtureMod earlier, Vector3 to)
    {
        var barrel = world.CellOf(world.BaseGame, 0).Place(Barrel, world.BaseGame.AddModeledStatic("B_BarrelBase", FixtureMeshes.CrateModel), BarrelOldPlace);
        world.CellOf(earlier, 0).PlaceCopyOf(barrel).Placement!.Position = new P3Float(to.X, to.Y, to.Z);
    }

    /// <summary>
    /// The base game's boulder stands north of the south-west crate of cell (0, 0); the plugin that raises the ground moves it north
    /// onto the raised ground, and the cleaned mod moves it farther north onto its own flat ground.
    /// </summary>
    private static void MoveTheBoulder(World world, FixtureMod raisesTheGround, FixtureMod target)
    {
        var baseGame = world.BaseGame;
        var boulderBase = baseGame.AddModeledStatic("B_BoulderBase", FixtureMeshes.CrateModel);
        var boulder = world.CellOf(baseGame, 0).Place(Boulder, boulderBase, new Vector3(BoulderX, BoulderY, 0));
        if (raisesTheGround != baseGame) world.CellOf(raisesTheGround, 0).PlaceCopyOf(boulder).Placement!.Position = new P3Float(BoulderX, BoulderY + BoulderStep, RaisedGround);
        world.CellOf(target, 0).PlaceCopyOf(boulder).Placement!.Position = new P3Float(BoulderX, BoulderY + 2 * BoulderStep, 0);
    }

    private sealed class World
    {
        private readonly FormKey _worldspace;
        private readonly Dictionary<int, (FormKey Cell, FormKey Land)> _cellKeys = [];
        private readonly Dictionary<(ModKey Mod, int CellX), Cell> _cells = [];
        private readonly Dictionary<ModKey, Worldspace> _worldspaces = [];

        public World()
        {
            BaseGame = new FixtureMod(BaseGamePlugin);
            _worldspace = BaseGame.NextKey();
        }

        public FixtureMod BaseGame { get; }

        public FormKey AddTexture()
        {
            var material = new MaterialType(BaseGame.NextKey(), FixtureMod.Release);
            BaseGame.Mod.MaterialTypes.Add(material);
            var texture = new LandscapeTexture(BaseGame.NextKey(), FixtureMod.Release);
            texture.MaterialType.SetTo(material.FormKey);
            BaseGame.Mod.LandscapeTextures.Add(texture);
            return texture.FormKey;
        }

        /// <summary>The mod's version of the cell's land record: flat at the height, with the texture as every quadrant's base layer.</summary>
        public void Paint(FixtureMod mod, int cellX, FormKey texture, float height = 0f)
        {
            var landscape = new Landscape(KeysOf(cellX).Land, FixtureMod.Release)
            {
                VertexHeightMap = new LandscapeVertexHeightMap { Offset = height / TerrainGrid.UnitsPerHeightStep, HeightMap = new Array2d<sbyte>(new P2Int(Side, Side), default) },
                VertexNormals = new Array2d<P3UInt8>(new P2Int(Side, Side), default),
                VertexColors = new Array2d<P3UInt8>(new P2Int(Side, Side), default),
            };
            foreach (var quadrant in Enum.GetValues<Quadrant>())
            {
                var layer = new BaseLayer { Header = new LayerHeader { Quadrant = quadrant, LayerNumber = 0 } };
                layer.Header.Texture.SetTo(texture);
                landscape.Layers.Add(layer);
            }
            CopyOf(mod, cellX).Landscape = landscape;
        }

        /// <summary>The mod's copy of the cell has water at its own height.</summary>
        public void Flood(FixtureMod mod, int cellX, float height)
        {
            var cell = CopyOf(mod, cellX);
            cell.Flags |= Cell.Flag.HasWater;
            cell.WaterHeight = height;
        }

        public FixtureCell CellOf(FixtureMod mod, int cellX) => new(mod, CopyOf(mod, cellX));

        private Cell CopyOf(FixtureMod mod, int cellX)
        {
            if (_cells.TryGetValue((mod.Key, cellX), out var existing)) return existing;
            var cell = new Cell(KeysOf(cellX).Cell, FixtureMod.Release) { Grid = new CellGrid { Point = new P2Int(cellX, 0) } };
            WorldspaceOf(mod).AddCell(cell);
            _cells[(mod.Key, cellX)] = cell;
            return cell;
        }

        private Worldspace WorldspaceOf(FixtureMod mod)
        {
            if (_worldspaces.TryGetValue(mod.Key, out var existing)) return existing;
            var worldspace = new Worldspace(_worldspace, FixtureMod.Release) { LandDefaults = new WorldspaceLandDefaults { DefaultLandHeight = DefaultLandHeight, DefaultWaterHeight = DefaultLandHeight } };
            mod.Mod.Worldspaces.Add(worldspace);
            _worldspaces[mod.Key] = worldspace;
            return worldspace;
        }

        private (FormKey Cell, FormKey Land) KeysOf(int cellX)
        {
            if (!_cellKeys.TryGetValue(cellX, out var keys)) _cellKeys[cellX] = keys = (BaseGame.NextKey(), BaseGame.NextKey());
            return keys;
        }
    }
}
