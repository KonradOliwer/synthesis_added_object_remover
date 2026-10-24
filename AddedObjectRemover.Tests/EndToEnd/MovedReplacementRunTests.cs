using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// Base-game objects that the cleaned mod overrides, through whole runs, on small worlds of their own: the cleaned mod also has a
/// crate of its own far away, and a city mod's walls and buildings stand where each test needs them. A wall 75 units from a
/// crate's centre is within the 32-unit distance margin of the 100-unit crate.
/// </summary>
public class MovedReplacementRunTests
{
    private const int Workers = 4;
    private const string BaseGamePlugin = "Skyrim.esm";
    private const string CityPlugin = "CityMod.esp";

    private static readonly Vector3 Original = Vector3.Zero;
    private static readonly Vector3 MovedTo = new(1000, 0, 0);
    private static readonly Vector3 NearMovedTo = MovedTo + new Vector3(75, 0, 0);
    private static readonly Vector3 OwnCrate = new(5000, 0, 0);

    private const int HomeGridX = 5;
    private const int EastGridX = HomeGridX + 1;

    /// <summary>A point inside the grid square of the home cell, and one inside the square of the cell east of it.</summary>
    private static readonly Vector3 InTheHomeCell = new(HomeGridX * ExteriorGrid.CellSize + 1000, 1000, 0);
    private static readonly Vector3 InTheEastCell = new(EastGridX * ExteriorGrid.CellSize + 1000, 1000, 0);

    [Fact]
    public void AReplacementLeftWhereItStoodIsNotCheckedEvenNextToAWall()
    {
        var outcome = Run(movedTo: Original, walls: [Original + new Vector3(75, 0, 0)]);

        Assert.Single(outcome.Collected.Targets);
        Assert.Empty(outcome.Final.RemovedTargets());
    }

    [Fact]
    public void AMovedReplacementThatFitsWhereTheCleanedModPutItStaysThere()
    {
        var outcome = Run(movedTo: MovedTo, walls: []);

        Assert.Null(outcome.Final.Of(ReplacementOf(outcome).Id));
    }

    /// <summary>Only the size margin, so the crate shrunk to 90% keeps clear of the wall that the full crate's margin reaches.</summary>
    [Fact]
    public void AMovedReplacementNextToAWallIsShrunkWhereTheCleanedModPutIt()
    {
        IPlacedObjectGetter? written = null;
        var outcome = Run(ShrinkSettings(), movedTo: MovedTo, walls: [NearMovedTo], inspectPatch: patch => written = FindBaseGameObject(patch));

        Assert.IsType<Decision.Fitted>(outcome.Final.Of(ReplacementOf(outcome).Id));
        Assert.Empty(outcome.Final.RemovedTargets());
        Assert.Equal(MovedTo, PositionOf(written!));
        Assert.Equal(0.9f, written!.Scale ?? 1f, 5);
        Assert.False(IsInitiallyDisabled(written!));
    }

    [Fact]
    public void AMovedReplacementThatFitsNowhereIsRemovedLikeTheCleanedModsObjects()
    {
        IPlacedObjectGetter? written = null;
        var outcome = Run(ShrinkSettings(), movedTo: MovedTo, walls: [NearMovedTo, MovedTo], inspectPatch: patch => written = FindBaseGameObject(patch));

        Assert.Equal(ReplacementOf(outcome).Id, Assert.Single(outcome.Final.RemovedTargets()));
        Assert.True(IsInitiallyDisabled(written!));
    }

    [Fact]
    public void AMovedReplacementNextToAWallIsRemovedWhenShrinkingAndTurningAreSwitchedOff()
    {
        var outcome = Run(ShrinkSettings(shrinkLimitPercent: 100, turnLimitDegrees: 0), movedTo: MovedTo, walls: [NearMovedTo]);

        Assert.Equal(ReplacementOf(outcome).Id, Assert.Single(outcome.Final.RemovedTargets()));
    }

