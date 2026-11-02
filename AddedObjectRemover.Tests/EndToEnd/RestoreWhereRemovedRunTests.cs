using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// Whole runs on small worlds where the cleaned mod's objects are removed from a whole area, so what the cleaned mod changed there
/// goes back: the saved expected-output files hold the log, the patch (placed records, cell settings, five rows of the land's
/// heights, navmeshes and navmesh info maps) and both report files of each run.
/// </summary>
public class RestoreWhereRemovedRunTests
{
    private const int Workers = 4;
    private const float SmallSearchRadius = 1500f;

    private static readonly int[] OtherWorkerCounts = [2, 8];

    [Fact]
    public void AClearedVillageGetsBackWhatTheCleanedModChangedAndKeepsWhatALaterPluginChangedAndWhatWouldNotFit()
    {
        var (_, run) = Run(SettingsOf(), RestoreWhereRemovedWorlds.CreateVillage);

        ExpectedOutputFiles.AssertMatches("restore-where-removed/Village", run.ToExpectedOutputLines());
    }

    [Fact]
    public void WithTheGroupOffTheVillageKeepsEverythingTheCleanedModChangedAndPrintsAndWritesNothingAboutIt()
    {
        var (_, run) = Run(SettingsOf(restore => SwitchEverythingOff(restore)), RestoreWhereRemovedWorlds.CreateVillage);

        Assert.Contains("Restore where the cleaned mod was removed: off.", run.Log);
        Assert.DoesNotContain(run.Log, line => line.Contains("where the cleaned mod was removed", StringComparison.Ordinal) && !line.EndsWith(": off.", StringComparison.Ordinal));
        Assert.DoesNotContain(run.Log, line => line.StartsWith("Restore where removed:", StringComparison.Ordinal));
        Assert.DoesNotContain(run.Reports, report => report.Name.StartsWith("restored-where-removed", StringComparison.Ordinal));
        ExpectedOutputFiles.AssertMatches("restore-where-removed/VillageGroupOff", run.ToExpectedOutputLines());
    }

    [Fact]
    public void ALandRaisedByTheCleanedModGoesBackInTheClearedQuartersAndIsBlendedIntoTheQuarterWhereACrateStays()
    {
        var (_, run) = Run(SettingsOf(restore => restore.ClearedArea.SearchRadius = SmallSearchRadius), RestoreWhereRemovedWorlds.CreateTerrain);

        ExpectedOutputFiles.AssertMatches("restore-where-removed/Terrain", run.ToExpectedOutputLines());
    }

    [Fact]
    public void AMalformedRecordTheRestoreStepReadsStopsOnlyThatStepAndTheOtherStepsAndTheirWritesStay()
    {
        var settings = SettingsOf();
        var healthy = FixtureRun.Execute(settings, Workers, RestoreWhereRemovedWorlds.CreateVillage);

        var outcome = ((RunResult.Done)FixtureRun.Run(
            settings,
            Workers,
            new RecordingLogSink(),
            (dataFolder, release, listedPlugins) => new MeshFilesFactory(dataFolder, release, listedPlugins),
            plugin => new MalformedLandWorldspacePlugin(plugin),
            createLoadOrder: RestoreWhereRemovedWorlds.CreateVillage)).Outcome;

        Assert.NotEmpty(healthy.RestoreWhereRemoved.Objects);
        Assert.Equal(StepOutcome.StoppedByUnexpectedError, outcome.RestoreWhereRemoved.Outcome);
        var failure = Assert.Single(outcome.RestoreWhereRemoved.Failures);
        Assert.Equal(RestoreWhereRemovedParts.Step, failure.Part);
        Assert.StartsWith(nameof(MalformedRecordException), failure.Failure, StringComparison.Ordinal);
        Assert.Empty(outcome.RestoreWhereRemoved.Objects);
        Assert.NotEmpty(outcome.Removals);
        Assert.Equal(healthy.Removals.Select(removal => removal.TargetIndex), outcome.Removals.Select(removal => removal.TargetIndex));
        Assert.Equal(healthy.Written.Removed.Select(id => id.Index), outcome.Written.Removed.Select(id => id.Index));
        Assert.Equal([failure], FailuresOf(outcome));
        AssertSameAsHealthy(healthy, outcome);
    }

    private static IEnumerable<PartFailure> FailuresOf(RunOutcome outcome) =>
        outcome.RestoreWhereRemoved.Failures
            .Concat(new[]
            {
                outcome.RestingObjects.Failure,
                outcome.RestingObjects.TouchChainsFailure,
                outcome.VisibleLeftBehind.Failure,
                outcome.LeftBehind.Failure,
                outcome.MarkerMoves.Failure,
                outcome.OtherModsObjects.Failure,
            }.OfType<PartFailure>());

