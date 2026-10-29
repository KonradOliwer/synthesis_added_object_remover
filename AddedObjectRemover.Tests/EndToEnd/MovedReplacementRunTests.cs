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
    private static readonly Vector3 NearOriginal = Original + new Vector3(75, 0, 0);
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

    [Theory]
    [InlineData(RestoreMode.Restore)]
    [InlineData(RestoreMode.RestoreIfNoCollision)]
    [InlineData(RestoreMode.Remove)]
    public void AMovedReplacementThatFitsWhereTheCleanedModPutItStaysThereInEveryMode(RestoreMode mode)
    {
        var outcome = Run(CrateSettings().WithMovedObjects(mode), movedTo: MovedTo, walls: []);

        Assert.Null(outcome.Final.Of(ReplacementOf(outcome).Id));
    }

    [Fact]
    public void ByDefaultAMovedReplacementThatWouldBeRemovedIsRestoredWhereItStoodWhenThatPlaceIsFree()
    {
        var outcome = Run(movedTo: MovedTo, walls: [NearMovedTo]);

        var restored = Assert.IsType<Decision.Restored>(outcome.Final.Of(ReplacementOf(outcome).Id));
        Assert.Equal(Original, restored.EarlierVersion.Placement.Position);
        Assert.Empty(outcome.Final.RemovedTargets());
    }

    [Fact]
    public void ByDefaultAMovedReplacementThatWouldBeRemovedIsRemovedWhenThePlaceItStoodCollides()
    {
        var outcome = FixtureRun.Execute(CrateSettings(), Workers, CreateCrateWorldWithAnOwnCrateAtTheOldPlace);

        Assert.IsType<RemovalReason.TooClose>(Assert.IsType<Decision.Removed>(outcome.Final.Of(ReplacementOf(outcome).Id)).Reason);
        Assert.Empty(outcome.Restorations);
    }

    [Fact]
    public void ByDefaultAMovedReplacementIsNeverShrunkOrTurnedEvenWhereShrinkingWouldFit()
    {
        var outcome = Run(ShrinkSettings(), movedTo: MovedTo, walls: [NearMovedTo]);

        Assert.IsType<Decision.Restored>(outcome.Final.Of(ReplacementOf(outcome).Id));
    }

    [Fact]
    public void WithResizingOnAMovedReplacementThatFitsShrunkStaysWhereTheCleanedModPutItAndIsNeitherRestoredNorRemoved()
    {
        var settings = ShrinkSettings().WithMovedObjects(RestoreMode.RestoreIfNoCollision, attemptResizing: true);

        var outcome = Run(settings, movedTo: MovedTo, walls: [NearMovedTo]);

        Assert.IsType<Decision.Fitted>(outcome.Final.Of(ReplacementOf(outcome).Id));
        Assert.Single(outcome.ChangedObjects.OldPlaceChecks);
        Assert.Empty(outcome.Restorations);
        Assert.Empty(outcome.Final.RemovedTargets());
    }

    [Fact]
    public void WithResizingOnAMovedReplacementThatFitsNowhereIsLeftToTheMode()
    {
        var settings = ShrinkSettings().WithMovedObjects(RestoreMode.RestoreIfNoCollision, attemptResizing: true);

        var outcome = Run(settings, movedTo: MovedTo, walls: [NearMovedTo, MovedTo]);

        Assert.IsType<Decision.Restored>(outcome.Final.Of(ReplacementOf(outcome).Id));
    }

    /// <summary>The cleaned mod gave the base game's crate the model of a building without moving it, and a city wall stands beside the building's edge.</summary>
    [Fact]
    public void AReplacedObjectIsIgnoredWhenReplacedObjectsAreNotProcessed()
    {
        var outcome = FixtureRun.Execute(CrateSettings(), Workers, CreateReplacedWorld);

        Assert.DoesNotContain(outcome.Collected.Targets, target => target.Origin == TargetOrigin.Replaced);
        Assert.Empty(outcome.Final.RemovedTargets());
        Assert.Empty(outcome.Restorations);
    }

    [Fact]
    public void AProcessedReplacedObjectIsJudgedLikeAMovedOneAndRestoredByDefaultWhenItsOldPlaceIsFree()
    {
        var outcome = FixtureRun.Execute(CrateSettings().WithReplacedObjects(process: true, SettingDefaults.ReplacedObjectsWhenRemoved), Workers, CreateReplacedWorld);

        var replaced = Assert.Single(outcome.Collected.Targets, target => target.Origin == TargetOrigin.Replaced);
        Assert.IsType<Decision.Restored>(outcome.Final.Of(replaced.Id));
    }

    [Fact]
    public void AProcessedReplacedObjectIsRemovedWhenItsModeIsRemove()
    {
        var outcome = FixtureRun.Execute(CrateSettings().WithReplacedObjects(process: true, RestoreMode.Remove), Workers, CreateReplacedWorld);

        var replaced = Assert.Single(outcome.Collected.Targets, target => target.Origin == TargetOrigin.Replaced);
        Assert.IsType<Decision.Removed>(outcome.Final.Of(replaced.Id));
    }

    /// <summary>Only the size margin, so the crate shrunk to 90% keeps clear of the wall that the full crate's margin reaches.</summary>
    [Fact]
    public void AMovedReplacementNextToAWallIsShrunkWhereTheCleanedModPutIt()
    {
        IPlacedObjectGetter? written = null;
        var outcome = Run(RemoveAfterResizing(ShrinkSettings()), movedTo: MovedTo, walls: [NearMovedTo], inspectPatch: patch => written = FindBaseGameObject(patch));

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
        var outcome = Run(RemoveAfterResizing(ShrinkSettings()), movedTo: MovedTo, walls: [NearMovedTo, MovedTo], inspectPatch: patch => written = FindBaseGameObject(patch));

        Assert.Equal(ReplacementOf(outcome).Id, Assert.Single(outcome.Final.RemovedTargets()));
        Assert.True(IsInitiallyDisabled(written!));
    }

    [Fact]
    public void AMovedReplacementNextToAWallIsRemovedWhenShrinkingAndTurningAreSwitchedOff()
    {
        var outcome = Run(RemoveAfterResizing(ShrinkSettings(shrinkLimitPercent: 100, turnLimitDegrees: 0)), movedTo: MovedTo, walls: [NearMovedTo]);

        Assert.Equal(ReplacementOf(outcome).Id, Assert.Single(outcome.Final.RemovedTargets()));
    }

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
    public void TheChangedAndFittedObjectsFileHasAKindColumnAndNamesTheEarlierBaseOfARestoredReplacedObject()
    {
        var settings = CrateSettings().WithReplacedObjects(process: true, SettingDefaults.ReplacedObjectsWhenRemoved);
        settings.DetailedLogAndReports = true;

        var (_, run) = FixtureRun.Capture(settings, Workers, CreateReplacedWorld);

        var fits = Assert.Single(run.Reports, report => report.Name == "changed-and-fitted-objects.csv");
        Assert.Equal("kind,outcome,formKey,editorId,base,worldspace,x,y,z,rotZDeg,scale,newX,newY,newZ,newRotZDeg,newScale,newBase", fits.Lines[0]);
        Assert.StartsWith("replaced,changed object restored,", fits.Lines[1]);
        Assert.EndsWith(",B_CrateBase [000804:Skyrim.esm]", fits.Lines[1]);
        Assert.Contains(run.Log, line => line.StartsWith("Changed and fitted objects: wrote 1 rows to ", StringComparison.Ordinal));
    }

    [Fact]
    public void TheChangedAndFittedObjectsFileLeavesTheNewBaseEmptyForAMovedObjectRestoredWithItsOwnBase()
    {
        var (_, run) = RunRestoring(RestoreMode.Restore, dataFolder => CreateCrateWorld(dataFolder, MovedTo, [NearMovedTo]));

        var row = Assert.Single(run.Reports, report => report.Name == "changed-and-fitted-objects.csv").Lines[1];
        Assert.StartsWith("moved,changed object restored,", row);
        Assert.EndsWith(",1,0,0,0,0,1,", row);
    }

    [Fact]
    public void TheChangedAndFittedObjectsFileHasAnOwnRowForAnObjectOfTheCleanedModKeptShrunk()
    {
        var settings = SupportSettings();
        settings.DetailedLogAndReports = true;

        var (_, run) = FixtureRun.Capture(settings, Workers, dataFolder => CreateStackWorld(dataFolder, [BesideTheTop]));

        var fits = Assert.Single(run.Reports, report => report.Name == "changed-and-fitted-objects.csv");
        var row = Assert.Single(fits.Lines.Skip(1));
        Assert.StartsWith("own,own object shrunk or turned,", row);
        Assert.Equal(17, row.Split(',').Length);
        Assert.EndsWith(",", row);
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

    /// <summary>The cleaned mod moved the crate into the next exterior cell; shrunk there, the patch keeps it in the cell the cleaned mod put it in.</summary>
    [Fact]
    public void AReplacementMovedIntoAnotherCellIsShrunkInThatCell()
    {
        string? cellOfTheWrittenCrate = null;
        var outcome = FixtureRun.Execute(RemoveAfterResizing(ShrinkSettings()), Workers, CreateTwoCellWorld);
        RunTwoCellWorld(patch => cellOfTheWrittenCrate = CellHoldingBaseGameObject(patch));

        Assert.IsType<Decision.Fitted>(outcome.Final.Of(ReplacementOf(outcome).Id));
        Assert.Equal(EastCellEditorId, cellOfTheWrittenCrate);
    }

    [Fact]
    public void AMovedReplacementIsJudgedInASpaceWhereTheCleanedModHasNoObjectOfItsOwn()
    {
        var outcome = FixtureRun.Execute(CrateSettings(), Workers, CreateTwoCellWorld);

        var replacement = ReplacementOf(outcome);
        Assert.DoesNotContain(outcome.Collected.Targets, target => target.Origin == TargetOrigin.CleanedMod && target.SpaceKey == replacement.SpaceKey);
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

    /// <summary>
    /// With EverythingTouchingIt too, the bottom crate only touches the top crate from below and does not rest on it, so the top
    /// crate is shrunk to fit and the bottom crate stays.
    /// </summary>
    [Fact]
    public void WithEverythingTouchingItAnObjectOnlyTouchedFromBelowIsShrunk()
    {
        var outcome = RunStackWorld(ShrinkSettings(), [BesideTheTop]);

        var (top, bottom) = StackOf(outcome);
        Assert.IsType<Decision.Fitted>(outcome.Final.Of(top.Id));
        Assert.Null(outcome.Final.Of(bottom.Id));
    }

    /// <summary>With EverythingTouchingIt, as with OnlyWhatLosesItsSupport, the top crate resting on the bottom crate keeps it from being shrunk.</summary>
    [Fact]
    public void WithEverythingTouchingItAnObjectOthersRestOnIsRemovedInsteadOfShrunkAndWhatTouchesItGoesWithIt()
    {
        var outcome = RunStackWorld(ShrinkSettings(), [BesideTheBottom]);

        var (top, bottom) = StackOf(outcome);
        Assert.IsType<RemovalReason.TooClose>(Assert.IsType<Decision.Removed>(outcome.Final.Of(bottom.Id)).Reason);
        Assert.Equal(new RemovalReason.Touching(bottom.Id), Assert.IsType<Decision.Removed>(outcome.Final.Of(top.Id)).Reason);
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
        var outcome = FixtureRun.Execute(CrateSettings().WithMovedObjects(RestoreMode.Remove), Workers, CreateSoundWorld);

        var removal = Assert.IsType<Decision.Removed>(outcome.Final.Of(ReplacementOf(outcome).Id));
        Assert.IsType<RemovalReason.InsideAnotherModsObject>(removal.Reason);
    }

    [Fact]
    public void ARestoredMovedReplacementIsPutBackWhereItStoodAndNeverCalledRemovedInTheOutput()
    {
        var (outcome, run) = RunRestoring(RestoreMode.Restore, dataFolder => CreateCrateWorld(dataFolder, MovedTo, [NearMovedTo]));

        var restored = Assert.IsType<Decision.Restored>(outcome.Final.Of(ReplacementOf(outcome).Id));
        Assert.Equal(Original, restored.EarlierVersion.Placement.Position);
        Assert.Empty(outcome.Final.RemovedTargets());
        Assert.Single(outcome.Restorations);
        var written = Assert.Single(run.Patch, line => line.Contains(BaseGamePlugin, StringComparison.Ordinal));
        Assert.Contains("disabled False", written);
        Assert.DoesNotContain(run.Log, line => line.StartsWith("  Removed ", StringComparison.Ordinal));
        Assert.Contains(run.Log, line => line.StartsWith("Removed 0 objects (", StringComparison.Ordinal));
        Assert.Contains(run.Log, line => line.StartsWith("Restored 1 objects of other mods that Target.esp changed (1 too close, ", StringComparison.Ordinal));
        ExpectedOutputFiles.AssertMatches("restored-objects/Restore", run.ToExpectedOutputLines());
    }

    [Fact]
    public void AMovedReplacementWhoseOldPlaceIsFreeIsRestoredAndTheCheckIsCounted()
    {
        var (outcome, run) = RunRestoring(RestoreMode.RestoreIfNoCollision, dataFolder => CreateCrateWorld(dataFolder, MovedTo, [NearMovedTo]));

        Assert.IsType<Decision.Restored>(outcome.Final.Of(ReplacementOf(outcome).Id));
        Assert.Contains(run.Log, line => line.StartsWith("Checked the old places of 1 objects of other mods that Target.esp changed: 1 free, 0 colliding", StringComparison.Ordinal));
        Assert.Contains("free", Assert.Single(run.Reports, report => report.Name == ReportFileNames.RestoredFileName).Lines[1]);
        ExpectedOutputFiles.AssertMatches("restored-objects/RestoreIfNoCollisionFree", run.ToExpectedOutputLines());
    }

    /// <summary>The cleaned mod's own crate stands beside the old place, so the crate would collide there and is disabled where the cleaned mod put it.</summary>
    [Fact]
    public void AMovedReplacementWhoseOldPlaceCollidesWithTheCleanedModsObjectIsRemovedAndTheCollisionIsNamed()
    {
        var (outcome, run) = RunRestoring(RestoreMode.RestoreIfNoCollision, CreateCrateWorldWithAnOwnCrateAtTheOldPlace);

        Assert.IsType<Decision.Removed>(outcome.Final.Of(ReplacementOf(outcome).Id));
        Assert.Empty(outcome.Restorations);
        Assert.Contains(run.Log, line => line.StartsWith("Checked the old places of 1 objects of other mods that Target.esp changed: 0 free, 1 colliding", StringComparison.Ordinal));
        Assert.Contains(run.Log, line => line.StartsWith("  Collides at its old place: B_Crate", StringComparison.Ordinal) && line.Contains("changed by Target.esp (new object)", StringComparison.Ordinal));
        Assert.DoesNotContain(run.Reports, report => report.Name == ReportFileNames.RestoredFileName);
        ExpectedOutputFiles.AssertMatches("restored-objects/RestoreIfNoCollisionColliding", run.ToExpectedOutputLines());
    }

    /// <summary>A crate of the cleaned mod stands on the moved crate; the crate goes back to where it stood, so the one on it is left without support and removed.</summary>
    [Fact]
    public void AnObjectRestingOnARestoredMovedReplacementLosesItsSupportAndTheReasonCallsTheCrateRestored()
    {
        var (outcome, run) = RunRestoring(RestoreMode.Restore, CreateCrateWorldWithAnOwnCrateOnTheMovedOne);

        var top = Assert.Single(outcome.Collected.Targets, target => target.EditorId == "T_OnTheMovedCrate");
        Assert.IsType<RemovalReason.LostSupport>(Assert.IsType<Decision.Removed>(outcome.Final.Of(top.Id)).Reason);
        Assert.Contains(run.Log, line => line.Contains("of its support left its place (mostly B_Crate [000805:Skyrim.esm], restored)", StringComparison.Ordinal));
        Assert.DoesNotContain(run.Log, line => line.Contains("support was removed (mostly B_Crate", StringComparison.Ordinal));
        ExpectedOutputFiles.AssertMatches("restored-objects/RestoredSupportLost", run.ToExpectedOutputLines());
    }

    [Fact]
    public void WithRemoveModeNothingIsCheckedAtTheOldPlaceAndNothingIsRestored()
    {
        var (outcome, run) = RunRestoring(RestoreMode.Remove, dataFolder => CreateCrateWorld(dataFolder, MovedTo, [NearMovedTo]));

        Assert.IsType<Decision.Removed>(outcome.Final.Of(ReplacementOf(outcome).Id));
        Assert.DoesNotContain(run.Log, line => line.StartsWith("Checked the old places", StringComparison.Ordinal));
        Assert.DoesNotContain(run.Log, line => line.Contains("estored", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(RestoreMode.Restore)]
    [InlineData(RestoreMode.RestoreIfNoCollision)]
    public void OneAndManyWorkersGiveTheSameOutputForARestore(RestoreMode mode)
    {
        Func<string, IReadOnlyList<ISkyrimMod>> createLoadOrder = CreateCrateWorldWithAnOwnCrateOnTheMovedOne;

        var one = RunRestoring(mode, createLoadOrder, workers: 1).Run.ToExpectedOutputLines();
        var many = RunRestoring(mode, createLoadOrder, workers: Workers).Run.ToExpectedOutputLines();

        Assert.Equal(one, many);
    }

    private static (RunOutcome Outcome, RunOutput Run) RunRestoring(RestoreMode mode, Func<string, IReadOnlyList<ISkyrimMod>> createLoadOrder, int workers = Workers)
    {
        var settings = CrateSettings().WithMovedObjects(mode);
        settings.DetailedLogAndReports = true;
        return FixtureRun.Capture(settings, workers, createLoadOrder);
    }

    /// <summary>A moved object that would be removed is tried shrunk or turned where the cleaned mod put it first and is then removed.</summary>
    private static Settings RemoveAfterResizing(Settings settings) => settings.WithMovedObjects(RestoreMode.Remove, attemptResizing: true);

    private const string HomeCellEditorId = "AorHomeCell";
    private const string EastCellEditorId = "AorEastCell";

    private static TargetObject ReplacementOf(RunOutcome outcome) => Assert.Single(outcome.Collected.Targets, target => target.Origin == TargetOrigin.Moved);

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
        FixtureRun.Run(RemoveAfterResizing(ShrinkSettings()), Workers, new RecordingLogSink(), OpenMeshFiles, createLoadOrder: CreateTwoCellWorld, inspectPatch: inspectPatch);

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

    /// <summary>The changed objects' modes are the defaults and their collision handling is the cleaned mod's, as the settings have it here.</summary>
    private static Settings CrateSettings() => new Settings
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
    private static Settings ShrinkSettings(
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
    private static IReadOnlyList<ISkyrimMod> CreateCrateWorld(string dataFolder, Vector3 movedTo, IReadOnlyList<Vector3> walls) =>
        FillCrateWorld(CreateWorld(dataFolder), movedTo, walls).LoadOrder;

    private static World FillCrateWorld(World world, Vector3 movedTo, IReadOnlyList<Vector3> walls)
    {
        var crate = world.BaseGame.Interior.Place("B_Crate", world.BaseGame.AddModeledStatic("B_CrateBase", FixtureMeshes.CrateModel), Original);
        MoveCopy(world.Target.Interior.PlaceCopyOf(crate), movedTo);
        PlaceWalls(world.City, world.City.Interior, walls);
        return world;
    }

    /// <summary>The crate world with a crate of the cleaned mod on top of the moved crate.</summary>
    private static IReadOnlyList<ISkyrimMod> CreateCrateWorldWithAnOwnCrateOnTheMovedOne(string dataFolder)
    {
        var world = FillCrateWorld(CreateWorld(dataFolder), MovedTo, [NearMovedTo]);
        world.Target.Interior.Place(
            "T_OnTheMovedCrate", world.Target.AddModeledStatic("T_OnTopBase", FixtureMeshes.CrateModel), MovedTo + new Vector3(0, 0, FixtureMeshes.CrateSize));
        return world.LoadOrder;
    }

    /// <summary>The base game's crate in the interior, which the cleaned mod gives a building's model without moving it, and a city wall beside the building's edge.</summary>
    private static IReadOnlyList<ISkyrimMod> CreateReplacedWorld(string dataFolder)
    {
        var world = CreateWorld(dataFolder);
        var crate = world.BaseGame.Interior.Place("B_Crate", world.BaseGame.AddModeledStatic("B_CrateBase", FixtureMeshes.CrateModel), Original);
        world.Target.Interior.PlaceCopyOf(crate).Base.SetTo(world.Target.AddModeledStatic("T_BuildingBase", FixtureMeshes.BuildingModel));
        PlaceWalls(world.City, world.City.Interior, [Original + new Vector3(250, 0, 0)]);
        return world.LoadOrder;
    }

    private static readonly Vector3 SecondCrate = new(0, 3000, 0);

    /// <summary>The crate world, and a second base-game crate far from it that the cleaned mod gives a building's model without moving it.</summary>
    private static IReadOnlyList<ISkyrimMod> CreateMovedAndReplacedWorld(string dataFolder)
    {
        var world = FillCrateWorld(CreateWorld(dataFolder), MovedTo, [NearMovedTo]);
        var second = world.BaseGame.Interior.Place("B_SecondCrate", world.BaseGame.AddModeledStatic("B_SecondCrateBase", FixtureMeshes.CrateModel), SecondCrate);
        world.Target.Interior.PlaceCopyOf(second).Base.SetTo(world.Target.AddModeledStatic("T_SecondBuildingBase", FixtureMeshes.BuildingModel));
        world.City.Interior.Place("C_SecondWall", world.City.AddModeledStatic("C_SecondWallBase", FixtureMeshes.PadModel), SecondCrate + new Vector3(250, 0, 0));
        return world.LoadOrder;
    }

    /// <summary>The crate world with a wall beside the new place, and a crate of the cleaned mod beside the old place.</summary>
    private static IReadOnlyList<ISkyrimMod> CreateCrateWorldWithAnOwnCrateAtTheOldPlace(string dataFolder)
    {
        var world = FillCrateWorld(CreateWorld(dataFolder), MovedTo, [NearMovedTo]);
        world.Target.Interior.Place("T_AtTheOldPlace", world.Target.AddModeledStatic("T_OldPlaceBase", FixtureMeshes.CrateModel), NearOriginal);
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
            .ToDictionary(mod => mod, mod => (Home: mod.AddExteriorCell(homeKey, HomeCellEditorId, HomeGridX), East: mod.AddExteriorCell(eastKey, EastCellEditorId, EastGridX)));
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
}