    [Fact]
    public void TheLogCountsTheMovedReplacementsByOutcomeAndTheScanLineNamesThem()
    {
        var log = new RecordingLogSink();
        FixtureRun.Run(
            ShrinkSettings(),
            Workers,
            log,
            OpenMeshFiles,
            createLoadOrder: dataFolder => CreateCrateWorld(dataFolder, MovedTo, [NearMovedTo]));

        Assert.Equal(
            "Objects of other mods that Target.esp moved: 1; kept where Target.esp put them: 0; kept there shrunk or turned: 1; removed: 0.",
            Assert.Single(log.Sections.Single(section => section.Id == "moved-replacements").Lines));
        Assert.Contains("1 Target.esp objects to check, 1 objects of other mods that Target.esp moved,", log.Sections.Single(section => section.Id == "scan").Lines[0]);
        Assert.Contains("1 shrunk or turned to fit", log.Sections.Single(section => section.Id == "write").Lines[0]);
    }

    /// <summary>The cleaned mod moved the crate into the next exterior cell; shrunk there, the patch keeps it in the cell the cleaned mod put it in.</summary>
    [Fact]
    public void AReplacementMovedIntoAnotherCellIsShrunkInThatCell()
    {
        string? cellOfTheWrittenCrate = null;
        var outcome = FixtureRun.Execute(ShrinkSettings(), Workers, CreateTwoCellWorld);
        RunTwoCellWorld(patch => cellOfTheWrittenCrate = CellHoldingBaseGameObject(patch));

        Assert.IsType<Decision.Fitted>(outcome.Final.Of(ReplacementOf(outcome).Id));
        Assert.Equal(EastCellEditorId, cellOfTheWrittenCrate);
    }

    [Fact]
    public void AMovedReplacementIsJudgedInASpaceWhereTheCleanedModHasNoObjectOfItsOwn()
    {
        var outcome = FixtureRun.Execute(CrateSettings(), Workers, CreateTwoCellWorld);

        var replacement = ReplacementOf(outcome);
        Assert.DoesNotContain(outcome.Collected.Targets, target => !target.IsMovedReplacement && target.SpaceKey == replacement.SpaceKey);
        Assert.NotNull(outcome.Final.Of(replacement.Id));
    }

    /// <summary>
    /// The cleaned mod's crate on top of its other crate fits shrunk beside a city wall, but the crate it rests on is removed for a
    /// wall standing in it: also-remove takes the fitted crate along like any object still standing.
    /// </summary>
    [Fact]
    public void AnObjectKeptShrunkIsRemovedByAlsoRemoveWhenWhatItRestsOnIsRemoved()
    {
        IPlacedObjectGetter? writtenTop = null;
        var outcome = RunStackWorld(SupportSettings(), [BesideTheTop, Original], patch => writtenTop = FindTarget(patch, "T_Top"));

        var (top, bottom) = StackOf(outcome);
        Assert.Equal(bottom.Id, Assert.IsType<RemovalReason.LostSupport>(Assert.IsType<Decision.Removed>(outcome.Final.Of(top.Id)).Reason).MainSupporter);
        Assert.True(IsInitiallyDisabled(writtenTop!));
        Assert.Equal(1f, writtenTop!.Scale ?? 1f);
    }

    /// <summary>The bottom crate only touches the top crate with its top face, far from its base, so it does not rest on the top crate.</summary>
    [Fact]
    public void AnObjectKeptShrunkStaysWhenWhatItRestsOnStays()
    {
        var outcome = RunStackWorld(SupportSettings(), [BesideTheTop]);

        var (top, bottom) = StackOf(outcome);
        Assert.IsType<Decision.Fitted>(outcome.Final.Of(top.Id));
        Assert.Null(outcome.Final.Of(bottom.Id));
    }

    /// <summary>The bottom crate would fit shrunk beside a city wall, but the top crate rests on it.</summary>
    [Fact]
    public void AnObjectOthersRestOnIsRemovedInsteadOfShrunkAndWhatRestsOnItGoesWithIt()
    {
        var outcome = RunStackWorld(SupportSettings(), [BesideTheBottom]);

        var (top, bottom) = StackOf(outcome);
        Assert.IsType<RemovalReason.TooClose>(Assert.IsType<Decision.Removed>(outcome.Final.Of(bottom.Id)).Reason);
        Assert.Equal(bottom.Id, Assert.IsType<RemovalReason.LostSupport>(Assert.IsType<Decision.Removed>(outcome.Final.Of(top.Id)).Reason).MainSupporter);
    }

