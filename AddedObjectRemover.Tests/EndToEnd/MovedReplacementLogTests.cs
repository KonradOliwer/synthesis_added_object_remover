using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using static AddedObjectRemover.Tests.EndToEnd.MovedReplacementScenes;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>The log lines of whole runs of base-game objects that the cleaned mod moved or replaced.</summary>
public class MovedReplacementLogTests
{
    private static readonly Vector3 SecondCrate = new(0, 3000, 0);

    [Fact]
    public void TheLogCountsTheMovedReplacementsByOutcomeAndTheScanLineNamesThem()
    {
        var log = new RecordingLogSink();
        FixtureRun.Run(
            RemoveAfterResizing(ShrinkSettings()),
            Workers,
            log,
            OpenMeshFiles,
            createLoadOrder: dataFolder => CreateCrateWorld(dataFolder, MovedTo, [NearMovedTo]));

        Assert.Equal(
            "Objects of other mods that Target.esp changed: 1 (1 moved); kept where Target.esp put them: 0; kept there shrunk or turned: 1; removed: 0.",
            Assert.Single(log.Sections.Single(section => section.Id == "changed-objects").Lines));
        Assert.Contains("1 Target.esp objects to check, 1 objects of other mods that Target.esp moved,", log.Sections.Single(section => section.Id == "scan").Lines[0]);
        Assert.Contains("1 shrunk or turned to fit", log.Sections.Single(section => section.Id == "write").Lines[0]);
    }

    [Fact]
    public void TheChangedObjectsLineCountsTheMovedAndTheReplacedObjectsAndTheirOutcomes()
    {
        var log = new RecordingLogSink();
        FixtureRun.Run(
            CrateSettings().WithReplacedObjects(process: true, RestoreMode.RestoreIfNoCollision),
            Workers,
            log,
            OpenMeshFiles,
            createLoadOrder: CreateMovedAndReplacedWorld);

        Assert.Equal(
            "Objects of other mods that Target.esp changed: 2 (1 moved, 1 replaced); kept where Target.esp put them: 0; kept there shrunk or turned: 0; restored: 2; removed: 0.",
            Assert.Single(log.Sections.Single(section => section.Id == "changed-objects").Lines));
        Assert.StartsWith("Restored 2 objects of other mods that Target.esp changed (", Assert.Single(log.Sections.Single(section => section.Id == "restore-summary").Lines));
    }

    [Fact]
    public void TheChangedObjectsLineLeavesOutAKindWithoutObjectsAndTheRestoredItemWhenNothingWasRestored()
    {
        var log = new RecordingLogSink();
        FixtureRun.Run(CrateSettings().WithMovedObjects(RestoreMode.Remove), Workers, log, OpenMeshFiles, createLoadOrder: dataFolder => CreateCrateWorld(dataFolder, MovedTo, [NearMovedTo]));

        Assert.Equal(
            "Objects of other mods that Target.esp changed: 1 (1 moved); kept where Target.esp put them: 0; kept there shrunk or turned: 0; removed: 1.",
            Assert.Single(log.Sections.Single(section => section.Id == "changed-objects").Lines));
    }

    /// <summary>The city mod needs the cleaned mod and holds the crate at the cleaned mod's place or moves it again; the detailed line names the plugin whose place the crate stands at.</summary>
    [Theory]
    [InlineData(false, FixtureWorld.TargetName)]
    [InlineData(true, CityPlugin)]
    public void TheDetailedLineNamesThePluginWhosePlaceTheKeptObjectStandsAt(bool cityMovesItAgain, string namedPlugin)
    {
        var settings = CrateSettings();
        settings.DetailedLogAndReports = true;

        var (_, run) = FixtureRun.Capture(settings, Workers, dataFolder => CreateCrateWorldCopiedByACityPluginThatNeedsTheCleanedMod(dataFolder, cityMovesItAgain));

        Assert.Contains(run.Log, line => line.StartsWith($"  Kept where {namedPlugin} put it: B_Crate ", StringComparison.Ordinal));
    }

    /// <summary>The crate world with the city mod having the cleaned mod as a master and a copy of the moved crate, moved again or not.</summary>
    private static IReadOnlyList<ISkyrimMod> CreateCrateWorldCopiedByACityPluginThatNeedsTheCleanedMod(string dataFolder, bool moveAgain)
    {
        var world = CreateWorld(dataFolder);
        var crate = world.BaseGame.Interior.Place("B_Crate", world.BaseGame.AddModeledStatic("B_CrateBase", FixtureMeshes.CrateModel), Original);
        var moved = world.Target.Interior.PlaceCopyOf(crate);
        MoveCopy(moved, MovedTo);
        world.City.Mod.ModHeader.MasterReferences.Add(new MasterReference { Master = world.Target.Key });
        var copy = world.City.Interior.PlaceCopyOf(moved);
        if (moveAgain) MoveCopy(copy, MovedTo + new Vector3(0, 500, 0));
        return world.LoadOrder;
    }