    private static void AssertSameAsHealthy(RunOutcome healthy, RunOutcome outcome)
    {
        Assert.Equal(healthy.RestingObjects.Outcome, outcome.RestingObjects.Outcome);
        Assert.Equal(healthy.RestingObjects.SeedRounds.ToArray(), outcome.RestingObjects.SeedRounds.ToArray());
        Assert.Equal(healthy.RestingObjects.Rounds.ToArray(), outcome.RestingObjects.Rounds.ToArray());
        Assert.Equal(healthy.OtherModsObjects.Outcome, outcome.OtherModsObjects.Outcome);
        Assert.Equal(healthy.OtherModsObjects.Objects.ToArray(), outcome.OtherModsObjects.Objects.ToArray());
        Assert.Equal(healthy.OtherModsObjects.Rounds.ToArray(), outcome.OtherModsObjects.Rounds.ToArray());
        Assert.Equal(healthy.MarkerMoves.Outcome, outcome.MarkerMoves.Outcome);
        Assert.Equal(healthy.MarkerMoves.Moves.ToArray(), outcome.MarkerMoves.Moves.ToArray());
    }

    [Fact]
    public void ANavmeshTheCleanedModDeletedComesBackWithItsEntryAndItsNeighbourLinksToItAgain()
    {
        var (_, run) = Run(SettingsOf(), dataFolder => NavmeshWorld.Create(dataFolder, NavmeshScenario.DeletedWithoutReplacement));

        ExpectedOutputFiles.AssertMatches("restore-where-removed/NavmeshBroughtBack", run.ToExpectedOutputLines());
    }

    [Fact]
    public void ANavmeshTheCleanedModDeletedComesBackWithTheMapEntryTheCleanedModChanged()
    {
        var (_, run) = Run(SettingsOf(), dataFolder => NavmeshWorld.Create(dataFolder, NavmeshScenario.DeletedWithItsMapEntryChanged));

        ExpectedOutputFiles.AssertMatches("restore-where-removed/NavmeshBroughtBackWithItsEntry", run.ToExpectedOutputLines());
    }

    [Fact]
    public void ANavmeshTheCleanedModReplacedComesBackWithItsDoorAndItsReplacementIsSunkUnderTheWorld()
    {
        var (_, run) = Run(SettingsOf(), dataFolder => NavmeshWorld.Create(dataFolder, NavmeshScenario.Replaced));

        ExpectedOutputFiles.AssertMatches("restore-where-removed/NavmeshReplacementSunk", run.ToExpectedOutputLines());
    }

    /// <summary>
    /// The cleaned mod moved and replaced nothing; a rock it disabled is still checked against the table a plugin that requires the
    /// cleaned mod placed where the rock stood, and stays disabled.
    /// </summary>
    [Fact]
    public void AnObjectOfAPluginThatRequiresTheCleanedModKeepsARockOutOfItInARunWithoutMovedOrReplacedObjects()
    {
        var (_, run) = Run(SettingsOf(), RestoreWhereRemovedWorlds.CreateVillageWithAPatchOfTheCleanedMod);

        ExpectedOutputFiles.AssertMatches("restore-where-removed/VillageWithAPatchOfTheCleanedMod", run.ToExpectedOutputLines());
        var objects = Assert.Single(run.Reports, report => report.Name == "restored-where-removed-objects.csv");
        var rock = Assert.Single(objects.Lines, line => line.Contains(",B_Rock,", StringComparison.Ordinal));
        Assert.EndsWith($":{RestoreWhereRemovedWorlds.PatchOfTheCleanedModPlugin}])", rock, StringComparison.Ordinal);
        Assert.Contains(",kept: too close to objects that stay (", rock, StringComparison.Ordinal);
        Assert.DoesNotContain(run.Log, line => line.StartsWith("  Shown again: B_Rock", StringComparison.Ordinal));
    }

    /// <summary>
    /// The changed objects' lines and file say what that step decided for the crate the cleaned mod moved, kept where it put it, and
    /// that it was then restored where the cleaned mod was removed, with the place the patch writes.
    /// </summary>
    [Fact]
    public void AnObjectKeptWhereTheCleanedModPutItAndThenRestoredByItsAreaIsNamedSoInTheChangedObjectsLinesAndFile()
    {
        var (_, run) = Run(SettingsOf(), RestoreWhereRemovedWorlds.CreateVillage);

        Assert.Contains(run.Log, line => line.Contains("kept where Target.esp put them: 1 (1 then restored where the cleaned mod was removed);", StringComparison.Ordinal));
        Assert.Contains(run.Log, line => line.StartsWith("  Kept where Target.esp put it, then restored where the cleaned mod was removed: B_Mover ", StringComparison.Ordinal));
        var changed = Assert.Single(run.Reports, report => report.Name == "changed-and-fitted-objects.csv");
        Assert.Contains(
            "moved,\"changed object kept where moved, then restored where the cleaned mod was removed\",00080C:Skyrim.esm,B_Mover,B_CrateBase [000809:Skyrim.esm],AorInterior,3000,600,0,0,1,3000,0,0,0,1,",
            changed.Lines);
    }

