using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// Small worlds of their own for the whole runs of base-game objects that the cleaned mod overrides: the cleaned mod also has a
/// crate of its own far away, and a city mod's walls and buildings stand where each test needs them. A wall 75 units from a
/// crate's centre is within the 32-unit distance margin of the 100-unit crate.
/// </summary>
internal static class MovedReplacementScenes
{
    public const int Workers = 4;
    public const string BaseGamePlugin = "Skyrim.esm";
    public const string CityPlugin = "CityMod.esp";

    public static readonly Vector3 Original = Vector3.Zero;
    public static readonly Vector3 MovedTo = new(1000, 0, 0);
    public static readonly Vector3 NearMovedTo = MovedTo + new Vector3(75, 0, 0);
    public static readonly Vector3 NearOriginal = Original + new Vector3(75, 0, 0);
    public static readonly Vector3 OwnCrate = new(5000, 0, 0);

    public static readonly Vector3 BesideTheTop = Original + new Vector3(75, 0, FixtureMeshes.CrateSize + 20);

    public static TargetObject ReplacementOf(RunOutcome outcome) => Assert.Single(outcome.Collected.Targets, target => target.Origin == TargetOrigin.Moved);

    /// <summary>The changed objects' modes are the defaults and their collision handling is the cleaned mod's, as the settings have it here.</summary>
    public static Settings CrateSettings() => new Settings
    {
        Standard = new StandardSettings
        {
            CleanedMod = ModKey.FromNameAndExtension(FixtureWorld.TargetName),
            TerrainChange = new TerrainChangeSettings { RemoveOnChangedTerrain = false },
            TooClose = new TooCloseRemovalSettings
            {
                BySize = new TooCloseBySizeSettings { Use = true, SizeMultiplier = 0.1f },
                ByDistance = new TooCloseByDistanceSettings { Use = true, DistanceInUnits = 32f },
            },
        },
    }.WithTheCollisionOfTheCleanedModsObjects();

    /// <summary>Only the size margin, so the 90% crate keeps clear of a wall the full crate's margin reaches.</summary>
    public static Settings ShrinkSettings(
        int shrinkLimitPercent = 80, int turnLimitDegrees = 0, FollowUpRemovalMode followUp = FollowUpRemovalMode.EverythingTouchingIt) => new Settings
    {
        Standard = new StandardSettings
        {
            CleanedMod = ModKey.FromNameAndExtension(FixtureWorld.TargetName),
            TerrainChange = new TerrainChangeSettings { RemoveOnChangedTerrain = false },
            TooClose = new TooCloseRemovalSettings
            {
                BySize = new TooCloseBySizeSettings { Use = true, SizeMultiplier = 0.1f },
                ByDistance = new TooCloseByDistanceSettings { Use = false },
            },
            ShrinkOrTurn = new ShrinkOrTurnSettings { ShrinkLimitPercent = shrinkLimitPercent, TurnLimitDegrees = turnLimitDegrees },
            FollowUpRemoval = new FollowUpRemovalSettings { Mode = followUp },
        },
    }.WithTheCollisionOfTheCleanedModsObjects();

    public static Settings SupportSettings(int shrinkLimitPercent = 80, int turnLimitDegrees = 0) =>
        ShrinkSettings(shrinkLimitPercent, turnLimitDegrees, FollowUpRemovalMode.OnlyWhatLosesItsSupport);

    /// <summary>A moved object that would be removed is tried shrunk or turned where the cleaned mod put it first and is then removed.</summary>
    public static Settings RemoveAfterResizing(Settings settings) => settings.WithMovedObjects(RestoreMode.Remove, attemptResizing: true);

    public static IMeshFilesFactory OpenMeshFiles(string dataFolder, Mutagen.Bethesda.GameRelease release, IReadOnlyList<PluginName> plugins) =>
        new MeshFilesFactory(dataFolder, release, plugins);

    public static RunOutcome RunCrateWorld(Vector3 movedTo, IReadOnlyList<Vector3> walls) => RunCrateWorld(CrateSettings(), movedTo, walls);

    public static RunOutcome RunCrateWorld(Settings settings, Vector3 movedTo, IReadOnlyList<Vector3> walls, Action<ISkyrimModGetter>? inspectPatch = null)
    {
        var result = FixtureRun.Run(
            settings, Workers, new RecordingLogSink(), OpenMeshFiles, createLoadOrder: dataFolder => CreateCrateWorld(dataFolder, movedTo, walls), inspectPatch: inspectPatch);
        return Assert.IsType<RunResult.Done>(result).Outcome;
    }

    /// <param name="earlierPatcherWrites">What an earlier patcher of the group wrote into the patch before the run starts; none when the patch starts empty.</param>
    public static (RunOutcome Outcome, RunOutput Run) RunRestoring(
        RestoreMode mode,
        Func<string, IReadOnlyList<ISkyrimMod>> createLoadOrder,
        Action<IPatcherState<ISkyrimMod, ISkyrimModGetter>>? earlierPatcherWrites = null,
        int workers = Workers)
    {
        var settings = CrateSettings().WithMovedObjects(mode);
        settings.DetailedLogAndReports = true;
        return FixtureRun.Capture(settings, workers, createLoadOrder, earlierPatcherWrites);
    }

