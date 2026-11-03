using System.Text.RegularExpressions;
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
    internal const float SmallSearchRadius = 1500f;
    private const string CellsFile = ReportFileNames.RestoredWhereRemovedCellsFileName;

    private static readonly int[] OtherWorkerCounts = [2, 8];

    [Fact]
    public void AClearedVillageGetsBackWhatTheCleanedModChangedAndKeepsWhatALaterPluginChangedAndWhatWouldNotFit()
    {
        var (_, run) = Run(SettingsOf(), RestoreWhereRemovedWorlds.CreateVillage);

        ExpectedOutputFiles.AssertMatches("restore-where-removed/Village", run.ToExpectedOutputLines());
    }

    /// <summary>The saved expected-output files drop the step-time lines, so the run's lines are read unmasked here.</summary>
    [Fact]
    public void TheDetailedLogTimesEachMainPartOfTheStepOnItsOwnStepTimeLine()
    {
        var log = new RecordingLogSink();
        FixtureRun.Run(
            SettingsOf(),
            Workers,
            log,
            (dataFolder, release, listedPlugins) => new MeshFilesFactory(dataFolder, release, listedPlugins),
            createLoadOrder: RestoreWhereRemovedWorlds.CreateVillage);

        var parts = log.Sections
            .SelectMany(section => section.Lines)
            .Where(line => line.StartsWith("Step time: Restore where removed, ", StringComparison.Ordinal))
            .Select(line => Regex.Match(line, @"^Step time: Restore where removed, (?<part>.+) in \d+\.\d+s\.$").Groups["part"].Value);
        Assert.Equal(
            [
                "areas judged and plans made",
                "world after removal",
                "objects that stay and guards set up, with the first collision layer read",
                "passes",
                "placed guards in the passes",
            ],
            parts);
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
        var (_, run) = Run(TerrainSettings(), RestoreWhereRemovedWorlds.CreateTerrain);

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
        var leftovers = healthy.Removals.OfType<ObjectWithReason.LeftoverInRestoredArea>().Select(removal => removal.TargetIndex).ToHashSet();
        Assert.NotEmpty(leftovers);
        Assert.Equal(
            healthy.Removals.Where(removal => !leftovers.Contains(removal.TargetIndex)).Select(removal => removal.TargetIndex),
            outcome.Removals.Select(removal => removal.TargetIndex));
        Assert.Equal(
            healthy.Written.Removed.Select(id => id.Index).Where(index => !leftovers.Contains(index)),
            outcome.Written.Removed.Select(id => id.Index));
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
        var (_, run) = Run(SettingsOf(), NavmeshAndLand(NavmeshAndLandScenario.DeletedWithoutReplacement));

        ExpectedOutputFiles.AssertMatches("restore-where-removed/NavmeshBroughtBack", run.ToExpectedOutputLines());
    }

    [Fact]
    public void ANavmeshTheCleanedModDeletedComesBackWithTheMapEntryTheCleanedModChanged()
    {
        var (_, run) = Run(SettingsOf(), NavmeshAndLand(NavmeshAndLandScenario.DeletedWithItsMapEntryChanged));

        ExpectedOutputFiles.AssertMatches("restore-where-removed/NavmeshBroughtBackWithItsEntry", run.ToExpectedOutputLines());
    }

    [Fact]
    public void ANavmeshTheCleanedModReplacedComesBackWithItsDoorAndItsReplacementIsSunkUnderTheWorld()
    {
        var (_, run) = Run(SettingsOf(), NavmeshAndLand(NavmeshAndLandScenario.Replaced));

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
        var objects = Assert.Single(run.Reports, report => report.Name == ReportFileNames.RestoredWhereRemovedObjectsFileName);
        var rock = Assert.Single(objects.Lines, line => line.Contains(",B_Rock,", StringComparison.Ordinal));
        Assert.EndsWith($":{RestoreWhereRemovedWorlds.PatchOfTheCleanedModPlugin}])", rock, StringComparison.Ordinal);
        Assert.Contains(",kept: too close to objects that stay (", rock, StringComparison.Ordinal);
        Assert.DoesNotContain(run.Log, line => line.StartsWith("  Shown again: B_Rock", StringComparison.Ordinal));
    }

    [Fact]
    public void AnObjectTheCleanedModDisabledStaysDisabledWhenAnObjectOfAnAddOnOfAPatchOfTheCleanedModStandsBesideIt()
    {
        var (_, run) = Run(SettingsOf(), RestoreWhereRemovedWorlds.CreateVillageWithATableOfAnotherModWhereARockStood);

        ExpectedOutputFiles.AssertMatches("restore-where-removed/VillageWithATableOfAnotherMod", run.ToExpectedOutputLines());
        var objects = Assert.Single(run.Reports, report => report.Name == ReportFileNames.RestoredWhereRemovedObjectsFileName);
        var rock = Assert.Single(objects.Lines, line => line.Contains(",B_Rock,", StringComparison.Ordinal));
        Assert.Contains(",kept: too close to objects that stay (A_Table ", rock, StringComparison.Ordinal);
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
        var (outcome, run) = Run(SettingsOf(), NavmeshAndLand(NavmeshAndLandScenario.DeletedWhereItsOnlyObjectStartsDisabled));

        var navmeshes = outcome.Collected.ChangedNavmeshes;
        Assert.NotEmpty(navmeshes);
        Assert.DoesNotContain(outcome.Collected.Targets, target => navmeshes.Any(navmesh => navmesh.SpaceKey == target.SpaceKey));
        Assert.Empty(outcome.Collected.ChangedRecords);
        Assert.All(
            navmeshes,
            navmesh => Assert.Contains(run.Log, line => line.Contains($"avmesh {navmesh.Navmesh} in worldspace {navmesh.SpaceKey}, cell {navmesh.Cell}: ", StringComparison.Ordinal)));
        var cells = RowsOf(run, CellsFile);
        Assert.All(navmeshes, navmesh => Assert.Contains(cells, row => row["space"] == $"worldspace {navmesh.SpaceKey}"));
    }

    [Fact]
    public void ANavmeshACityPluginEditedAgainStaysAsTheCleanedModMadeItAndNothingOfItsGroupIsWritten()
    {
        var (_, run) = Run(SettingsOf(), NavmeshAndLand(NavmeshAndLandScenario.EditedAgainByTheCityMod));

        ExpectedOutputFiles.AssertMatches("restore-where-removed/NavmeshRefused", run.ToExpectedOutputLines());
    }

    [Fact]
    public void ALandTheCleanedModRaisedInACellOffTheDiagonalGoesBackInItsQuartersExceptWhereALaterPluginRaisedItAgain()
    {
        var (_, run) = Run(SettingsOf(), NavmeshAndLand(NavmeshAndLandScenario.LandRaisedWithABlockRaisedAgainByTheCityMod));

        ExpectedOutputFiles.AssertMatches("restore-where-removed/TerrainOffTheDiagonal", run.ToExpectedOutputLines());
        AssertEveryQuarterGoesBackAndTheNorthEastLeaves(run, "25 heights (CityMod.esp)");
        Assert.Contains(" 0 0 0 0 0 ", LandRow(run, 16), StringComparison.Ordinal);
        Assert.Contains(" 96 96 96 96 96 ", LandRow(run, 24), StringComparison.Ordinal);
    }

    /// <summary>
    /// The city mod's block is wider than it is tall and lies off the diagonal of the land's points, so a swap of columns and rows in
    /// the quarters or in the points that go back would move the heights written.
    /// </summary>
    [Fact]
    public void ALandBlockALaterPluginRaisedAgainOffThePointDiagonalStaysWhereItLiesAndTheRestOfItsQuarterGoesBack()
    {
        var (_, run) = Run(SettingsOf(), NavmeshAndLand(NavmeshAndLandScenario.LandRaisedWithANarrowBlockOffThePointDiagonalRaisedAgainByTheCityMod));

        AssertEveryQuarterGoesBackAndTheNorthEastLeaves(run, "12 heights (CityMod.esp)");
        Assert.Contains(" 0 24 40 96 96 96 96 40 24 0 ", LandRow(run, 24), StringComparison.Ordinal);
        Assert.DoesNotContain(" 96 ", LandRow(run, 16), StringComparison.Ordinal);
    }

    /// <summary>The city mod changed every point of the land again, so a later plugin has the last word on all of it: no quarter is judged and no land is written.</summary>
    [Fact]
    public void ALandALaterPluginChangedAgainEverywhereIsNeitherJudgedNorWritten()
    {
        var (outcome, run) = Run(SettingsOf(), NavmeshAndLand(NavmeshAndLandScenario.LandRaisedFurtherEverywhereByTheCityMod));

        Assert.NotEmpty(outcome.Collected.ChangedLands);
        Assert.DoesNotContain(run.Reports.Where(report => report.Name == CellsFile).SelectMany(ReportRows.Of), row => row["kind"] == "quarter");
        Assert.DoesNotContain(run.Patch, line => line.StartsWith("land ", StringComparison.Ordinal));
    }

    [Fact]
    public void ANavmeshTheCleanedModEditedGoesBackToItsEarlierShapeAndOneACityPluginEditedAgainStays()
    {
        IReadOnlyList<ISkyrimMod> mods = [];
        Dictionary<string, string> patchGeometry = [];
        var (_, run) = FixtureRun.Capture(
            SettingsOf(),
            Workers,
            dataFolder => mods = NavmeshAndLandWorld.Create(dataFolder, NavmeshAndLandScenario.EditedWithAnUnlinkedOneEditedAgainByTheCityMod),
            earlierPatcherWrites: null,
            patch =>
            {
                patchGeometry = GeometryByKey(patch);
                return AreaPatchDump.Describe(patch);
            });

        ExpectedOutputFiles.AssertMatches("restore-where-removed/NavmeshEditedOneRestoredOneKept", run.ToExpectedOutputLines());
        var navmeshes = RowsOf(run, CellsFile).Where(row => row["kind"] == "navmesh").ToList();
        Assert.Equal(2, navmeshes.Count);
        var restored = Assert.Single(navmeshes, row => row["result"] == "restored");
        Assert.Equal(("shape", "", "Cleared"), (restored["undone"], restored["leftToLaterPlugins"], restored["verdict"]));
        var kept = Assert.Single(navmeshes, row => row["result"].StartsWith("kept: changed again by a later plugin", StringComparison.Ordinal));
        Assert.Equal("shape (CityMod.esp)", kept["leftToLaterPlugins"]);
        Assert.Equal(new[] { restored["navmesh"] }, patchGeometry.Keys);
        Assert.Equal(GeometryByKey(mods[0])[restored["navmesh"]], patchGeometry[restored["navmesh"]]);
        Assert.Single(run.Patch, line => line.StartsWith($"navmesh {restored["navmesh"]}:", StringComparison.Ordinal));
        Assert.DoesNotContain(run.Patch, line => line.StartsWith($"navmesh {kept["navmesh"]}:", StringComparison.Ordinal));
    }

    [Fact]
    public void OneTwoAndEightWorkersGiveTheSameOutputForTheVillageTheTerrainTheLandAndTheNavmeshes()
    {
        (Settings Settings, Func<string, IReadOnlyList<ISkyrimMod>> World)[] worlds =
        [
            (SettingsOf(), RestoreWhereRemovedWorlds.CreateVillage),
            (SettingsOf(), RestoreWhereRemovedWorlds.CreateVillageWithAPatchOfTheCleanedMod),
            (TerrainSettings(), RestoreWhereRemovedWorlds.CreateTerrain),
            (SettingsOf(), NavmeshAndLand(NavmeshAndLandScenario.Replaced)),
            (SettingsOf(), NavmeshAndLand(NavmeshAndLandScenario.LandRaisedWithABlockRaisedAgainByTheCityMod)),
            (SettingsOf(), NavmeshAndLand(NavmeshAndLandScenario.EditedWithAnUnlinkedOneEditedAgainByTheCityMod)),
        ];

        foreach (var (settings, world) in worlds)
        {
            var oneWorker = Run(settings, world, workers: 1).Run.ToExpectedOutputLines();
            Assert.All(OtherWorkerCounts, workers => Assert.Equal(oneWorker, Run(settings, world, workers).Run.ToExpectedOutputLines()));
        }
    }

    internal static (RunOutcome Outcome, RunOutput Run) Run(Settings settings, Func<string, IReadOnlyList<ISkyrimMod>> createLoadOrder, int workers = Workers) =>
        FixtureRun.Capture(settings, workers, createLoadOrder, earlierPatcherWrites: null, AreaPatchDump.Describe);

    private static Func<string, IReadOnlyList<ISkyrimMod>> NavmeshAndLand(NavmeshAndLandScenario scenario) =>
        dataFolder => NavmeshAndLandWorld.Create(dataFolder, scenario);

    private static List<Dictionary<string, string>> RowsOf(RunOutput run, string fileName) =>
        ReportRows.Of(Assert.Single(run.Reports, report => report.Name == fileName));

    /// <summary>
    /// The cell's four quarters, south-west, south-east, north-west and north-east in that order, are cleared and go back; only the
    /// north-east one leaves heights to later plugins.
    /// </summary>
    private static void AssertEveryQuarterGoesBackAndTheNorthEastLeaves(RunOutput run, string leftToLaterPlugins)
    {
        var quarters = RowsOf(run, CellsFile).Where(row => row["kind"] == "quarter").ToList();
        Assert.Equal(["south-west", "south-east", "north-west", "north-east"], quarters.Select(quarter => quarter["quarter"]));
        Assert.All(quarters, quarter => Assert.Equal(("Cleared", "restored"), (quarter["verdict"], quarter["result"])));
        Assert.Equal(["", "", "", leftToLaterPlugins], quarters.Select(quarter => quarter["leftToLaterPlugins"]));
    }

    /// <summary>The patch's line of the land's heights in the row, one of those <see cref="AreaPatchDump"/> shows.</summary>
    private static string LandRow(RunOutput run, int row) => Assert.Single(run.Patch, line => line.Contains($" row {row}: ", StringComparison.Ordinal));

    /// <returns>By navmesh key, every vertex, every triangle with its edges and flags, and every edge link of each navmesh.</returns>
    private static Dictionary<string, string> GeometryByKey(ISkyrimModGetter mod) =>
        mod.EnumerateMajorRecords<INavigationMeshGetter>().ToDictionary(navmesh => navmesh.FormKey.ToString(), GeometryOf);

    private static string GeometryOf(INavigationMeshGetter navmesh) =>
        navmesh.Data is not { } data
            ? "no data"
            : string.Join(
                " | ",
                string.Join("; ", data.Vertices),
                string.Join("; ", data.Triangles.Select(triangle => $"{triangle.Vertices} {triangle.EdgeLink_0_1} {triangle.EdgeLink_1_2} {triangle.EdgeLink_2_0} {triangle.Flags}")),
                string.Join("; ", data.EdgeLinks.Select(link => $"{link.Mesh.FormKey} {link.TriangleIndex}")));

    private static Settings TerrainSettings() => SettingsOf(restore => restore.ClearedArea.SearchRadius = SmallSearchRadius);

    private static void SwitchEverythingOff(RestoreWhereRemovedSettings restore)
    {
        restore.RestoreOtherModsObjects = false;
        restore.RestoreTerrain = false;
        restore.RestoreCells = false;
        restore.RestoreNavmeshes = false;
    }

    /// <summary>The cleaned mod's objects that stand too close to a city pad are removed; the left-behind step stays off so only the pads remove anything.</summary>
    internal static Settings SettingsOf(Action<RestoreWhereRemovedSettings>? changeRestore = null)
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
