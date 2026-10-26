using System.Numerics;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// The changed-terrain step through a whole run, on a small world of its own: the base game's flat grass terrain, one target
/// crate per cell, a mod that requires the target, another mod that changes the terrain under some of the crates, a mod trusted
/// for its own changes that re-saves one of those changes, a trusted mod that carries another, and a mod whose copy of the world
/// stores no default water height.
/// </summary>
public class ChangedTerrainRunTests
{
    private const int Workers = 4;

    private static readonly PluginName Other = new(TerrainWorld.OtherPlugin);
    private static readonly PluginName NoDefaultWater = new(TerrainWorld.NoDefaultWaterPlugin);

    [Fact]
    public void ObjectsOnTerrainAnotherModChangedAreRemovedWithTheKindOfChange()
    {
        var outcome = FixtureRun.Execute(SettingsWithTerrain(on: true), Workers, TerrainWorld.Create);

        Assert.Equal(
            new Dictionary<string, RemovalReason?>
            {
                [TerrainWorld.CrateOnRaisedTerrain] = new RemovalReason.TerrainChanged(Other, TerrainChangeKind.Height),
                [TerrainWorld.CrateOnRoad] = new RemovalReason.TerrainChanged(Other, TerrainChangeKind.TerrainType),
                [TerrainWorld.CrateOnOtherGrass] = null,
                [TerrainWorld.CrateOnTerrainRaisedByARequiringMod] = null,
                [TerrainWorld.CrateUnderRaisedWater] = new RemovalReason.TerrainChanged(Other, TerrainChangeKind.WaterHeight),
                [TerrainWorld.CrateInAddedWater] = new RemovalReason.TerrainChanged(Other, TerrainChangeKind.WaterAdded),
                [TerrainWorld.CrateOnResavedTerrain] = new RemovalReason.TerrainChanged(Other, TerrainChangeKind.Height),
                [TerrainWorld.CrateOnCarriedTerrain] = null,
                [TerrainWorld.CrateInDroppedDefaultWater] = new RemovalReason.TerrainChanged(NoDefaultWater, TerrainChangeKind.WaterRemoved),
            },
            ReasonsByEditorId(outcome));
    }

    [Fact]
    public void WithTheStepSwitchedOffNoObjectIsRemoved() =>
        Assert.Empty(FixtureRun.Execute(SettingsWithTerrain(on: false), Workers, TerrainWorld.Create).Final.RemovedTargets());

    [Fact]
    public void TheNormalLogPrintsTheEchoTheSectionAndTheSummaryItem()
    {
        var log = RunLogged(SettingsWithTerrain(on: true));

        Assert.Contains(
            "Remove objects on terrain changed by other mods: on; Which of the cleaned mod's objects are checked: AllObjectsIncludingInvisibleOnes; "
            + "Margin by object size: on, 0.5 x object size; Margin by fixed distance: on, 64 units; Count height changes: on, ignoring changes up to 16 units; "
            + "Count ground type changes: on, remove only when this share of the ground points changed type: 20%; "
            + "Count water height changes: on, ignoring changes up to 16 units; Count water added/removed changes: on; "
            + "remove only when this share of the ground got covered/uncovered: 20%.",
            SectionLines(log, "config"));
        Assert.Equal([$"Found 6 of 9 {FixtureWorld.TargetName} objects on terrain changed by other mods."], SectionLines(log, "changedTerrain"));
        Assert.Equal(
            ["Removed 6 objects (6 on changed terrain, 0 too close, 0 follow-up removals, 0 visible objects left behind, 0 invisible objects left behind, 0 linked to removed objects)."],
            SectionLines(log, "removal-summary"));
        Assert.All(log.Sections.Where(section => section.Id == "linked"), section => Assert.Empty(section.Lines));
    }

