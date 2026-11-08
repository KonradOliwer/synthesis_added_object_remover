using System.Numerics;
using System.Text.RegularExpressions;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// Whole runs on small worlds where the cleaned mod's objects are removed from a whole area, so what the cleaned mod changed there
/// goes back: the saved expected-output files hold the log, the patch (placed records, cell settings, five rows of the land's
/// heights, navmeshes and navmesh info maps) and both report files of each run.
/// </summary>
[ExpectedOutputTest]
public class RestoreWhereRemovedRunTests
{
    private const int Workers = 4;
    internal const float SmallSearchRadius = 1500f;

    /// <summary>
    /// The small worlds hold only a few objects of the cleaned mod: one tiny object standing among two removed ones is a third of them
    /// by number, just under this, while one standing beside one removed is a half and keeps its area.
    /// </summary>
    private const float OneInThreeMayStand = 34f;
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
                "plans read in the passes",
                "follow-up of what went back in the passes",
                "objects that stand read in the passes",
                "placed guards in the passes",
                "changed-terrain check in the passes",
                "navmesh guards in the passes",
                "ground protected in the passes",
                "water levels kept in the passes",
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

    /// <summary>
    /// Only terrain goes back, and the cleaned mod changed no placed record of another plugin. A trusted plugin's shrine stands on the
    /// ground the cleaned mod lowered, in a cleared quarter: the ground going back up would bury it, so the ground under it stays
    /// lowered while the rest of the quarter goes back up.
    /// </summary>
    [Fact]
    public void InATerrainOnlyRunTheGroundUnderATrustedShrinePlacedOnTheLoweredGroundStaysLowered()
    {
        var settings = SettingsOf(restore =>
        {
            restore.ClearedArea.SearchRadius = SmallSearchRadius;
            restore.RestoreOtherModsObjects = false;
            restore.RestoreCells = false;
            restore.RestoreNavmeshes = false;
        });
        settings.Standard.Trust.TrustedModsAndTheirMasters = [ModKey.FromNameAndExtension(RestoreWhereRemovedWorlds.ShrinePlugin)];

        var (outcome, _) = Run(settings, RestoreWhereRemovedWorlds.CreateLoweredTerrainWithAShrine);

        Assert.Empty(outcome.Collected.ChangedRecords);
        var shrine = Assert.Single(outcome.Collected.SupportOnlyObjects, other => other.WinningMod == new PluginName(RestoreWhereRemovedWorlds.ShrinePlugin));
        Assert.NotNull(shrine.ChangeSinceBeforeCleanedMod);
        var land = Assert.Single(outcome.RestoreWhereRemoved.Lands);
        var underShrine = LandPoint(ShrineVertex, ShrineVertex);
        Assert.Contains(land.Kept, kept => kept.Reason == TerrainKeptReason.UnderOtherModsObject && kept.Points.Contains(underShrine));
        Assert.Equal(RestoreWhereRemovedWorlds.BaseLandSteps * RestoreWhereRemovedWorlds.UnitsPerLandStep, land.FinalHeights![underShrine], HeightTolerance);
        Assert.Equal(RestoreWhereRemovedWorlds.CleanedModLandSteps * RestoreWhereRemovedWorlds.UnitsPerLandStep, land.FinalHeights[LandPoint(4, 4)], HeightTolerance);
    }