    /// <summary>With EverythingTouchingIt, the bottom crate touching the top crate counts as resting on it.</summary>
    [Fact]
    public void WithEverythingTouchingItAnObjectAnotherTouchesIsRemovedInsteadOfShrunk()
    {
        var outcome = RunStackWorld(ShrinkSettings(), [BesideTheTop]);

        var (top, bottom) = StackOf(outcome);
        Assert.IsType<RemovalReason.TooClose>(Assert.IsType<Decision.Removed>(outcome.Final.Of(top.Id)).Reason);
        Assert.Equal(new RemovalReason.Touching(top.Id), Assert.IsType<Decision.Removed>(outcome.Final.Of(bottom.Id)).Reason);
    }

    [Fact]
    public void TheCleanedModsOwnObjectIsRemovedWhenShrinkingAndTurningAreSwitchedOff()
    {
        var outcome = RunStackWorld(SupportSettings(shrinkLimitPercent: 100, turnLimitDegrees: 0), [BesideTheTop]);

        Assert.IsType<Decision.Removed>(outcome.Final.Of(StackOf(outcome).Top.Id));
    }

    /// <summary>
    /// The cleaned mod's twin pillars with a city pad 5 units east of the east pillar: turned 15 degrees either way the pillar is
    /// still within the 32-unit margin, turned 30 degrees it is clear.
    /// </summary>
    [Fact]
    public void AnObjectThatCannotBeShrunkIsTurnedByTheSmallestTurnThatFitsAndThePatchHoldsTheTurn()
    {
        IPlacedObjectGetter? written = null;
        var settings = ShrinkSettings(shrinkLimitPercent: 100, turnLimitDegrees: 90);
        settings.Standard.TooClose = new TooCloseRemovalSettings
        {
            BySize = new TooCloseBySizeSettings { Use = false },
            ByDistance = new TooCloseByDistanceSettings { Use = true, DistanceInUnits = 32f },
        };
        var outcome = Assert.IsType<RunResult.Done>(FixtureRun.Run(
            settings, Workers, new RecordingLogSink(), OpenMeshFiles, createLoadOrder: CreatePillarWorld, inspectPatch: patch => written = FindTarget(patch, "T_Pillars"))).Outcome;

        var pillars = Assert.Single(outcome.Collected.Targets, target => target.EditorId == "T_Pillars");
        var fitted = Assert.IsType<Decision.Fitted>(outcome.Final.Of(pillars.Id));
        Assert.Equal(float.DegreesToRadians(30), fitted.To.Rotation.Z, 4);
        Assert.Equal(1f, fitted.To.Scale);
        Assert.Equal(float.DegreesToRadians(30), written!.Placement!.Rotation.Z, 4);
        Assert.False(IsInitiallyDisabled(written));
    }

    /// <summary>The cleaned mod moved a base-game sound marker into a city mod's building: an invisible object has no shape to fit, so it is removed.</summary>
    [Fact]
    public void AnInvisibleReplacementMovedIntoAnotherModsObjectIsRemovedAsLeftInsideIt()
    {
        var outcome = FixtureRun.Execute(CrateSettings(), Workers, CreateSoundWorld);

        var removal = Assert.IsType<Decision.Removed>(outcome.Final.Of(ReplacementOf(outcome).Id));
        Assert.IsType<RemovalReason.InsideAnotherModsObject>(removal.Reason);
    }

    private const string HomeCellEditorId = "AorHomeCell";
    private const string EastCellEditorId = "AorEastCell";

    private static TargetObject ReplacementOf(RunOutcome outcome) => Assert.Single(outcome.Collected.Targets, target => target.IsMovedReplacement);

    /// <summary>The base game's object that the cleaned mod overrides, as the patch holds it; null when the patch does not hold it.</summary>
    private static IPlacedObjectGetter? FindBaseGameObject(ISkyrimModGetter patch) =>
        patch.EnumerateMajorRecords<IPlacedObjectGetter>().SingleOrDefault(placed => placed.FormKey.ModKey == ModKey.FromNameAndExtension(BaseGamePlugin));

    /// <summary>The Editor ID of the patch's cell whose temporary list holds the base game's object.</summary>
    private static string? CellHoldingBaseGameObject(ISkyrimModGetter patch) =>
        patch.EnumerateMajorRecords<ICellGetter>()
            .Single(cell => cell.Temporary.Any(placed => placed.FormKey.ModKey == ModKey.FromNameAndExtension(BaseGamePlugin)))
            .EditorID;