    [Fact]
    public void TheDetailedLogPrintsTheCountsLinkedGroupsAndTheReasonOfEachRemoval()
    {
        var settings = SettingsWithTerrain(on: true);
        settings.DetailedLogAndReports = true;

        var log = RunLogged(settings);

        var changedTerrain = SectionLines(log, "changedTerrain");
        Assert.Equal($"Found 6 of 9 {FixtureWorld.TargetName} objects on terrain changed by other mods in 0.0s.", changedTerrain[0]);
        Assert.Equal(
            "  Terrain: 9 cells compared; 6 objects on changed terrain (by changing mod: 5 Other.esp, 1 NoDefaultWater.esp); 0 objects not checked (interior, no terrain, or no trusted land record).",
            changedTerrain[1]);
        Assert.Contains(
            "Linked groups: 0 more objects removed with the changed-terrain removals they are linked to.",
            log.Sections.Where(section => section.Id == "linked").SelectMany(section => section.Lines));
        var removals = SectionLines(log, "removals");
        Assert.Single(removals, line => line.Contains(TerrainWorld.CrateOnRaisedTerrain) && line.EndsWith("terrain changed by Other.esp (height)"));
        Assert.Single(removals, line => line.Contains(TerrainWorld.CrateOnRoad) && line.EndsWith("terrain changed by Other.esp (ground type)"));
        Assert.Single(removals, line => line.Contains(TerrainWorld.CrateUnderRaisedWater) && line.EndsWith("terrain changed by Other.esp (water height)"));
        Assert.Single(removals, line => line.Contains(TerrainWorld.CrateInAddedWater) && line.EndsWith("terrain changed by Other.esp (water added)"));
        Assert.Single(removals, line => line.Contains(TerrainWorld.CrateOnResavedTerrain) && line.EndsWith("terrain changed by Other.esp (height)"));
        Assert.Single(removals, line => line.Contains(TerrainWorld.CrateInDroppedDefaultWater) && line.EndsWith("terrain changed by NoDefaultWater.esp (water removed)"));
    }

    [Fact]
    public void WithWaterChangesNotCountedTheWaterCratesStay()
    {
        var settings = SettingsWithTerrain(on: true);
        settings.Standard.TerrainChange.CountWaterHeightChanges = false;
        settings.Standard.TerrainChange.CountWaterAddedOrRemoved = false;

        var reasons = ReasonsByEditorId(FixtureRun.Execute(settings, Workers, TerrainWorld.Create));

        Assert.Equal(
            (null, null, null),
            (reasons[TerrainWorld.CrateUnderRaisedWater], reasons[TerrainWorld.CrateInAddedWater], reasons[TerrainWorld.CrateInDroppedDefaultWater]));
    }

    [Fact]
    public void WithTheStepSwitchedOffNothingAboutItIsPrinted()
    {
        var log = RunLogged(SettingsWithTerrain(on: false));

        Assert.DoesNotContain(log.Sections, section => section.Id == "changedTerrain");
        Assert.DoesNotContain(SectionLines(log, "config"), line => line.StartsWith("Changed terrain"));
        Assert.DoesNotContain("on changed terrain", Assert.Single(SectionLines(log, "removal-summary")));
    }

    [Fact]
    public void AFailingCheckOfEveryObjectIsPrintedAsOneErrorAndRemovesNothing()
    {
        var log = new RecordingLogSink();

        var outcome = ((RunResult.Done)FixtureRun.Run(
            SettingsWithTerrain(on: true), Workers, log, DefaultMeshFiles, plugin => new FailingTerrainVersionsPlugin(plugin), createLoadOrder: TerrainWorld.Create)).Outcome;

        Assert.Empty(outcome.Final.RemovedTargets());
        var error = Assert.Single(log.Sections, section => section.Id == "unexpectedErrors" && section.Lines.Length > 0).Lines;
        Assert.StartsWith(
            $"  Unexpected error while checking objects for changed terrain (InvalidOperationException: {FailingTerrainVersionsPlugin.FailureMessage}): 9 objects, e.g. ",
            Assert.Single(error));
        Assert.EndsWith(" They were not removed.", error[0]);
    }

    private static RecordingLogSink RunLogged(Settings settings)
    {
        var log = new RecordingLogSink();
        FixtureRun.Run(settings, Workers, log, DefaultMeshFiles, createLoadOrder: TerrainWorld.Create);
        return log;
    }

    private static IMeshFilesFactory DefaultMeshFiles(string dataFolder, GameRelease release, IReadOnlyList<ModKey> listedPlugins) =>
        new MeshFilesFactory(dataFolder, release, listedPlugins);

    private static string[] SectionLines(RecordingLogSink log, string id) =>
        [.. log.Sections.Where(section => section.Id == id).SelectMany(section => section.Lines)];

    private static Settings SettingsWithTerrain(bool on) => new()
    {
        Standard = new StandardSettings
        {
            CleanedMod = ModKey.FromNameAndExtension(FixtureWorld.TargetName),
            Trust = new TrustSettings
            {
                TrustedModsAndTheirMasters = [ModKey.FromNameAndExtension(TerrainWorld.CarrierPlugin)],
                ModsTrustedForOwnChangesOnly = [ModKey.FromNameAndExtension(TerrainWorld.ResaverPlugin)],
            },
            TerrainChange = new TerrainChangeSettings { RemoveOnChangedTerrain = on },
        },
    };