    /// <summary>
    /// The worldspace of the navmesh the cleaned mod deleted holds no target and no record of another plugin the cleaned mod changed:
    /// its only object there starts disabled. The detailed log and the cells file still name that worldspace for the navmesh.
    /// </summary>
    [Fact]
    public void ANavmeshInASpaceWhereTheCleanedModsOnlyObjectStartsDisabledIsNamedWithItsSpace()
    {
        var (outcome, run) = Run(SettingsOf(), dataFolder => NavmeshWorld.Create(dataFolder, NavmeshScenario.DeletedWhereItsOnlyObjectStartsDisabled));

        var navmeshes = outcome.Collected.ChangedNavmeshes;
        Assert.NotEmpty(navmeshes);
        Assert.DoesNotContain(outcome.Collected.Targets, target => navmeshes.Any(navmesh => navmesh.SpaceKey == target.SpaceKey));
        Assert.Empty(outcome.Collected.ChangedRecords);
        Assert.All(
            navmeshes,
            navmesh => Assert.Contains(run.Log, line => line.Contains($"avmesh {navmesh.Navmesh} in worldspace {navmesh.SpaceKey}, cell {navmesh.Cell}: ", StringComparison.Ordinal)));
        var cells = Assert.Single(run.Reports, report => report.Name == "restored-where-removed-cells.csv");
        Assert.All(navmeshes, navmesh => Assert.Contains(cells.Lines, line => line.Contains($"worldspace {navmesh.SpaceKey}", StringComparison.Ordinal)));
    }

    [Fact]
    public void ANavmeshACityPluginEditedAgainStaysAsTheCleanedModMadeItAndNothingOfItsGroupIsWritten()
    {
        var (_, run) = Run(SettingsOf(), dataFolder => NavmeshWorld.Create(dataFolder, NavmeshScenario.EditedAgainByTheCityMod));

        ExpectedOutputFiles.AssertMatches("restore-where-removed/NavmeshRefused", run.ToExpectedOutputLines());
    }

    [Fact]
    public void OneTwoAndEightWorkersGiveTheSameOutputForTheVillageTheTerrainAndTheNavmeshes()
    {
        Func<string, IReadOnlyList<ISkyrimMod>>[] worlds =
        [
            RestoreWhereRemovedWorlds.CreateVillage,
            RestoreWhereRemovedWorlds.CreateVillageWithAPatchOfTheCleanedMod,
            RestoreWhereRemovedWorlds.CreateTerrain,
            dataFolder => NavmeshWorld.Create(dataFolder, NavmeshScenario.Replaced),
        ];

        foreach (var world in worlds)
        {
            var settings = SettingsOf(restore => restore.ClearedArea.SearchRadius = SmallSearchRadius);
            var oneWorker = Run(settings, world, workers: 1).Run.ToExpectedOutputLines();
            Assert.All(OtherWorkerCounts, workers => Assert.Equal(oneWorker, Run(settings, world, workers).Run.ToExpectedOutputLines()));
        }
    }

    private static (RunOutcome Outcome, RunOutput Run) Run(Settings settings, Func<string, IReadOnlyList<ISkyrimMod>> createLoadOrder, int workers = Workers) =>
        FixtureRun.Capture(settings, workers, createLoadOrder, earlierPatcherWrites: null, AreaPatchDump.Describe);

    private static void SwitchEverythingOff(RestoreWhereRemovedSettings restore)
    {
        restore.RestoreOtherModsObjects = false;
        restore.RestoreTerrain = false;
        restore.RestoreCells = false;
        restore.RestoreNavmeshes = false;
    }

    /// <summary>The cleaned mod's objects that stand too close to a city pad are removed; the left-behind step stays off so only the pads remove anything.</summary>
    private static Settings SettingsOf(Action<RestoreWhereRemovedSettings>? changeRestore = null)
    {
        var settings = new Settings
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
                LeftoverVisibleObjects = new LeftoverVisibleObjectSettings { RemoveLeftoverVisibleObjects = false },
            },
            DetailedLogAndReports = true,
        }.WithTheCollisionOfTheCleanedModsObjects();
        changeRestore?.Invoke(settings.Standard.RestoreWhereRemoved);
        return settings;
    }
}