    /// <summary>
    /// The cleaned mod's rock stands on the ground it raised. A trusted plugin that requires the cleaned mod moved the rock, and an
    /// earlier patcher of the group copied it into the patch, so the rock is left to that plugin. Around the south-west quarter's
    /// centre the cleaned mod's only other object, the crate in the middle of the cell, is removed. The rock counts as standing there,
    /// and with half of the objects allowed to stand the quarter is cleared: its ground goes back down, except under the rock.
    /// </summary>
    [Fact]
    public void AnObjectOfTheCleanedModALaterPluginHoldsCountsAsStandingAndKeepsTheGroundUnderIt()
    {
        var rock = default(FormKey);
        var settings = SettingsOf(restore =>
        {
            restore.ClearedArea.SearchRadius = SmallSearchRadius;
            restore.ClearedArea.MaxStandingCountPercent = MoreThanHalfMayStand;
            restore.ClearedArea.MaxStandingSizePercent = MoreThanHalfMayStand;
        });

        var (outcome, run) = FixtureRun.Capture(
            settings,
            Workers,
            dataFolder => RestoreWhereRemovedWorlds.CreateRaisedTerrainWithARockALaterPluginMoved(dataFolder, found => rock = found),
            state => state.LinkCache.ResolveContext<IPlacedObject, IPlacedObjectGetter>(rock).GetOrAddAsOverride(state.PatchMod),
            AreaPatchDump.Describe);

        var overridden = Assert.Single(outcome.Collected.CleanedModsObjectsOverriddenLater);
        Assert.Equal((rock.ToRecordKey(), RestoreWhereRemovedWorlds.MovedRockAt), (overridden.Key, overridden.Transform.Position));
        Assert.Contains("disabled False", Assert.Single(run.Patch, line => line.StartsWith($"{rock} in ", StringComparison.Ordinal)), StringComparison.Ordinal);
        var land = Assert.Single(outcome.RestoreWhereRemoved.Lands);
        var southWest = land.Quarters[TerrainQuadrants.IndexOf(0, 0)].Area;
        Assert.Equal((2, 1, AreaVerdict.Cleared), (southWest.Objects, southWest.Standing, southWest.Verdict));
        var underRock = LandPoint(RockColumn, RockRow);
        Assert.Contains(land.Kept, kept => kept.Reason == TerrainKeptReason.UnderObjectThatStays && kept.Points.Contains(underRock));
        Assert.Equal(RestoreWhereRemovedWorlds.CleanedModLandSteps * RestoreWhereRemovedWorlds.UnitsPerLandStep, land.FinalHeights![underRock], HeightTolerance);
        Assert.Equal(RestoreWhereRemovedWorlds.BaseLandSteps * RestoreWhereRemovedWorlds.UnitsPerLandStep, land.FinalHeights[LandPoint(4, 4)], HeightTolerance);
    }

    /// <summary>One of the cleaned mod's objects standing beside one removed is a half of them by number and by size, just under this.</summary>
    private const float MoreThanHalfMayStand = 60f;

    /// <summary>The land grid column under the rock's place.</summary>
    private static readonly int RockColumn = (int)(RestoreWhereRemovedWorlds.MovedRockAt.X / TerrainGrid.VertexSpacing);

    /// <summary>The land grid row under the rock's place.</summary>
    private static readonly int RockRow = (int)(RestoreWhereRemovedWorlds.MovedRockAt.Y / TerrainGrid.VertexSpacing);

    /// <summary>The land grid column and row of the shrine's place.</summary>
    private static readonly int ShrineVertex = (int)(RestoreWhereRemovedWorlds.ShrineAt.X / TerrainGrid.VertexSpacing);