    private static Vector3 PositionOf(IPlacedObjectGetter placed) => new(placed.Placement!.Position.X, placed.Placement.Position.Y, placed.Placement.Position.Z);

    private static bool IsInitiallyDisabled(IPlacedObjectGetter placed) =>
        (placed.MajorRecordFlagsRaw & (int)SkyrimMajorRecord.SkyrimMajorRecordFlag.InitiallyDisabled) != 0;

    private static IMeshFilesFactory OpenMeshFiles(string dataFolder, Mutagen.Bethesda.GameRelease release, IReadOnlyList<ModKey> plugins) =>
        new MeshFilesFactory(dataFolder, release, plugins);

    private static RunOutcome Run(Vector3 movedTo, IReadOnlyList<Vector3> walls) => Run(CrateSettings(), movedTo, walls);

    private static RunOutcome Run(Settings settings, Vector3 movedTo, IReadOnlyList<Vector3> walls, Action<ISkyrimModGetter>? inspectPatch = null)
    {
        var result = FixtureRun.Run(
            settings, Workers, new RecordingLogSink(), OpenMeshFiles, createLoadOrder: dataFolder => CreateCrateWorld(dataFolder, movedTo, walls), inspectPatch: inspectPatch);
        return Assert.IsType<RunResult.Done>(result).Outcome;
    }

    private static void RunTwoCellWorld(Action<ISkyrimModGetter> inspectPatch) =>
        FixtureRun.Run(ShrinkSettings(), Workers, new RecordingLogSink(), OpenMeshFiles, createLoadOrder: CreateTwoCellWorld, inspectPatch: inspectPatch);

    /// <param name="walls">Where the city mod's walls stand.</param>
    private static RunOutcome RunStackWorld(Settings settings, IReadOnlyList<Vector3> walls, Action<ISkyrimModGetter>? inspectPatch = null)
    {
        var result = FixtureRun.Run(
            settings, Workers, new RecordingLogSink(), OpenMeshFiles, createLoadOrder: dataFolder => CreateStackWorld(dataFolder, walls), inspectPatch: inspectPatch);
        return Assert.IsType<RunResult.Done>(result).Outcome;
    }

    private static (TargetObject Top, TargetObject Bottom) StackOf(RunOutcome outcome) =>
        (Assert.Single(outcome.Collected.Targets, target => target.EditorId == "T_Top"),
            Assert.Single(outcome.Collected.Targets, target => target.EditorId == "T_Bottom"));

    private static IPlacedObjectGetter FindTarget(ISkyrimModGetter patch, string editorId) =>
        patch.EnumerateMajorRecords<IPlacedObjectGetter>().Single(placed => placed.EditorID == editorId);