    public sealed record World(FixtureMod BaseGame, FixtureMod Target, FixtureMod City)
    {
        public IReadOnlyList<ISkyrimMod> LoadOrder => [BaseGame.Mod, Target.Mod, City.Mod];
    }

    /// <summary>Base game, cleaned mod with a crate of its own far away, and the city mod, each with the shared spaces.</summary>
    public static World CreateWorld(string dataFolder)
    {
        FixtureMeshes.WriteAll(dataFolder);
        var baseGame = new FixtureMod(BaseGamePlugin);
        var target = new FixtureMod(FixtureWorld.TargetName, baseGame);
        var city = new FixtureMod(CityPlugin, baseGame);
        var spaces = FixtureSpaces.CreateIn(baseGame);
        spaces.AddOverridesTo(target);
        spaces.AddOverridesTo(city);
        target.Interior.Place("T_Crate", target.AddModeledStatic("T_CrateBase", FixtureMeshes.CrateModel), OwnCrate);
        return new World(baseGame, target, city);
    }

    /// <summary>The base game's crate in the interior, which the cleaned mod moves, and the city mod's walls.</summary>
    public static IReadOnlyList<ISkyrimMod> CreateCrateWorld(string dataFolder, Vector3 movedTo, IReadOnlyList<Vector3> walls) =>
        FillCrateWorld(CreateWorld(dataFolder), movedTo, walls).LoadOrder;

    public static World FillCrateWorld(World world, Vector3 movedTo, IReadOnlyList<Vector3> walls)
    {
        var crate = world.BaseGame.Interior.Place("B_Crate", world.BaseGame.AddModeledStatic("B_CrateBase", FixtureMeshes.CrateModel), Original);
        MoveCopy(world.Target.Interior.PlaceCopyOf(crate), movedTo);
        PlaceWalls(world.City, world.City.Interior, walls);
        return world;
    }

    /// <summary>The base game's crate in the interior, which the cleaned mod gives a building's model without moving it, and a city wall beside the building's edge.</summary>
    public static IReadOnlyList<ISkyrimMod> CreateReplacedWorld(string dataFolder) => CreateReplacedWorld(dataFolder, Original, _ => { });

    /// <param name="replacedAt">Where the cleaned mod puts the replaced crate; within the moved distance of the original place it still counts as replaced.</param>
    /// <param name="crateFound">Receives the crate's key.</param>
    public static IReadOnlyList<ISkyrimMod> CreateReplacedWorld(string dataFolder, Vector3 replacedAt, Action<FormKey> crateFound)
    {
        var world = CreateWorld(dataFolder);
        var crate = world.BaseGame.Interior.Place("B_Crate", world.BaseGame.AddModeledStatic("B_CrateBase", FixtureMeshes.CrateModel), Original);
        var replaced = world.Target.Interior.PlaceCopyOf(crate);
        replaced.Base.SetTo(world.Target.AddModeledStatic("T_BuildingBase", FixtureMeshes.BuildingModel));
        MoveCopy(replaced, replacedAt);
        crateFound(replaced.FormKey);
        PlaceWalls(world.City, world.City.Interior, [Original + new Vector3(250, 0, 0)]);
        return world.LoadOrder;
    }

    /// <summary>The crate world with a wall beside the new place, and a crate of the cleaned mod beside the old place.</summary>
    public static IReadOnlyList<ISkyrimMod> CreateCrateWorldWithAnOwnCrateAtTheOldPlace(string dataFolder)
    {
        var world = FillCrateWorld(CreateWorld(dataFolder), MovedTo, [NearMovedTo]);
        world.Target.Interior.Place("T_AtTheOldPlace", world.Target.AddModeledStatic("T_OldPlaceBase", FixtureMeshes.CrateModel), NearOriginal);
        return world.LoadOrder;
    }

    /// <summary>
    /// The cleaned mod's crate standing on its other crate in the interior, on a base-game floor, and the city mod's walls. A wall
    /// beside the top crate stands 20 units above the bottom crate, so only the top crate is too close to it.
    /// </summary>
    public static IReadOnlyList<ISkyrimMod> CreateStackWorld(string dataFolder, IReadOnlyList<Vector3> walls)
    {
        var world = CreateWorld(dataFolder);
        var floor = world.BaseGame.AddModeledStatic("B_FloorBase", FixtureMeshes.BuildingModel);
        world.BaseGame.Interior.Place("B_Floor", floor, Original - new Vector3(0, 0, FixtureMeshes.BuildingHeight));
        var crateBase = world.Target.AddModeledStatic("T_StackCrateBase", FixtureMeshes.CrateModel);
        world.Target.Interior.Place("T_Bottom", crateBase, Original);
        world.Target.Interior.Place("T_Top", crateBase, Original + new Vector3(0, 0, FixtureMeshes.CrateSize));
        PlaceWalls(world.City, world.City.Interior, walls);
        return world.LoadOrder;
    }

    public static void MoveCopy(PlacedObject copy, Vector3 to) => copy.Placement!.Position = new P3Float(to.X, to.Y, to.Z);

    public static void PlaceWalls(FixtureMod city, FixtureCell cell, IReadOnlyList<Vector3> walls)
    {
        var wallBase = city.AddModeledStatic("C_WallBase", FixtureMeshes.PadModel);
        foreach (var (wall, index) in walls.Select((wall, index) => (wall, index))) cell.Place($"C_Wall{index}", wallBase, wall);
    }
}