    private static int LandPoint(int column, int row) => row * LandSide + column;

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
        Assert.Equal(StepOutcome.StoppedByUserDataFailure, outcome.RestoreWhereRemoved.Outcome);
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
                outcome.InvisibleLeftBehind.Failure,
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
        Assert.EndsWith($":{RestoreWhereRemovedWorlds.PatchOfTheCleanedModPlugin}]),,", rock, StringComparison.Ordinal);
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
    /// The top crate of the stack stays by its area and is restored by the follow-up of the bottom crate going back: the changed
    /// objects' lines and file name that outcome with the place the patch writes, and the patch holds the base game's record.
    /// </summary>
    [Fact]
    public void AnObjectKeptWhereTheCleanedModPutItAndThenRestoredByTheFollowUpIsNamedSoInTheChangedObjectsLinesAndFile()
    {
        var (outcome, run, written) = RunTheStack(TerrainSettings());

        var top = Assert.Single(outcome.Collected.Targets, target => target.EditorId == "B_StackTop");
        Assert.Contains(outcome.RestoreWhereRemoved.Objects, judged => judged.Change.Target == top.Id && !judged.IsRestored);
        Assert.NotNull(Assert.Single(outcome.RestoreWhereRemoved.FollowUp.Targets, followed => followed.Target == top.Id).RestoredTo);
        Assert.Contains(
            run.Log,
            line => line.Contains(
                "kept where Target.esp put them: 2 (1 then restored where the cleaned mod was removed, 1 then restored by the follow-up of what went back);",
                StringComparison.Ordinal));
        Assert.Contains(run.Log, line => line.StartsWith("  Kept where Target.esp put it, then restored by the follow-up of what went back: B_StackTop ", StringComparison.Ordinal));
        var row = StackTopRow(run);
        Assert.StartsWith("moved,\"changed object kept where moved, then restored by the follow-up of what went back\",", row, StringComparison.Ordinal);
        Assert.EndsWith($",{string.Join(",", CsvFormat.Numbers(RestoreWhereRemovedWorlds.StackTopFrom))},0,1,", row, StringComparison.Ordinal);
        var position = written(top.Key).Placement!.Position;
        Assert.Equal(RestoreWhereRemovedWorlds.StackTopFrom, new Vector3(position.X, position.Y, position.Z));
    }

    /// <summary>
    /// With moved objects removed instead of restored, the follow-up of the bottom crate going back removes the top crate: the changed
    /// objects' lines and file name that outcome with no new placement, and the patch disables the top crate.
    /// </summary>
    [Fact]
    public void AnObjectKeptWhereTheCleanedModPutItAndThenRemovedByTheFollowUpIsNamedSoInTheChangedObjectsLinesAndFile()
    {
        var settings = TerrainSettings();
        settings.Standard.ChangedObjects.Moved.WhenRemoved = RestoreMode.Remove;

        var (outcome, run, written) = RunTheStack(settings);

        var top = Assert.Single(outcome.Collected.Targets, target => target.EditorId == "B_StackTop");
        Assert.Null(Assert.Single(outcome.RestoreWhereRemoved.FollowUp.Targets, followed => followed.Target == top.Id).RestoredTo);
        Assert.Contains(run.Log, line => line.Contains("1 then removed by the follow-up of what went back", StringComparison.Ordinal));
        Assert.Contains(run.Log, line => line.StartsWith("  Kept where Target.esp put it, then removed by the follow-up of what went back: B_StackTop ", StringComparison.Ordinal));
        var row = StackTopRow(run);
        Assert.StartsWith("moved,\"changed object kept where moved, then removed by the follow-up of what went back\",", row, StringComparison.Ordinal);
        Assert.EndsWith(",,,,,,", row, StringComparison.Ordinal);
        Assert.NotEqual(0, written(top.Key).MajorRecordFlagsRaw & (int)SkyrimMajorRecord.SkyrimMajorRecordFlag.InitiallyDisabled);
    }

    /// <returns>The run, and the patch's placed record of a key.</returns>
    private static (RunOutcome Outcome, RunOutput Run, Func<RecordKey, IPlacedObjectGetter> Written) RunTheStack(Settings settings)
    {
        ISkyrimModGetter? patch = null;
        var (outcome, run) = FixtureRun.Capture(settings, Workers, RestoreWhereRemovedWorlds.CreateStackOfMovedCrates, describeMore: written =>
        {
            patch = written;
            return [];
        });
        return (outcome, run, key => patch!.EnumerateMajorRecords<IPlacedObjectGetter>().Single(record => record.FormKey.ToRecordKey() == key));
    }

    private static string StackTopRow(RunOutput run) =>
        Assert.Single(Assert.Single(run.Reports, report => report.Name == "changed-and-fitted-objects.csv").Lines, line => line.Contains(",B_StackTop,", StringComparison.Ordinal));

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