    private static Dictionary<string, RemovalReason?> ReasonsByEditorId(RunOutcome outcome) =>
        outcome.Collected.Targets.ToDictionary(
            target => target.EditorId!,
            target => outcome.Final.Of(target.Id) is Decision.Removed removed ? removed.Reason : null);

    /// <summary>
    /// Nine exterior cells in a row, each with one target crate. The base game paints them all flat grass; another mod raises the
    /// first, paves the second with a road (a stone material) and repaints the third with another grass texture; a mod that
    /// requires the target raises the fourth. The fifth has water below the ground, which the other mod raises above it; the
    /// sixth has no water, and the other mod gives it water at its own height, above the ground. The other mod also raises the
    /// seventh, which a mod trusted for its own changes re-saves one height step higher, and the eighth, which a trusted mod
    /// carries as it is. The ninth has water at the worldspace's default height, above the ground, and a mod loading last has a
    /// copy of the worldspace that stores no default water height.
    /// </summary>
    private sealed class TerrainWorld
    {
        public const string OtherPlugin = "Other.esp";
        public const string ResaverPlugin = "Resaver.esp";
        public const string CarrierPlugin = "Carrier.esp";
        public const string NoDefaultWaterPlugin = "NoDefaultWater.esp";
        public const string CrateOnRaisedTerrain = "T_CrateOnRaisedTerrain";
        public const string CrateOnRoad = "T_CrateOnRoad";
        public const string CrateOnOtherGrass = "T_CrateOnOtherGrass";
        public const string CrateOnTerrainRaisedByARequiringMod = "T_CrateOnTerrainRaisedByARequiringMod";
        public const string CrateUnderRaisedWater = "T_CrateUnderRaisedWater";
        public const string CrateInAddedWater = "T_CrateInAddedWater";
        public const string CrateOnResavedTerrain = "T_CrateOnResavedTerrain";
        public const string CrateOnCarriedTerrain = "T_CrateOnCarriedTerrain";
        public const string CrateInDroppedDefaultWater = "T_CrateInDroppedDefaultWater";

        private const string BaseGamePlugin = "Skyrim.esm";
        private const string RequiringModPlugin = "RequiringMod.esp";

        /// <summary>Height steps of 8 units: 64 units, above the default tolerance.</summary>
        private const float RaisedSteps = 8f;

        /// <summary>One height step (8 units) above the raise: within the default tolerance of 16 units.</summary>
        private const float ResavedSteps = RaisedSteps + 1f;

        private const float FlatSteps = 0f;
        private const float CrateOffsetInCell = 1000f;

        private const float WaterBelowTheGround = -100f;
        private const float WaterAboveTheGround = 100f;
        private const float DefaultWaterHeight = 50f;
        private const float DefaultLandHeight = -2048f;
        private const int RaisedWaterCell = 4;
        private const int AddedWaterCell = 5;
        private const int ResavedCell = 6;
        private const int CarriedCell = 7;
        private const int DroppedDefaultWaterCell = 8;

        private readonly FixtureMod _baseGame = new(BaseGamePlugin);
        private readonly FormKey _worldspace;
        private readonly Dictionary<int, (FormKey Cell, FormKey Land)> _cellKeys = [];
        private readonly Dictionary<(ModKey Mod, int CellX), Cell> _cells = [];
        private readonly Dictionary<ModKey, Worldspace> _worldspaces = [];

        private TerrainWorld()
        {
            _worldspace = _baseGame.NextKey();
        }