    [Fact]
    public void ARunWithoutChangedObjectsPrintsNoChangedObjectsLine()
    {
        var log = new RecordingLogSink();
        FixtureRun.Run(CrateSettings(), Workers, log, OpenMeshFiles, createLoadOrder: dataFolder => CreateCrateWorld(dataFolder, Original, []));

        Assert.DoesNotContain(log.Sections, section => section.Id == "changed-objects");
    }

    [Fact]
    public void TheRemovalSummaryCountsTheRemovedMovedObjectAsOneTheCleanedModChanged()
    {
        var log = new RecordingLogSink();
        FixtureRun.Run(CrateSettings().WithMovedObjects(RestoreMode.Remove), Workers, log, OpenMeshFiles, createLoadOrder: dataFolder => CreateCrateWorld(dataFolder, MovedTo, [NearMovedTo]));

        Assert.EndsWith("; 0 of them Target.esp objects, 1 objects of other mods that Target.esp changed.", Assert.Single(log.Sections.Single(section => section.Id == "removal-summary").Lines));
    }

    [Fact]
    public void ASectionCollisionLikeTheStandardOneGivesOneFoundLineInTheTooCloseAndTheChangedTerrainStep()
    {
        var log = RunWithTerrainOn(settings => { });

        Assert.Single(FoundLines(log, "tooClose"));
        Assert.Single(FoundLines(log, "changedTerrain"));
    }

    [Fact]
    public void ASectionCollisionOtherThanTheStandardOneGivesTwoFoundLinesInBothStepsEachNamingItsObjects()
    {
        var log = RunWithTerrainOn(settings =>
        {
            var changed = settings.Standard.ChangedObjects.Collision;
            changed.TooClose = new TooCloseRemovalSettings
            {
                BySize = new TooCloseBySizeSettings { Use = true, SizeMultiplier = 0.1f },
                ByDistance = new TooCloseByDistanceSettings { Use = true, DistanceInUnits = 33f },
            };
            changed.TerrainChange = new TerrainChangeSettings { RemoveOnChangedTerrain = true, HeightChangeTolerance = 20f };
        });

        var tooClose = FoundLines(log, "tooClose");
        Assert.Equal(2, tooClose.Count);
        Assert.StartsWith("Found 0 of 1 Target.esp objects too close", tooClose[0]);
        Assert.StartsWith("Found 1 of 1 objects of other mods that Target.esp moved too close", tooClose[1]);
        Assert.EndsWith("; 1 of them restored.", tooClose[1]);
        Assert.Equal(
            [
                "Found 0 of 1 Target.esp objects on terrain changed by other mods.",
                "Found 0 of 1 objects of other mods that Target.esp moved on terrain changed by other mods.",
            ],
            FoundLines(log, "changedTerrain"));
    }

    private static RecordingLogSink RunWithTerrainOn(Action<Settings> change)
    {
        var settings = CrateSettings();
        settings.Standard.TerrainChange = new TerrainChangeSettings { RemoveOnChangedTerrain = true };
        settings.WithTheCollisionOfTheCleanedModsObjects();
        change(settings);
        var log = new RecordingLogSink();
        FixtureRun.Run(settings, Workers, log, OpenMeshFiles, createLoadOrder: dataFolder => CreateCrateWorld(dataFolder, MovedTo, [NearMovedTo]));
        return log;
    }

    private static List<string> FoundLines(RecordingLogSink log, string sectionId) =>
        [.. log.Sections.Single(section => section.Id == sectionId).Lines.Where(line => line.StartsWith("Found ", StringComparison.Ordinal))];

    [Fact]
    public void TheTooCloseLineAndTheRunSummaryCountTheMovedReplacementBesideTheCleanedModsOwnObject()
    {
        var log = new RecordingLogSink();
        FixtureRun.Run(
            RemoveAfterResizing(ShrinkSettings()),
            Workers,
            log,
            OpenMeshFiles,
            createLoadOrder: dataFolder => CreateCrateWorld(dataFolder, MovedTo, [NearMovedTo]));

        Assert.Contains(
            " of 2 objects (1 Target.esp objects and 1 objects of other mods that Target.esp moved) ",
            Assert.Single(log.Sections.Single(section => section.Id == "tooClose").Lines, line => line.StartsWith("Found ")));
        Assert.Contains("judged in this run: 1;", Assert.Single(log.Sections.Single(section => section.Id == "run-summary").Lines));
    }

    /// <summary>The crate world, and a second base-game crate far from it that the cleaned mod gives a building's model without moving it.</summary>
    private static IReadOnlyList<ISkyrimMod> CreateMovedAndReplacedWorld(string dataFolder)
    {
        var world = FillCrateWorld(CreateWorld(dataFolder), MovedTo, [NearMovedTo]);
        var second = world.BaseGame.Interior.Place("B_SecondCrate", world.BaseGame.AddModeledStatic("B_SecondCrateBase", FixtureMeshes.CrateModel), SecondCrate);
        world.Target.Interior.PlaceCopyOf(second).Base.SetTo(world.Target.AddModeledStatic("T_SecondBuildingBase", FixtureMeshes.BuildingModel));
        world.City.Interior.Place("C_SecondWall", world.City.AddModeledStatic("C_SecondWallBase", FixtureMeshes.PadModel), SecondCrate + new Vector3(250, 0, 0));
        return world.LoadOrder;
    }
}