    /// <summary>
    /// An earlier patcher of the group wrote the cleaned mod's raised land with a square block of points raised again, so the block is
    /// the patch's own change: the block stays, and every other point of the cell goes back to the base game's height.
    /// </summary>
    [Fact]
    public void ALandAnEarlierPatcherRaisedASquareBlockOfAgainGoesBackExceptThatBlock() =>
        AssertEveryPointIsBackExceptTheBlock(NavmeshAndLandWorld.SquareBlock, "restore-where-removed/TerrainBlockOnTheDiagonal");

    /// <summary>
    /// As <see cref="ALandAnEarlierPatcherRaisedASquareBlockOfAgainGoesBackExceptThatBlock"/>, with a block wider than it is tall whose
    /// columns are none of its rows: swapping the columns and rows when the land is written moves the block.
    /// </summary>
    [Fact]
    public void ALandAnEarlierPatcherRaisedANarrowBlockOffThePointDiagonalOfAgainGoesBackExceptThatBlock() =>
        AssertEveryPointIsBackExceptTheBlock(NavmeshAndLandWorld.NarrowBlock, "restore-where-removed/TerrainOffTheDiagonal");

    /// <summary>Checks the run's output against the saved file, then the land point by point against the base game's land and the earlier patcher's block.</summary>
    private static void AssertEveryPointIsBackExceptTheBlock(NavmeshAndLandWorld.PointBlock block, string savedOutput)
    {
        IReadOnlyList<ISkyrimMod> mods = [];
        float[] withBlock = [];
        float[] written = [];
        var (_, run) = FixtureRun.Capture(
            SettingsOf(),
            Workers,
            dataFolder => mods = NavmeshAndLand(NavmeshAndLandScenario.LandRaised)(dataFolder),
            state =>
            {
                NavmeshAndLandWorld.RaiseBlockInPatch(state, mods, block);
                withBlock = HeightsOfTheLand(state.PatchMod);
            },
            patch =>
            {
                written = HeightsOfTheLand(patch);
                return AreaPatchDump.Describe(patch);
            });

        ExpectedOutputFiles.AssertMatches(savedOutput, run.ToExpectedOutputLines());
        AssertEveryPointIsBack(HeightsOfTheLand(mods[0]), written, block, withBlock);
    }

    private const int LandSide = TerrainGrid.VerticesPerSide;
    private const float HeightTolerance = 0.01f;

    /// <summary>The terrain edges are blended over 256 units, two points; the points one farther out are as the rule alone leaves them.</summary>
    private const int PointsOutsideTheBlend = 3;

    /// <summary>
    /// The heights are written row by row: a point of the kept block holds the earlier patcher's height, a point that the blend reaches
    /// lies between that and the base game's, and every other point inside the cell edge holds the base game's height. The cell edge
    /// keeps the cleaned mod's heights where the neighbouring cell is not restored, which other terrain tests cover.
    /// </summary>
    /// <param name="block">The block the earlier patcher raised and the patch keeps; none when nothing is kept.</param>
    private static void AssertEveryPointIsBack(float[] baseGame, float[] written, NavmeshAndLandWorld.PointBlock? block, float[] withBlock)
    {
        Assert.Equal(baseGame.Length, written.Length);
        var wrong = Enumerable.Range(0, baseGame.Length).Where(point => !IsAsExpected(point)).ToList();
        Assert.True(wrong.Count == 0, $"Points (column, row) not as expected: {string.Join("; ", wrong.Take(8).Select(point => $"{point % LandSide},{point / LandSide}"))}");

        bool IsAsExpected(int point)
        {
            var (column, row) = (point % LandSide, point / LandSide);
            var distanceToBlock = block?.DistanceTo(column, row) ?? int.MaxValue;
            if (distanceToBlock == 0) return Same(written[point], withBlock[point]);
            if (Math.Min(Math.Min(column, row), Math.Min(LandSide - 1 - column, LandSide - 1 - row)) < PointsOutsideTheBlend) return true;
            if (distanceToBlock >= PointsOutsideTheBlend) return Same(written[point], baseGame[point]);
            return written[point] >= Math.Min(baseGame[point], withBlock[point]) - HeightTolerance && written[point] <= Math.Max(baseGame[point], withBlock[point]) + HeightTolerance;
        }
    }