        public static IReadOnlyList<ISkyrimMod> Create(string dataFolder)
        {
            FixtureMeshes.WriteAll(dataFolder);
            var world = new TerrainWorld();
            var baseGame = world._baseGame;
            var target = new FixtureMod(FixtureWorld.TargetName, baseGame);
            var requiringMod = new FixtureMod(RequiringModPlugin, baseGame, target);
            var other = new FixtureMod(OtherPlugin, baseGame);
            var resaver = new FixtureMod(ResaverPlugin, baseGame);
            var carrier = new FixtureMod(CarrierPlugin, baseGame);
            var noDefaultWater = new FixtureMod(NoDefaultWaterPlugin, baseGame);

            var grass = world.AddTexture(world.AddMaterial());
            var otherGrass = world.AddTexture(world.Material(grass));
            var road = world.AddTexture(world.AddMaterial());

            var crates = new[]
            {
                CrateOnRaisedTerrain, CrateOnRoad, CrateOnOtherGrass, CrateOnTerrainRaisedByARequiringMod, CrateUnderRaisedWater, CrateInAddedWater,
                CrateOnResavedTerrain, CrateOnCarriedTerrain, CrateInDroppedDefaultWater,
            };
            var crateBase = target.AddModeledStatic("T_CrateBase", FixtureMeshes.CrateModel);
            for (var cellX = 0; cellX < crates.Length; cellX++)
            {
                world.Paint(baseGame, cellX, FlatSteps, grass);
                new FixtureCell(target, world.CopyOf(target, cellX))
                    .Place(crates[cellX], crateBase, new Vector3(cellX * ExteriorGrid.CellSize + CrateOffsetInCell, CrateOffsetInCell, 0));
            }
            world.Paint(other, 0, RaisedSteps, grass);
            world.Paint(other, 1, FlatSteps, road);
            world.Paint(other, 2, FlatSteps, otherGrass);
            world.Paint(requiringMod, 3, RaisedSteps, grass);
            world.Flood(baseGame, RaisedWaterCell, WaterBelowTheGround);
            world.Flood(target, RaisedWaterCell, WaterBelowTheGround);
            world.Flood(other, RaisedWaterCell, WaterAboveTheGround);
            world.Flood(other, AddedWaterCell, WaterAboveTheGround);
            world.Paint(other, ResavedCell, RaisedSteps, grass);
            world.Paint(resaver, ResavedCell, ResavedSteps, grass);
            world.Paint(other, CarriedCell, RaisedSteps, grass);
            world.Paint(carrier, CarriedCell, RaisedSteps, grass);
            world.Flood(baseGame, DroppedDefaultWaterCell, height: null);
            world.Flood(target, DroppedDefaultWaterCell, height: null);
            world.StoreNoDefaultWater(noDefaultWater);
            return [baseGame.Mod, target.Mod, requiringMod.Mod, other.Mod, resaver.Mod, carrier.Mod, noDefaultWater.Mod];
        }

        /// <summary>The mod has a copy of the worldspace that stores no default water height, and no copy of any cell.</summary>
        private void StoreNoDefaultWater(FixtureMod mod) => WorldspaceOf(mod).LandDefaults = null;

        /// <summary>The mod's copy of the cell has water at the given height; null uses the worldspace's default height.</summary>
        private void Flood(FixtureMod mod, int cellX, float? height)
        {
            var cell = CopyOf(mod, cellX);
            cell.Flags |= Cell.Flag.HasWater;
            cell.WaterHeight = height;
        }

        private FormKey AddMaterial()
        {
            var material = new MaterialType(_baseGame.NextKey(), FixtureMod.Release);
            _baseGame.Mod.MaterialTypes.Add(material);
            return material.FormKey;
        }

        private FormKey Material(FormKey texture) => _baseGame.Mod.LandscapeTextures[texture].MaterialType.FormKey;

        private FormKey AddTexture(FormKey material)
        {
            var texture = new LandscapeTexture(_baseGame.NextKey(), FixtureMod.Release);
            texture.MaterialType.SetTo(material);
            _baseGame.Mod.LandscapeTextures.Add(texture);
            return texture.FormKey;
        }

        /// <summary>The mod's version of the cell's land record: flat at the given height, with the texture as every quadrant's base layer.</summary>
        private void Paint(FixtureMod mod, int cellX, float offsetSteps, FormKey texture)
        {
            var landscape = new Landscape(KeysOf(cellX).Land, FixtureMod.Release)
            {
                VertexHeightMap = new LandscapeVertexHeightMap { Offset = offsetSteps },
            };
            foreach (var quadrant in Enum.GetValues<Quadrant>())
            {
                var layer = new BaseLayer { Header = new LayerHeader { Quadrant = quadrant, LayerNumber = 0 } };
                layer.Header.Texture.SetTo(texture);
                landscape.Layers.Add(layer);
            }
            CopyOf(mod, cellX).Landscape = landscape;
        }

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
            var worldspace = new Worldspace(_worldspace, FixtureMod.Release)
            {
                LandDefaults = new WorldspaceLandDefaults { DefaultLandHeight = DefaultLandHeight, DefaultWaterHeight = DefaultWaterHeight },
            };
            mod.Mod.Worldspaces.Add(worldspace);
            _worldspaces[mod.Key] = worldspace;
            return worldspace;
        }

        private (FormKey Cell, FormKey Land) KeysOf(int cellX)
        {
            if (!_cellKeys.TryGetValue(cellX, out var keys)) _cellKeys[cellX] = keys = (_baseGame.NextKey(), _baseGame.NextKey());
            return keys;
        }
    }
}