    private static Settings CrateSettings() => new()
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
    };

    /// <summary>Only the size margin, so the 90% crate keeps clear of a wall the full crate's margin reaches.</summary>
    private static Settings ShrinkSettings(
        int shrinkLimitPercent = 80, int turnLimitDegrees = 0, FollowUpRemovalMode followUp = FollowUpRemovalMode.EverythingTouchingIt) => new()
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
    };

    private static Settings SupportSettings(int shrinkLimitPercent = 80, int turnLimitDegrees = 0) =>
        ShrinkSettings(shrinkLimitPercent, turnLimitDegrees, FollowUpRemovalMode.OnlyWhatLosesItsSupport);

    private sealed record World(FixtureMod BaseGame, FixtureMod Target, FixtureMod City)
    {
        public IReadOnlyList<ISkyrimMod> LoadOrder => [BaseGame.Mod, Target.Mod, City.Mod];
    }

    /// <summary>Base game, cleaned mod with a crate of its own far away, and the city mod, each with the shared spaces.</summary>
    private static World CreateWorld(string dataFolder)
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
    private static IReadOnlyList<ISkyrimMod> CreateCrateWorld(string dataFolder, Vector3 movedTo, IReadOnlyList<Vector3> walls)
    {
        var world = CreateWorld(dataFolder);
        var crate = world.BaseGame.Interior.Place("B_Crate", world.BaseGame.AddModeledStatic("B_CrateBase", FixtureMeshes.CrateModel), Original);
        MoveCopy(world.Target.Interior.PlaceCopyOf(crate), movedTo);
        PlaceWalls(world.City, world.City.Interior, walls);
        return world.LoadOrder;
    }

    /// <summary>
    /// The base game's crate in its own exterior cell; the cleaned mod moves it into the cell east of it, next to a city wall. The
    /// cleaned mod's own crate is in the interior, so the worldspace holds no object of its own. Each plugin holds both cells.
    /// </summary>
    private static IReadOnlyList<ISkyrimMod> CreateTwoCellWorld(string dataFolder)
    {
        var world = CreateWorld(dataFolder);
        var (homeKey, eastKey) = (world.BaseGame.NextKey(), world.BaseGame.NextKey());
        var cells = new[] { world.BaseGame, world.Target, world.City }
            .ToDictionary(mod => mod, mod => (Home: AddExteriorCell(mod, homeKey, HomeCellEditorId, HomeGridX), East: AddExteriorCell(mod, eastKey, EastCellEditorId, EastGridX)));
        var crate = cells[world.BaseGame].Home.Place("B_Crate", world.BaseGame.AddModeledStatic("B_CrateBase", FixtureMeshes.CrateModel), InTheHomeCell);
        MoveCopy(cells[world.Target].East.PlaceCopyOf(crate), InTheEastCell);
        PlaceWalls(world.City, cells[world.City].East, [InTheEastCell + new Vector3(75, 0, 0)]);
        return world.LoadOrder;
    }

    /// <summary>
    /// The cleaned mod's crate standing on its other crate in the interior, on a base-game floor, and the city mod's walls. A wall
    /// beside the top crate stands 20 units above the bottom crate, so only the top crate is too close to it.
    /// </summary>
    private static IReadOnlyList<ISkyrimMod> CreateStackWorld(string dataFolder, IReadOnlyList<Vector3> walls)
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

    private static readonly Vector3 BesideTheBottom = Original + new Vector3(75, 0, 0);
    private static readonly Vector3 BesideTheTop = Original + new Vector3(75, 0, FixtureMeshes.CrateSize + 20);

    /// <summary>The cleaned mod's twin pillars in the interior and a city pad 5 units east of the east pillar.</summary>
    private static IReadOnlyList<ISkyrimMod> CreatePillarWorld(string dataFolder)
    {
        var world = CreateWorld(dataFolder);
        world.Target.Interior.Place("T_Pillars", world.Target.AddModeledStatic("T_PillarsBase", FixtureMeshes.TwinPillarsModel), Original);
        PlaceWalls(world.City, world.City.Interior, [Original + new Vector3(175, 0, 0)]);
        return world.LoadOrder;
    }

    /// <summary>A base-game sound marker in the interior that the cleaned mod moves into a city building.</summary>
    private static IReadOnlyList<ISkyrimMod> CreateSoundWorld(string dataFolder)
    {
        var world = CreateWorld(dataFolder);
        var sound = world.BaseGame.Interior.Place("B_Sound", world.BaseGame.AddSoundMarker("B_SoundBase"), Original + new Vector3(0, 0, 50));
        MoveCopy(world.Target.Interior.PlaceCopyOf(sound), MovedTo + new Vector3(0, 0, 50));
        var building = world.City.AddModeledStatic("C_Building", FixtureMeshes.BuildingModel);
        world.City.Interior.Place("C_BuildingAtTheNewSpot", building, MovedTo);
        return world.LoadOrder;
    }

    private static void MoveCopy(PlacedObject copy, Vector3 to) => copy.Placement!.Position = new P3Float(to.X, to.Y, to.Z);

    private static void PlaceWalls(FixtureMod city, FixtureCell cell, IReadOnlyList<Vector3> walls)
    {
        var wallBase = city.AddModeledStatic("C_WallBase", FixtureMeshes.PadModel);
        foreach (var (wall, index) in walls.Select((wall, index) => (wall, index))) cell.Place($"C_Wall{index}", wallBase, wall);
    }

    /// <summary>One plugin's copy of an exterior cell of the fixture's worldspace, at grid (x, 0).</summary>
    private static FixtureCell AddExteriorCell(FixtureMod mod, FormKey key, string editorId, int x)
    {
        var cell = new Cell(key, FixtureMod.Release) { EditorID = editorId, Grid = new CellGrid { Point = new P2Int(x, 0) } };
        mod.Mod.Worldspaces.First().AddCell(cell);
        return new FixtureCell(mod, cell);
    }
}