    private static bool Same(float one, float other) => MathF.Abs(one - other) <= HeightTolerance;

    private static float[] HeightsOfTheLand(ISkyrimModGetter mod) => LandHeightReader.Read(mod.EnumerateMajorRecords<ILandscapeGetter>().Single())!;

    /// <summary>The city mod needs the cleaned mod, copies its raised land and raises a block again: nothing of that is kept, so the whole land goes back, the block included.</summary>
    [Fact]
    public void ALandACityPluginThatNeedsTheCleanedModRaisedABlockOfAgainGoesBackWhole()
    {
        IReadOnlyList<ISkyrimMod> mods = [];
        float[] written = [];
        var (_, run) = FixtureRun.Capture(
            SettingsOf(),
            Workers,
            dataFolder => mods = NavmeshAndLand(NavmeshAndLandScenario.LandRaisedWithABlockRaisedAgainByACityModThatNeedsTheCleanedMod)(dataFolder),
            earlierPatcherWrites: null,
            patch =>
            {
                if (patch.EnumerateMajorRecords<ILandscapeGetter>().Any()) written = HeightsOfTheLand(patch);
                return AreaPatchDump.Describe(patch);
            });

        ExpectedOutputFiles.AssertMatches("restore-where-removed/TerrainOfAPluginThatNeedsTheCleanedMod", run.ToExpectedOutputLines());
        AssertEveryPointIsBack(HeightsOfTheLand(mods[0]), written, block: null, withBlock: []);
    }

    private static void AssertNoQuarterIsJudgedAndNoLandIsWritten(RunOutput run)
    {
        Assert.DoesNotContain(run.Reports.Where(report => report.Name == CellsFile).SelectMany(ReportRows.Of), row => row["kind"] == "quarter");
        Assert.DoesNotContain(run.Patch, line => line.StartsWith("land ", StringComparison.Ordinal));
    }

    /// <summary>The city mod changed every point of the land again, so a later plugin has the last word on all of it: no quarter is judged and no land is written.</summary>
    [Fact]
    public void ALandALaterPluginChangedAgainEverywhereIsNeitherJudgedNorWritten()
    {
        var (outcome, run) = Run(SettingsOf(), NavmeshAndLand(NavmeshAndLandScenario.LandRaisedFurtherEverywhereByTheCityMod));

        Assert.NotEmpty(outcome.Collected.ChangedLands);
        AssertNoQuarterIsJudgedAndNoLandIsWritten(run);
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
            (SettingsOf(), NavmeshAndLand(NavmeshAndLandScenario.LandRaisedWithABlockRaisedAgainByACityModThatNeedsTheCleanedMod)),
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

    internal static Settings TerrainSettings() => SettingsOf(restore => restore.ClearedArea.SearchRadius = SmallSearchRadius);

    internal static void SwitchEverythingOff(RestoreWhereRemovedSettings restore)
    {
        restore.RestoreOtherModsObjects = false;
        restore.RestoreTerrain = false;
        restore.RestoreCells = false;
        restore.RestoreNavmeshes = false;
    }

    /// <summary>
    /// The cleaned mod's objects that stand too close to a city pad are removed; the left-behind step stays off so only the pads remove
    /// anything. An area counts as cleared with at most <see cref="OneInThreeMayStand"/> of the objects standing and the default share by size.
    /// </summary>
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
                LeftoverObjects = new LeftoverObjectSettings { RemoveBySurroundingDirections = false },
            },
            DetailedLogAndReports = true,
        }.WithTheCollisionOfTheCleanedModsObjects();
        settings.Standard.RestoreWhereRemoved.ClearedArea.MaxStandingCountPercent = OneInThreeMayStand;
        changeRestore?.Invoke(settings.Standard.RestoreWhereRemoved);
        return settings;
    }
}
