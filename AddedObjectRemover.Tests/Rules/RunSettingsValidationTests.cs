using System.Collections.Immutable;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Rules;

public class RunSettingsValidationTests
{
    private static readonly PluginName Target = new("Target.esp");
    private static readonly PluginName Patch = new("Patch.esp");
    private const string ReportsFolder = "/out";

    private static LoadOrderPlugins Facts(bool targetLoaded = true) =>
        TestLoadOrderPlugins.Of(
            new KnownPluginNames([Target, Patch]),
            Patch,
            [new PluginListing(Target, targetLoaded, []), new PluginListing(Patch, true, [])]);

    /// <summary>The target only has to be in the plugin list; the plugins above the patch are the ones Synthesis loads.</summary>
    private static LoadOrderPlugins FactsWithPluginList(PluginName[] pluginList, params PluginName[] disabled) =>
        TestLoadOrderPlugins.Of(
            new KnownPluginNames([Target, Patch]),
            Patch,
            [new PluginListing(Patch, true, [])],
            pluginList,
            disabled);

    private static readonly PluginName Base = new("Skyrim.esm");
    private static readonly PluginName Middle = new("Middle.esp");
    private static readonly LoadOrderPlugins DisabledTargetFacts = FactsWithPluginList([Base, Target, Patch], Target);
    private static readonly LoadOrderPlugins NotLoadedTargetFacts = FactsWithPluginList([Base, Target, Patch]);
    private static readonly LoadOrderPlugins TargetAfterPatchFacts = FactsWithPluginList([Base, Patch, Middle, Target]);

    private static StandardSettings SettingsFor(string target = "Target.esp") =>
        new() { CleanedMod = ModKey.FromNameAndExtension(target) };

    /// <summary>For the tests that ask what the standard settings alone read: the changed objects check no terrain and no old place.</summary>
    private static StandardSettings WithoutChangedObjectsTerrainAndOldPlaces(StandardSettings settings)
    {
        settings.ChangedObjects.Collision.TerrainChange.RemoveOnChangedTerrain = false;
        settings.ChangedObjects.Moved.WhenRemoved = RestoreMode.Remove;
        return settings;
    }

    private static RunRequest Build(StandardSettings settings, LoadOrderPlugins? facts = null, bool detailedLogAndReports = false, int workers = 3) =>
        SettingsValidator.Build(StandardConfig.Of(settings), new PluginNameParsingPlugin(), facts ?? Facts(), detailedLogAndReports, onlyWarnWhenRequiringModLoadsAfterPatch: false, ReportsFolder, workers);

    private static RunRequest.Ready BuildReady(StandardSettings settings, LoadOrderPlugins? facts = null, bool detailedLogAndReports = false, int workers = 3) =>
        Assert.IsType<RunRequest.Ready>(Build(settings, facts, detailedLogAndReports, workers));

    private static BatchRequest BuildAll(ConfigsRead read, LoadOrderPlugins? facts = null) =>
        BatchRequests.BuildAll(read, new PluginNameParsingPlugin(), facts ?? Facts(), detailedLogAndReports: false, onlyWarnWhenRequiringModLoadsAfterPatch: false, ReportsFolder, 1);

    [Fact]
    public void TheShrinkAndTurnDefaultsReachTheRunUnchanged()
    {
        var ready = BuildReady(SettingsFor());

        Assert.Equal(new FitSettings(80, 360), ready.Settings.Collision.Fit);
        Assert.Empty(ready.Warnings);
    }

    [Theory]
    [InlineData(100, 180)]
    [InlineData(250, 720)]
    [InlineData(1, 0)]
    public void ShrinkAndTurnLimitsAtOrBeyondTheirMeaningfulRangeAreKeptWithoutWarning(int shrink, int turn)
    {
        var settings = SettingsFor();
        settings.ShrinkOrTurn = new ShrinkOrTurnSettings { ShrinkLimitPercent = shrink, TurnLimitDegrees = turn };

        var ready = BuildReady(settings);

        Assert.Equal((shrink, turn), (ready.Settings.Collision.Fit.ShrinkLimitPercent, ready.Settings.Collision.Fit.TurnLimitDegrees));
        Assert.Empty(ready.Warnings);
    }

    [Fact]
    public void WithShrinkingAndTurningOffTheLimitsAreNeverShrinkAndNeverTurnWithoutWarnings()
    {
        var settings = SettingsFor();
        settings.ShrinkOrTurn = new ShrinkOrTurnSettings { ShrinkOrTurnObjects = false, ShrinkLimitPercent = 0, TurnLimitDegrees = -10 };

        var ready = BuildReady(settings);

        Assert.Equal(new FitSettings(100, 0), ready.Settings.Collision.Fit);
        Assert.Empty(ready.Warnings);
    }

    [Fact]
    public void AShrinkLimitBelow1AndANegativeTurnLimitAreRaisedWithAWarningEach()
    {
        var settings = SettingsFor();
        settings.ShrinkOrTurn = new ShrinkOrTurnSettings { ShrinkLimitPercent = 0, TurnLimitDegrees = -10 };

        var ready = BuildReady(settings);

        Assert.Equal((1, 0), (ready.Settings.Collision.Fit.ShrinkLimitPercent, ready.Settings.Collision.Fit.TurnLimitDegrees));
        Assert.Equal(
            [
                "Warning: Shrink or turn objects to make them fit > Shrink down to 0 is below 1; using 1.",
                "Warning: Shrink or turn objects to make them fit > Turn by at most -10 is below 0; using 0.",
            ],
            ready.Warnings.Select(warning => warning.Message));
    }

    [Fact]
    public void TheDistanceThatCountsAsMovedIs10UnitsByDefaultAndReachesTheRead()
    {
        Assert.Equal(10f, BuildReady(SettingsFor()).Settings.Read.MovedDistance);

        var settings = SettingsFor();
        settings.ChangedObjects.Moved.MovedDistanceInUnits = 0f;
        var ready = BuildReady(settings);

        Assert.Equal(0f, ready.Settings.Read.MovedDistance);
        Assert.Empty(ready.Warnings);
    }

    [Theory]
    [InlineData(-3f, "Warning: Other mods' objects the cleaned mod changed > Moved objects > Count as moved when moved farther than -3 is below 0; using 10.")]
    [InlineData(float.NaN, "Warning: Other mods' objects the cleaned mod changed > Moved objects > Count as moved when moved farther than is not a valid number; using 10.")]
    public void ANegativeDistanceThatCountsAsMovedIsReplacedByTheDefaultWithAWarning(float distance, string warning)
    {
        var settings = SettingsFor();
        settings.ChangedObjects.Moved.MovedDistanceInUnits = distance;

        var ready = BuildReady(settings);

        Assert.Equal(10f, ready.Settings.Read.MovedDistance);
        Assert.Equal(warning, Assert.Single(ready.Warnings).Message);
    }

    [Fact]
    public void AnEmptyCleanedModStopsTheRunAsNoTargetSet()
    {
        var stop = Assert.IsType<RunRequest.Stop>(Build(new StandardSettings()));
        Assert.Equal(new StopReason(StopKind.NoTargetSet, null), stop.Reason);
    }

    [Fact]
    public void ATargetNotInThePluginListStopsTheRun()
    {
        var stop = Assert.IsType<RunRequest.Stop>(Build(SettingsFor("Missing.esp")));
        Assert.Equal(new StopReason(StopKind.CleanedModNotInLoadOrder, "Missing.esp"), stop.Reason);
    }

    [Fact]
    public void ADisabledTargetStopsTheRun()
    {
        var stop = Assert.IsType<RunRequest.Stop>(Build(SettingsFor(), DisabledTargetFacts));
        Assert.Equal(new StopReason(StopKind.CleanedModDisabled, "Target.esp"), stop.Reason);
    }

    [Fact]
    public void ATargetEnabledAboveThePatchButNotLoadedBySynthesisStopsTheRun()
    {
        var stop = Assert.IsType<RunRequest.Stop>(Build(SettingsFor(), NotLoadedTargetFacts));
        Assert.Equal(new StopReason(StopKind.CleanedModNotLoadedBySynthesis, "Target.esp"), stop.Reason);
    }

    [Fact]
    public void ATargetLoadingAfterThePatchStopsTheRunWithBothPositions()
    {
        var stop = Assert.IsType<RunRequest.Stop>(Build(SettingsFor(), TargetAfterPatchFacts));
        Assert.Equal(new StopReason(StopKind.CleanedModLoadsAfterPatch, "Target.esp", new PatchPlacement("Patch.esp", "Target.esp", 3, 1)), stop.Reason);
    }

    [Fact]
    public void ThePatchPluginAsTargetStopsTheRun()
    {
        var stop = Assert.IsType<RunRequest.Stop>(Build(SettingsFor("Patch.esp")));
        Assert.Equal(new StopReason(StopKind.TargetIsThePatch, "Patch.esp"), stop.Reason);
    }

    [Fact]
    public void TooCloseWithNeitherMarginOnGivesAWarning()
    {
        var settings = SettingsFor();
        settings.TooClose.BySize.Use = false;
        settings.TooClose.ByDistance.Use = false;

        var ready = BuildReady(settings);

        Assert.False(ready.Settings.Collision.TooClose.BySize || ready.Settings.Collision.TooClose.ByDistance);
        Assert.Equal(
            "Warning: neither too-close margin is on; no object is removed for being too close.",
            Assert.Single(ready.Warnings).Message);
    }

    [Fact]
    public void ValidDefaultSettingsGiveNoWarnings()
    {
        var ready = BuildReady(SettingsFor());
        Assert.Empty(ready.Warnings);
        Assert.Equal(Target, ready.Settings.Target);
        Assert.Equal(3, ready.Settings.Execution.Workers);
    }

    [Fact]
    public void AnUnreadableTargetGivesAWarning()
    {
        var ready = BuildReady(SettingsFor(), Facts(targetLoaded: false));
        Assert.Equal("Warning: could not read Target.esp to find its masters.", Assert.Single(ready.Warnings).Message);
    }

    [Fact]
    public void EachEnabledPluginWithoutItsFileGivesAWarningLine()
    {
        var facts = Facts() with { MissingPlugins = [new PluginName("Gone.esp"), new PluginName("Lost.esm")] };

        var ready = BuildReady(SettingsFor(), facts);

        Assert.Equal(
            [
                "Warning: Gone.esp is enabled in your plugin list but its file was not found; it was skipped.",
                "Warning: Lost.esm is enabled in your plugin list but its file was not found; it was skipped.",
            ],
            ready.Warnings.Select(warning => warning.Message));
    }

    [Fact]
    public void WarningsComeInTheOrderSettingsAreCheckedWithEachInvalidValueReportedOnce()
    {
        var settings = SettingsFor();
        settings.ChangedObjects.Moved.MovedDistanceInUnits = -5f;
        settings.Trust = new TrustSettings { MaxOtherMastersOfRequiringModToTrust = 500 };
        settings.TooClose.NpcHandling = (NpcHandling)99;
        settings.TooClose.BySize.SizeMultiplier = 9f;
        settings.FollowUpRemoval = new FollowUpRemovalSettings
        {
            Mode = (FollowUpRemovalMode)99,
            TouchDistance = 100f,
            AnchoringThresholdPercent = 500f,
        };
        settings.LeftoverInvisibleObjects = new LeftoverInvisibleObjectSettings
        {
            SurroundingObjects = new SurroundingObjectsSettings
            {
                SearchRadius = 1f,
                DirectionThresholdPercent = 45,
                ClearedDirectionsPercent = 44,
                MinOccupiedDirections = 0,
            },
        };

        var messages = BuildReady(settings, Facts(targetLoaded: false)).Warnings.Select(warning => warning.Message).ToList();

        string[] expectedStarts =
        [
            "Warning: could not read Target.esp",
            "Warning: Other mods' objects the cleaned mod changed > Moved objects > Count as moved when moved farther than",
            "Warning: Which other mods can cause removals > ...only if it has at most this many other masters 500 is outside 0-100",
            "Warning: Remove objects too close to other mods' objects > Other mods' NPCs and creatures cause removal",
            "Warning: Remove objects too close to other mods' objects > Margin by object size > Margin on every side",
            "Warning: Also remove what rests on removed objects > When an object is removed, also remove",
            "Warning: Also remove what rests on removed objects > Gap that still counts as touching",
            "Warning: Also remove what rests on removed objects > OnlyWhatLosesItsSupport: remove when this much support is gone",
            "Warning: Invisible objects left behind > When the cleaned mod's visible objects around it were removed > Look this far around it",
            "Warning: Invisible objects left behind > When the cleaned mod's visible objects around it were removed > Decide only if at least this many of the 8 directions have objects 0 is outside 1-8; using 1",
            "Warning: Invisible objects left behind > When the cleaned mod's visible objects around it were removed > A direction is cleared when this much of it was removed 45 is not a multiple",
            "Warning: Invisible objects left behind > When the cleaned mod's visible objects around it were removed > Remove when this share of the directions with objects is cleared 44 is not a multiple",
        ];
        Assert.Equal(expectedStarts.Length, messages.Count);
        for (var i = 0; i < expectedStarts.Length; i++)
        {
            Assert.StartsWith(expectedStarts[i], messages[i]);
        }
    }

    [Fact]
    public void TheCollisionWarningsComeInTheOrderTooCloseThenChangedTerrainThenFit()
    {
        var settings = SettingsFor();
        settings.ShrinkOrTurn = new ShrinkOrTurnSettings { ShrinkLimitPercent = 0 };
        settings.TerrainChange = new TerrainChangeSettings { TerrainCheckedObjects = (TerrainCheckedObjects)99 };
        settings.TooClose.NpcHandling = (NpcHandling)99;

        var messages = BuildReady(settings).Warnings.Select(warning => warning.Message).ToList();

        string[] expectedStarts =
        [
            "Warning: Remove objects too close to other mods' objects > Other mods' NPCs and creatures cause removal",
            "Warning: Remove objects on terrain changed by other mods > Which of the cleaned mod's objects are checked",
            "Warning: Shrink or turn objects to make them fit > Shrink down to",
        ];
        Assert.Equal(expectedStarts.Length, messages.Count);
        Assert.All(expectedStarts.Zip(messages), pair => Assert.StartsWith(pair.First, pair.Second));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(9, 8)]
    [InlineData(1, 1)]
    [InlineData(8, 8)]
    public void TheMinimumOccupiedDirectionsAreLimitedToTheEightDirections(int configured, int expected)
    {
        var settings = SettingsFor();
        settings.LeftoverInvisibleObjects = new LeftoverInvisibleObjectSettings
        {
            SurroundingObjects = new SurroundingObjectsSettings { MinOccupiedDirections = configured },
        };

        var ready = BuildReady(settings);

        Assert.Equal(expected, ready.Settings.LeftBehind!.Surroundings.MinOccupiedDirections);
        Assert.Equal(configured != expected, ready.Warnings.Any(warning => warning.Message.Contains("directions have objects")));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(9, 8)]
    [InlineData(1, 1)]
    [InlineData(8, 8)]
    public void TheVisibleMinimumOccupiedDirectionsAreLimitedToTheEightDirections(int configured, int expected)
    {
        var settings = SettingsFor();
        settings.LeftoverVisibleObjects = new LeftoverVisibleObjectSettings
        {
            RemoveLeftoverVisibleObjects = true,
            SurroundingObjects = new VisibleSurroundingObjectsSettings { MinOccupiedDirections = configured },
        };

        var ready = BuildReady(settings);

        Assert.Equal(expected, ready.Settings.VisibleLeftBehind!.Surroundings.MinOccupiedDirections);
        Assert.Equal(
            configured != expected,
            ready.Warnings.Any(warning => warning.Message.StartsWith("Warning: Visible objects left behind > ", StringComparison.Ordinal) && warning.Message.Contains("directions have objects")));
    }

    [Fact]
    public void TheVisibleStepIsOnByDefaultWithItsOwnDefaultsAndNullWhenSwitchedOff()
    {
        var settings = SettingsFor();
        settings.LeftoverVisibleObjects = new LeftoverVisibleObjectSettings { RemoveLeftoverVisibleObjects = false };

        var off = BuildReady(settings);
        var on = BuildReady(SettingsFor());

        Assert.Null(off.Settings.VisibleLeftBehind);
        var visible = on.Settings.VisibleLeftBehind!;
        Assert.Equal(new LeftBehindSurroundings(1024f, 6, 80, 80), visible.Surroundings);
        Assert.Empty(visible.NeverRemove);
        Assert.Empty(on.Warnings);
    }

    [Fact]
    public void InvalidVisibleSettingsAreReportedAndCorrectedEvenWhenTheStepIsOff()
    {
        var settings = SettingsFor();
        settings.LeftoverVisibleObjects = new LeftoverVisibleObjectSettings
        {
            RemoveLeftoverVisibleObjects = false,
            SurroundingObjects = new VisibleSurroundingObjectsSettings { SearchRadius = 1f, DirectionThresholdPercent = 45, ClearedDirectionsPercent = 44 },
        };

        var messages = BuildReady(settings).Warnings.Select(warning => warning.Message).ToList();

        const string group = "Warning: Visible objects left behind > When the cleaned mod's visible objects around it were removed > ";
        Assert.Equal(3, messages.Count);
        Assert.StartsWith(group + "Look this far around it", messages[0]);
        Assert.StartsWith(group + "A direction is cleared when this much of it was removed 45 is not a multiple", messages[1]);
        Assert.StartsWith(group + "Remove when this share of the directions with objects is cleared 44 is not a multiple", messages[2]);
    }

    [Fact]
    public void TheTickedVisibleKindsAreTheOnesNeverRemoved()
    {
        var settings = SettingsFor();
        settings.LeftoverVisibleObjects = new LeftoverVisibleObjectSettings
        {
            RemoveLeftoverVisibleObjects = true,
            AlwaysKeep = new KeptVisibleObjectSettings { Containers = true, Furniture = true, Activators = true },
        };

        var ready = BuildReady(settings);

        Assert.Equal(
            [VisibleObjectKind.Containers, VisibleObjectKind.Furniture, VisibleObjectKind.Activators],
            ready.Settings.VisibleLeftBehind!.NeverRemove.Order());
    }

    private const string RestoreGroup = "Restore what the cleaned mod changed where it was removed > ";
    private const string ClearedAreaGroup = RestoreGroup + "When an area counts as cleared > ";

    [Fact]
    public void TheRestoreGroupIsOnByDefaultWithItsDefaultsAndTheReusedCollisionSettings()
    {
        var ready = BuildReady(SettingsFor());

        var restore = ready.Settings.RestoreWhereRemoved!;
        Assert.Equal((true, true, true, true), (restore.OtherModsObjects, restore.Terrain, restore.Cells, restore.Navmeshes));
        Assert.Equal(new ClearedArea(5000f, 0.1, ClearedAreaMeasure.ObjectSize), restore.ClearedArea);
        Assert.Equal(256f, restore.TerrainEdgeBlendDistance);
        Assert.Equal(
            (ready.Settings.Collision.ChangedTerrain.HeightTolerance, ready.Settings.Collision.ChangedTerrain.WaterHeightTolerance),
            (restore.HeightChangeTolerance, restore.WaterHeightChangeTolerance));
        Assert.Equal((ready.Settings.ChangedObjectCollision.TooClose, ready.Settings.ChangedObjectCollision.ChangedTerrain), (restore.TooClose, restore.ChangedTerrain));
        Assert.Equal(
            new AreaRecordReads(new AreaPartTolerances(restore.HeightChangeTolerance, restore.WaterHeightChangeTolerance), Cells: true, Lands: true),
            ready.Settings.Read.Needs.AreaRecordVersions);
        Assert.Empty(ready.Warnings);
    }

    [Fact]
    public void TheRestoreGroupTakesTheHeightAndWaterTolerancesOfTheCleanedModsTerrainCheck()
    {
        var settings = SettingsFor();
        settings.TerrainChange.HeightChangeTolerance = 30f;
        settings.TerrainChange.WaterHeightChangeTolerance = 40f;
        settings.ChangedObjects.Collision.TerrainChange.HeightChangeTolerance = 50f;

        var restore = BuildReady(settings).Settings.RestoreWhereRemoved!;

        Assert.Equal((30f, 40f), (restore.HeightChangeTolerance, restore.WaterHeightChangeTolerance));
        Assert.Equal(50f, restore.ChangedTerrain.HeightTolerance);
    }

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public void TheRestoreGroupIsOnWhenAnyOneSwitchIsOn(bool objects, bool terrain, bool cells, bool navmeshes)
    {
        var settings = SettingsFor();
        settings.RestoreWhereRemoved = new RestoreWhereRemovedSettings
        {
            RestoreOtherModsObjects = objects, RestoreTerrain = terrain, RestoreCells = cells, RestoreNavmeshes = navmeshes,
        };

        var ready = BuildReady(settings);

        var restore = ready.Settings.RestoreWhereRemoved!;
        Assert.Equal((objects, terrain, cells, navmeshes), (restore.OtherModsObjects, restore.Terrain, restore.Cells, restore.Navmeshes));
        Assert.NotNull(ready.Settings.Read.Needs.AreaRecordVersions);
    }

    /// <summary>
    /// A kind whose switch is off is read when another switched-on kind's guards look at it: restored objects and navmeshes look at
    /// the cells and the ground that stay changed, the terrain looks at no cell and the cells at no ground.
    /// </summary>
    [Theory]
    [InlineData(true, false, false, false, true, true)]
    [InlineData(false, true, false, false, false, true)]
    [InlineData(false, false, true, false, true, false)]
    [InlineData(false, false, false, true, true, true)]
    [InlineData(false, true, true, false, true, true)]
    public void TheCellsAndTheGroundAreReadWhenTheirOwnSwitchOrTheGuardsOfAnotherSwitchedOnKindNeedThem(
        bool objects, bool terrain, bool cells, bool navmeshes, bool cellsRead, bool landsRead)
    {
        var settings = SettingsFor();
        settings.RestoreWhereRemoved = new RestoreWhereRemovedSettings
        {
            RestoreOtherModsObjects = objects, RestoreTerrain = terrain, RestoreCells = cells, RestoreNavmeshes = navmeshes,
        };

        var reads = BuildReady(settings).Settings.Read.Needs.AreaRecordVersions!;

        Assert.Equal((cellsRead, landsRead), (reads.Cells, reads.Lands));
    }

    [Fact]
    public void TheRestoreGroupDoesNotRunAndReadsNothingWhenAllFourSwitchesAreOff()
    {
        var settings = SettingsFor();
        settings.RestoreWhereRemoved = AllRestoresOff();

        var ready = BuildReady(settings);

        Assert.False(ready.Settings.RestoreWhereRemoved.Runs);
        Assert.Null(ready.Settings.Read.Needs.AreaRecordVersions);
    }

    [Theory]
    [InlineData(1023f, 1024f)]
    [InlineData(16385f, 16384f)]
    [InlineData(0f, 1024f)]
    [InlineData(1024f, 1024f)]
    [InlineData(16384f, 16384f)]
    public void TheClearedAreaRadiusIsLimitedTo1024To16384(float configured, float expected)
    {
        var settings = SettingsFor();
        settings.RestoreWhereRemoved.ClearedArea.SearchRadius = configured;

        var ready = BuildReady(settings);

        Assert.Equal(expected, ready.Settings.RestoreWhereRemoved!.ClearedArea.SearchRadius);
        AssertOnlyWarning(ready, configured != expected, $"Warning: {ClearedAreaGroup}Look this far around the place {configured} is outside 1024-16384; using {expected}.");
    }

    [Theory]
    [InlineData(-0.5f, 0.0)]
    [InlineData(100.5f, 100.0)]
    [InlineData(0f, 0.0)]
    [InlineData(100f, 100.0)]
    public void TheMaxStandingPercentIsLimitedTo0To100(float configured, double expected)
    {
        var settings = SettingsFor();
        settings.RestoreWhereRemoved.ClearedArea.MaxStandingPercent = configured;

        var ready = BuildReady(settings);

        Assert.Equal(expected, ready.Settings.RestoreWhereRemoved!.ClearedArea.MaxStandingPercent);
        AssertOnlyWarning(
            ready,
            configured != expected,
            $"Warning: {ClearedAreaGroup}Cleared when at most this share still stands {configured} is outside 0-100; using {expected}.");
    }

    [Theory]
    [InlineData(0.1f, 0.1)]
    [InlineData(0.7f, 0.7)]
    [InlineData(12.5f, 12.5)]
    public void ADecimalMaxStandingPercentIsKeptAsTyped(float configured, double expected)
    {
        var settings = SettingsFor();
        settings.RestoreWhereRemoved.ClearedArea.MaxStandingPercent = configured;

        var ready = BuildReady(settings);

        Assert.Equal(expected, ready.Settings.RestoreWhereRemoved!.ClearedArea.MaxStandingPercent);
        Assert.Empty(ready.Warnings);
    }

    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(1025f, 1024f)]
    [InlineData(0f, 0f)]
    [InlineData(1024f, 1024f)]
    public void TheTerrainEdgeBlendDistanceIsLimitedTo0To1024(float configured, float expected)
    {
        var settings = SettingsFor();
        settings.RestoreWhereRemoved.TerrainEdgeBlendDistanceInUnits = configured;

        var ready = BuildReady(settings);

        Assert.Equal(expected, ready.Settings.RestoreWhereRemoved!.TerrainEdgeBlendDistance);
        AssertOnlyWarning(ready, configured != expected, $"Warning: {RestoreGroup}Blend terrain edges over {configured} is outside 0-1024; using {expected}.");
    }

    [Fact]
    public void ARestoreNumberThatIsNotANumberBecomesTheDefaultWithAWarning()
    {
        var settings = SettingsFor();
        settings.RestoreWhereRemoved.ClearedArea.SearchRadius = float.NaN;
        settings.RestoreWhereRemoved.ClearedArea.MaxStandingPercent = float.NaN;
        settings.RestoreWhereRemoved.TerrainEdgeBlendDistanceInUnits = float.NaN;

        var ready = BuildReady(settings);

        var restore = ready.Settings.RestoreWhereRemoved!;
        Assert.Equal((new ClearedArea(5000f, 0.1, ClearedAreaMeasure.ObjectSize), 256f), (restore.ClearedArea, restore.TerrainEdgeBlendDistance));
        Assert.Equal(
            [
                $"Warning: {ClearedAreaGroup}Look this far around the place is not a valid number; using 5000.",
                $"Warning: {ClearedAreaGroup}Cleared when at most this share still stands is not a valid number; using 0.1.",
                $"Warning: {RestoreGroup}Blend terrain edges over is not a valid number; using 256.",
            ],
            ready.Warnings.Select(warning => warning.Message));
    }

    [Fact]
    public void AnUndefinedMeasureBecomesObjectSizeWithAWarning()
    {
        var settings = SettingsFor();
        settings.RestoreWhereRemoved.ClearedArea.Measure = (ClearedAreaMeasure)99;

        var ready = BuildReady(settings);

        Assert.Equal(ClearedAreaMeasure.ObjectSize, ready.Settings.RestoreWhereRemoved!.ClearedArea.Measure);
        Assert.Equal($"Warning: {ClearedAreaGroup}Measure the share by 99 is invalid; using ObjectSize.", Assert.Single(ready.Warnings).Message);
    }

    [Fact]
    public void ObjectCountIsKept()
    {
        var settings = SettingsFor();
        settings.RestoreWhereRemoved.ClearedArea.Measure = ClearedAreaMeasure.ObjectCount;

        Assert.Equal(ClearedAreaMeasure.ObjectCount, BuildReady(settings).Settings.RestoreWhereRemoved!.ClearedArea.Measure);
    }

    [Fact]
    public void InvalidRestoreSettingsAreReportedInOrderEvenWhenAllSwitchesAreOff()
    {
        var settings = SettingsFor();
        settings.RestoreWhereRemoved = AllRestoresOff();
        settings.RestoreWhereRemoved.ClearedArea = new ClearedAreaSettings { SearchRadius = 1f, MaxStandingPercent = 101f, Measure = (ClearedAreaMeasure)7 };
        settings.RestoreWhereRemoved.TerrainEdgeBlendDistanceInUnits = 2000f;

        var ready = BuildReady(settings);

        Assert.False(ready.Settings.RestoreWhereRemoved.Runs);
        Assert.Equal(
            [
                $"Warning: {ClearedAreaGroup}Look this far around the place 1 is outside 1024-16384; using 1024.",
                $"Warning: {ClearedAreaGroup}Cleared when at most this share still stands 101 is outside 0-100; using 100.",
                $"Warning: {ClearedAreaGroup}Measure the share by 7 is invalid; using ObjectSize.",
                $"Warning: {RestoreGroup}Blend terrain edges over 2000 is outside 0-1024; using 1024.",
            ],
            ready.Warnings.Select(warning => warning.Message));
    }

    [Fact]
    public void AMissingRestoreGroupOrClearedAreaGroupHasTheDefaults()
    {
        var withoutGroup = SettingsFor();
        withoutGroup.RestoreWhereRemoved = null!;
        var withoutArea = SettingsFor();
        withoutArea.RestoreWhereRemoved.ClearedArea = null!;

        var expected = BuildReady(SettingsFor()).Settings.RestoreWhereRemoved;

        Assert.Equal(expected, BuildReady(withoutGroup).Settings.RestoreWhereRemoved);
        Assert.Equal(expected, BuildReady(withoutArea).Settings.RestoreWhereRemoved);
    }

    private static void AssertOnlyWarning(RunRequest.Ready ready, bool warns, string message) =>
        Assert.Equal(warns ? new[] { message } : [], ready.Warnings.Select(warning => warning.Message));

    [Fact]
    public void TheTrustListsHoldTheNamesAsTheSettingsSpellThemWithoutUnsetEntries()
    {
        var settings = SettingsFor();
        settings.Trust = new TrustSettings { TrustedModsAndTheirMasters = [ModKey.FromNameAndExtension("PATCH.ESP"), ModKey.Null] };

        var ready = BuildReady(settings);

        Assert.Equal("PATCH.esp", Assert.Single(ready.Settings.ModTrust.TrustedModsAndTheirMasters).FileName);
        Assert.Empty(ready.Warnings);
    }

    [Fact]
    public void EachListedPluginThatIsNotInThePluginListGivesAWarningNamingItsList()
    {
        var settings = SettingsFor();
        settings.Trust = new TrustSettings
        {
            TrustedModsAndTheirMasters = [ModKey.FromNameAndExtension("City.esp")],
            ModsTrustedForOwnChangesOnly = [ModKey.FromNameAndExtension("Birds.esp")],
            NeverTrustedMods = [ModKey.FromNameAndExtension("Merged.esp")],
        };

        var ready = BuildReady(settings);

        Assert.Equal(
            [
                "Warning: Which other mods can cause removals > Trusted mods, and their masters: City.esp is not in your plugin list.",
                "Warning: Which other mods can cause removals > Mods trusted for their own changes only: Birds.esp is not in your plugin list.",
                "Warning: Which other mods can cause removals > Never trusted mods: Merged.esp is not in your plugin list.",
            ],
            ready.Warnings.Select(warning => warning.Message));
        Assert.Equal("City.esp", Assert.Single(ready.Settings.ModTrust.TrustedModsAndTheirMasters).FileName);
    }

    [Fact]
    public void TheCleanedModOrThePatchListedAsNeverTrustedGivesAWarningEach()
    {
        var settings = SettingsFor();
        settings.Trust = new TrustSettings { NeverTrustedMods = [ModKey.FromNameAndExtension("Target.esp"), ModKey.FromNameAndExtension("Patch.esp")] };

        var ready = BuildReady(settings);

        Assert.Equal(
            [
                "Warning: Which other mods can cause removals > Never trusted mods: Target.esp is the mod to clean up, which cannot be listed here; the entry has no effect.",
                "Warning: Which other mods can cause removals > Never trusted mods: Patch.esp is the patch this patcher writes, which cannot be listed here; the entry has no effect.",
            ],
            ready.Warnings.Select(warning => warning.Message));
    }

    [Fact]
    public void TheTargetIsComparedAsTheLoadOrderSpellsItAndPrintedAsTheSettingsSpellIt()
    {
        var ready = BuildReady(SettingsFor("TARGET.ESP"));
        Assert.Equal("Target.esp", ready.Settings.Target.FileName);
        Assert.Equal("Target.esp", ready.Settings.ModTrust.Target.FileName);
        Assert.Equal("TARGET.esp", ready.Settings.TargetAsTyped.FileName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditorIdAndOverriddenListsFollowTheDetailedLog(bool detailed)
    {
        var plan = BuildReady(SettingsFor(), detailedLogAndReports: detailed).Settings.Read;
        Assert.Equal(detailed, plan.Needs.OtherModDetails);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheDetailedLogAndReportsSettingTurnsOnTheDetailedLogTheReportFilesAndTheTouchChains(bool on)
    {
        var settings = BuildReady(SettingsFor(), detailedLogAndReports: on).Settings;

        Assert.Equal(on, settings.DetailedLog);
        Assert.Equal(on, settings.Reports is not null);
        Assert.Equal(on, settings.AlsoRemove.TouchChainsShown);
    }

    [Fact]
    public void NoReportsFolderIsNeededWhenTheDetailedLogAndReportsAreOff() =>
        Assert.Null(Assert.IsType<RunRequest.Ready>(SettingsValidator.Build(
            StandardConfig.Of(SettingsFor()), new PluginNameParsingPlugin(), Facts(), detailedLogAndReports: false, onlyWarnWhenRequiringModLoadsAfterPatch: false, reportsFolder: null, 1)).Settings.Reports);

    [Fact]
    public void AReportsFolderIsRequiredWhenTheDetailedLogAndReportsAreOn() =>
        Assert.Throws<ArgumentNullException>(() => SettingsValidator.Build(
            StandardConfig.Of(SettingsFor()), new PluginNameParsingPlugin(), Facts(), detailedLogAndReports: true, onlyWarnWhenRequiringModLoadsAfterPatch: false, reportsFolder: null, 1));

    [Theory]
    [InlineData(nameof(NpcHandling.Never))]
    [InlineData(nameof(NpcHandling.OnlyWhenStuckInsideTheObject))]
    [InlineData(nameof(NpcHandling.OnlyWhenStuckInsideTheObjectWithCollision))]
    [InlineData(nameof(NpcHandling.AlwaysLikeAnyObject))]
    public void TheSelectStepHandlesNpcsAsTheTooCloseStepDoes(string handling)
    {
        var settings = SettingsFor();
        settings.TooClose.NpcHandling = Enum.Parse<NpcHandling>(handling);

        var ready = BuildReady(settings);

        Assert.Equal(Enum.Parse<NpcHandling>(handling), ready.Settings.Selection.Npcs);
        Assert.Equal(ready.Settings.Collision.TooClose.Npcs, ready.Settings.Selection.Npcs);
    }

    [Fact]
    public void AnInvalidNpcSettingGivesBothStepsTheDefault()
    {
        var settings = SettingsFor();
        settings.TooClose.NpcHandling = (NpcHandling)99;

        var ready = BuildReady(settings);

        Assert.Equal(SettingDefaults.Npcs, ready.Settings.Selection.Npcs);
        Assert.Equal(SettingDefaults.Npcs, ready.Settings.Collision.TooClose.Npcs);
    }

    [Fact]
    public void SurroundingsAreAlwaysReadAndNavmeshesOnlyForMarkerMovesOrTheRestoreGroup()
    {
        var relocating = WithoutChangedObjectsTerrainAndOldPlaces(SettingsFor());
        relocating.TerrainChange.RemoveOnChangedTerrain = false;
        relocating.FollowUpRemoval = new FollowUpRemovalSettings { Mode = FollowUpRemovalMode.EverythingTouchingIt };
        relocating.LeftoverInvisibleObjects = new LeftoverInvisibleObjectSettings
        {
            RemoveLeftoverInvisibleObjects = true,
            MoveKeptMarkersOutOfOtherModsObjects = true,
        };
        TurnRestoreWhereRemovedOff(relocating);
        var runSettings = BuildReady(relocating).Settings;
        Assert.Equal(new WhatToCollect(new ScanNeeds(Navmesh: true), 10f, ProcessReplaced: false), runSettings.Read);
        Assert.True(runSettings.MarkerMoves.Runs);

        var neither = WithoutChangedObjectsTerrainAndOldPlaces(SettingsFor());
        neither.TerrainChange.RemoveOnChangedTerrain = false;
        neither.LeftoverInvisibleObjects.MoveKeptMarkersOutOfOtherModsObjects = false;
        neither.FollowUpRemoval = new FollowUpRemovalSettings { Mode = FollowUpRemovalMode.Nothing };
        TurnRestoreWhereRemovedOff(neither);
        Assert.Equal(new WhatToCollect(new ScanNeeds(), 10f, ProcessReplaced: false), BuildReady(neither).Settings.Read);

        var restoring = WithoutChangedObjectsTerrainAndOldPlaces(SettingsFor());
        restoring.TerrainChange.RemoveOnChangedTerrain = false;
        restoring.LeftoverInvisibleObjects.MoveKeptMarkersOutOfOtherModsObjects = false;
        restoring.FollowUpRemoval = new FollowUpRemovalSettings { Mode = FollowUpRemovalMode.Nothing };
        var restoringSettings = BuildReady(restoring).Settings;
        Assert.Equal(
            new WhatToCollect(
                new ScanNeeds(Navmesh: true, JudgeOldPlaces: true, AreaRecordVersions: AreaReadsOf(restoringSettings), DecideEveryObjectWithoutCleanedMod: true),
                10f,
                ProcessReplaced: false),
            restoringSettings.Read);
    }

    /// <summary>Only restoring other mods' objects where the cleaned mod was removed decides every object in the world without the cleaned mod.</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void EveryObjectIsDecidedWithoutTheCleanedModOnlyWhenOtherModsObjectsAreRestoredWhereItWasRemoved(bool otherModsObjects, bool decided)
    {
        var settings = SettingsFor();
        settings.RestoreWhereRemoved.RestoreOtherModsObjects = otherModsObjects;

        Assert.Equal(decided, BuildReady(settings).Settings.Read.Needs.DecideEveryObjectWithoutCleanedMod);
    }

    private static void TurnRestoreWhereRemovedOff(StandardSettings settings)
    {
        settings.RestoreWhereRemoved.RestoreOtherModsObjects = false;
        settings.RestoreWhereRemoved.RestoreTerrain = false;
        settings.RestoreWhereRemoved.RestoreCells = false;
        settings.RestoreWhereRemoved.RestoreNavmeshes = false;
    }

    /// <remarks>Every restore switch is on by default, so every kind is read.</remarks>
    private static AreaRecordReads AreaReadsOf(RunSettings settings) =>
        new(new AreaPartTolerances(settings.RestoreWhereRemoved!.HeightChangeTolerance, settings.RestoreWhereRemoved.WaterHeightChangeTolerance), Cells: true, Lands: true);

    /// <summary>An object counts as resting only within the touch gap, so the drop test stops at the first distance within it without measuring up to the pull limit.</summary>
    [Fact]
    public void TheLargestTouchGapIsBelowTheLimitOtherModsObjectsArePulledDownBy() =>
        Assert.True(AlsoRemoveSettingsBuilder.MaxTouchDistance < OtherModsObjectsLimits.MaxPullDistance);

    [Fact]
    public void OtherModsObjectsAreJudgedByTheGapAndShareOfTheAlsoRemoveGroup()
    {
        var settings = SettingsFor();
        settings.FollowUpRemoval = new FollowUpRemovalSettings
        {
            Mode = FollowUpRemovalMode.Nothing,
            TouchDistance = 12f,
            AnchoringThresholdPercent = 40f,
            OtherModsObjectsLeftWithoutSupport = OtherModsObjectsHandling.RemoveFloatingObject,
        };

        var otherModsObjects = BuildReady(settings).Settings.OtherModsObjects;

        Assert.Equal(new OtherModsObjectsSettings(OtherModsObjectsHandling.RemoveFloatingObject, 12f, 0.4f), otherModsObjects);
    }

    [Fact]
    public void OtherModsObjectsArePulledToTheGroundByDefault() =>
        Assert.Equal(OtherModsObjectsHandling.PullObjectToTheGround, BuildReady(SettingsFor()).Settings.OtherModsObjects.Handling);

    [Fact]
    public void AnUndefinedOtherModsObjectsValueIsReplacedByTheDefaultWithAWarning()
    {
        var settings = SettingsFor();
        settings.FollowUpRemoval.OtherModsObjectsLeftWithoutSupport = (OtherModsObjectsHandling)99;

        var ready = BuildReady(settings);

        Assert.Equal(SettingDefaults.OtherModsObjects, ready.Settings.OtherModsObjects.Handling);
        Assert.Contains(ready.Warnings, warning => warning.Message.Contains("Other mods' objects left without support", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, true)]
    public void WaterIsReadOnlyForTheChangedTerrainStepCountingAWaterChange(bool on, bool waterHeights, bool waterAddedOrRemoved, bool water)
    {
        var settings = WithoutChangedObjectsTerrainAndOldPlaces(SettingsFor());
        settings.TerrainChange = new TerrainChangeSettings
        {
            RemoveOnChangedTerrain = on,
            CountWaterHeightChanges = waterHeights,
            CountWaterAddedOrRemoved = waterAddedOrRemoved,
        };

        Assert.Equal(water, BuildReady(settings).Settings.Read.Needs.Water);
    }

    [Theory]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, true, true)]
    public void TerrainVersionsAreReadOnlyForTheChangedTerrainStepAndLayersOnlyForTerrainTypes(bool on, bool types, bool versions, bool layers)
    {
        var settings = WithoutChangedObjectsTerrainAndOldPlaces(SettingsFor());
        settings.TerrainChange = new TerrainChangeSettings { RemoveOnChangedTerrain = on, CountTerrainTypeChanges = types };

        var read = BuildReady(settings).Settings.Read.Needs;

        Assert.Equal((versions, layers), (read.TerrainVersions, read.TerrainLayers));
    }

    [Fact]
    public void TheChangedTerrainSettingsAreOnByDefaultAndPassedThrough()
    {
        var defaults = BuildReady(SettingsFor()).Settings.Collision.ChangedTerrain;
        Assert.Equal(
            new ChangedTerrainSettings(
                On: true, TerrainCheckedObjects.AllObjectsIncludingInvisibleOnes, CountHeightChanges: true, HeightTolerance: 16f, CountTerrainTypeChanges: true,
                MinTerrainTypeChangePercent: 20, CountWaterHeightChanges: true, WaterHeightTolerance: 16f, CountWaterAddedOrRemoved: true,
                MinWaterCoverChangePercent: 20, AreaBySize: true, AreaMultiplier: 0.5f, AreaByDistance: true, AreaDistanceInUnits: 64f),
            defaults);

        var settings = SettingsFor();
        settings.TerrainChange = new TerrainChangeSettings
        {
            RemoveOnChangedTerrain = true,
            TerrainCheckedObjects = TerrainCheckedObjects.AllObjectsIncludingInvisibleOnes,
            CountHeightChanges = false,
            HeightChangeTolerance = 0f,
            MinTerrainTypeChangePercent = 50,
            CountWaterHeightChanges = false,
            WaterHeightChangeTolerance = 32f,
            CountWaterAddedOrRemoved = false,
            MinWaterCoverChangePercent = 40,
            BySize = new TerrainBySizeSettings { Use = true, SizeMultiplier = 2f },
            ByDistance = new TerrainByDistanceSettings { Use = true, DistanceInUnits = 128f },
        };
        var ready = BuildReady(settings);

        Assert.Empty(ready.Warnings);
        Assert.Equal(
            new ChangedTerrainSettings(
                On: true, TerrainCheckedObjects.AllObjectsIncludingInvisibleOnes, false, 0f, true, 50, false, 32f, false, 40,
                AreaBySize: true, AreaMultiplier: 2f, AreaByDistance: true, AreaDistanceInUnits: 128f),
            ready.Settings.Collision.ChangedTerrain);
    }

    [Fact]
    public void ChangedTerrainWithNeitherTerrainMarginOnIsSwitchedOffWithAWarning()
    {
        var settings = WithoutChangedObjectsTerrainAndOldPlaces(SettingsFor());
        settings.TerrainChange = new TerrainChangeSettings
        {
            RemoveOnChangedTerrain = true,
            BySize = new TerrainBySizeSettings { Use = false },
            ByDistance = new TerrainByDistanceSettings { Use = false },
        };

        var ready = BuildReady(settings);

        Assert.False(ready.Settings.Collision.ChangedTerrain.Runs);
        Assert.False(ready.Settings.Read.Needs.TerrainVersions);
        Assert.Equal(
            "Warning: changed terrain is on, but neither terrain margin is on; no object is removed for changed terrain.",
            Assert.Single(ready.Warnings).Message);
    }

    [Fact]
    public void TerrainMarginsAboveTheirLimitsAreClampedWithAWarningEach()
    {
        var settings = SettingsFor();
        settings.TerrainChange.BySize = new TerrainBySizeSettings { Use = true, SizeMultiplier = 9f };
        settings.TerrainChange.ByDistance = new TerrainByDistanceSettings { Use = true, DistanceInUnits = 5000f };

        var ready = BuildReady(settings);

        Assert.Equal(5f, ready.Settings.Collision.ChangedTerrain.AreaMultiplier);
        Assert.Equal(1024f, ready.Settings.Collision.ChangedTerrain.AreaDistanceInUnits);
        string[] expectedStarts =
        [
            "Warning: Remove objects on terrain changed by other mods > Margin by object size > Margin on every side 9",
            "Warning: Remove objects on terrain changed by other mods > Margin by fixed distance > Margin on every side 5000",
        ];
        Assert.Equal(expectedStarts.Length, ready.Warnings.Length);
        Assert.All(expectedStarts.Zip(ready.Warnings), pair => Assert.StartsWith(pair.First, pair.Second.Message));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(100)]
    public void AGroundTypePercentFromZeroTo100IsKeptWithoutAWarning(int percent)
    {
        var settings = SettingsFor();
        settings.TerrainChange.MinTerrainTypeChangePercent = percent;

        var ready = BuildReady(settings);

        Assert.Equal(percent, ready.Settings.Collision.ChangedTerrain.MinTerrainTypeChangePercent);
        Assert.Empty(ready.Warnings);
    }

    [Theory]
    [InlineData(-1, 0, "Warning: Remove objects on terrain changed by other mods > Remove only when this share of the ground points changed type -1 is outside 0-100")]
    [InlineData(101, 100, "Warning: Remove objects on terrain changed by other mods > Remove only when this share of the ground points changed type 101 is outside 0-100")]
    public void AGroundTypePercentOutsideZeroTo100IsCorrectedWithAWarning(int percent, int expected, string expectedStart)
    {
        var settings = SettingsFor();
        settings.TerrainChange.MinTerrainTypeChangePercent = percent;

        var ready = BuildReady(settings);

        Assert.Equal(expected, ready.Settings.Collision.ChangedTerrain.MinTerrainTypeChangePercent);
        Assert.StartsWith(expectedStart, Assert.Single(ready.Warnings).Message);
    }

    [Theory]
    [InlineData(false, ZoneShape.ObjectShape)]
    [InlineData(true, ZoneShape.BoundingBox)]
    public void RoughBoxesChooseTheBoundingBoxZoneShape(bool useRoughBoxes, ZoneShape expected)
    {
        var settings = SettingsFor();
        settings.TooClose.UseRoughBoxes = useRoughBoxes;

        Assert.Equal(expected, BuildReady(settings).Settings.Collision.TooClose.Zone);
    }

    [Fact]
    public void TickedAlwaysKeepKindsAreTheKindsNeverRemoved()
    {
        var settings = SettingsFor();
        settings.LeftoverInvisibleObjects.AlwaysKeep = new KeptInvisibleObjectSettings { Lights = true, SoundMarkers = true };

        var neverRemove = BuildReady(settings).Settings.LeftBehind!.NeverRemove;

        Assert.Equal(new[] { InvisibleObjectKind.Lights, InvisibleObjectKind.SoundMarkers }, neverRemove.Order());
    }

    [Fact]
    public void NothingIsKeptUntilAKindIsTicked()
    {
        Assert.Empty(BuildReady(SettingsFor()).Settings.LeftBehind!.NeverRemove);
    }

    [Fact]
    public void ChangedTerrainCountingNoChangeIsSwitchedOffWithAWarning()
    {
        var settings = WithoutChangedObjectsTerrainAndOldPlaces(SettingsFor());
        settings.TerrainChange = new TerrainChangeSettings
        {
            RemoveOnChangedTerrain = true,
            CountHeightChanges = false,
            CountTerrainTypeChanges = false,
            CountWaterHeightChanges = false,
            CountWaterAddedOrRemoved = false,
        };

        var ready = BuildReady(settings);

        Assert.False(ready.Settings.Collision.ChangedTerrain.Runs);
        Assert.False(ready.Settings.Read.Needs.TerrainVersions);
        Assert.Equal(
            "Warning: changed terrain is on, but no height, ground type or water changes are counted; no object is removed for changed terrain.",
            Assert.Single(ready.Warnings).Message);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ChangedTerrainCountingOnlyAWaterChangeStaysOn(bool waterHeights, bool waterAddedOrRemoved)
    {
        var settings = SettingsFor();
        settings.TerrainChange = new TerrainChangeSettings
        {
            RemoveOnChangedTerrain = true,
            CountHeightChanges = false,
            CountTerrainTypeChanges = false,
            CountWaterHeightChanges = waterHeights,
            CountWaterAddedOrRemoved = waterAddedOrRemoved,
        };

        var ready = BuildReady(settings);

        Assert.True(ready.Settings.Collision.ChangedTerrain.Runs);
        Assert.True(ready.Settings.Read.Needs.TerrainVersions);
        Assert.Empty(ready.Warnings);
    }

    [Fact]
    public void InvalidWaterValuesAreCorrectedWithAWarningEach()
    {
        var settings = SettingsFor();
        settings.TerrainChange.WaterHeightChangeTolerance = -5f;
        settings.TerrainChange.MinWaterCoverChangePercent = 101;

        var ready = BuildReady(settings);

        Assert.Equal((0f, 100), (ready.Settings.Collision.ChangedTerrain.WaterHeightTolerance, ready.Settings.Collision.ChangedTerrain.MinWaterCoverChangePercent));
        string[] expectedStarts =
        [
            "Warning: Remove objects on terrain changed by other mods > Count only water height changes larger than -5",
            "Warning: Remove objects on terrain changed by other mods > Remove only when this share of the ground got covered/uncovered 101 is outside 0-100",
        ];
        Assert.Equal(expectedStarts.Length, ready.Warnings.Length);
        Assert.All(expectedStarts.Zip(ready.Warnings), pair => Assert.StartsWith(pair.First, pair.Second.Message));
    }

    [Fact]
    public void InvalidChangedTerrainValuesAreCorrectedEvenWhenTheStepIsOff()
    {
        var settings = SettingsFor();
        settings.TerrainChange = new TerrainChangeSettings
        {
            RemoveOnChangedTerrain = false,
            TerrainCheckedObjects = (TerrainCheckedObjects)99,
            HeightChangeTolerance = 300f,
            BySize = new TerrainBySizeSettings { SizeMultiplier = float.NaN },
            ByDistance = new TerrainByDistanceSettings { Use = true, DistanceInUnits = -1f },
        };

        var ready = BuildReady(settings);

        Assert.Equal(
            new ChangedTerrainSettings(
                On: false, TerrainCheckedObjects.AllObjectsIncludingInvisibleOnes, true, 256f, true, 20, true, 16f, true, 20,
                AreaBySize: true, AreaMultiplier: 0.5f, AreaByDistance: true, AreaDistanceInUnits: 0f),
            ready.Settings.Collision.ChangedTerrain);
        string[] expectedStarts =
        [
            "Warning: Remove objects on terrain changed by other mods > Which of the cleaned mod's objects are checked",
            "Warning: Remove objects on terrain changed by other mods > Count only height changes larger than 300",
            "Warning: Remove objects on terrain changed by other mods > Margin by object size > Margin on every side is not a valid number",
            "Warning: Remove objects on terrain changed by other mods > Margin by fixed distance > Margin on every side -1",
        ];
        Assert.Equal(expectedStarts.Length, ready.Warnings.Length);
        Assert.All(expectedStarts.Zip(ready.Warnings), pair => Assert.StartsWith(pair.First, pair.Second.Message));
    }

    [Fact]
    public void MarkerMovesNeedTheLeftBehindStep()
    {
        var settings = SettingsFor();
        settings.LeftoverInvisibleObjects = new LeftoverInvisibleObjectSettings
        {
            RemoveLeftoverInvisibleObjects = false,
            MoveKeptMarkersOutOfOtherModsObjects = true,
        };
        var runSettings = BuildReady(settings).Settings;
        Assert.False(runSettings.MarkerMoves.Runs);
        Assert.Null(runSettings.LeftBehind);
    }

    [Fact]
    public void SupportThresholdBecomesAFraction()
    {
        var settings = SettingsFor();
        settings.FollowUpRemoval = new FollowUpRemovalSettings { AnchoringThresholdPercent = 25f };
        Assert.Equal(0.25f, BuildReady(settings).Settings.AlsoRemove.SupportLostFraction);
    }

    [Fact]
    public void StandardReportsGoToTheReportsFolderItself() =>
        Assert.Equal(ReportsFolder, BuildReady(SettingsFor(), detailedLogAndReports: true).Settings.Reports?.Folder);

    [Fact]
    public void AConfigFilesReportsGoToASubfolderNamedAfterTheConfig()
    {
        var read = new ConfigsRead.Ready([new ConfiguredSettings(ConfigSource("town_walls", 1, 1), SettingsFor())]);
        var batch = Assert.IsType<BatchRequest.Ready>(BatchRequests.BuildAll(read, new PluginNameParsingPlugin(), Facts(), detailedLogAndReports: true, onlyWarnWhenRequiringModLoadsAfterPatch: false, ReportsFolder, 1));
        var ready = Assert.IsType<RunRequest.Ready>(Assert.Single(batch.Requests));

        Assert.Equal(Path.Combine(ReportsFolder, "town_walls"), ready.Settings.Reports?.Folder);
    }

    [Fact]
    public void TheSourceReachesTheRequest()
    {
        var source = ConfigSource("town_walls", 1, 2);

        var request = Assert.Single(BuildBatch(source, SettingsFor()).Requests);

        Assert.Same(source, request.Source);
        Assert.IsType<SettingsSource.Standard>(BuildReady(SettingsFor()).Source);
    }

    [Fact]
    public void AReadFailurePassesThroughUnchanged()
    {
        var failed = BuildAll(new ConfigsRead.Failed("Group file x is missing."));

        Assert.Equal("Group file x is missing.", Assert.IsType<BatchRequest.Failed>(failed).Message);
    }

    [Fact]
    public void AConfigFileWithoutATargetFailsTheBatchNamingTheFile()
    {
        var failed = FailedBatch(ConfigSource("town_walls", 1, 1), new StandardSettings());

        Assert.Equal(
            $"Config file {FilePathOf("town_walls")} sets no mod to clean up (CleanedMod). Nothing was run and no patch was written.",
            failed.Message);
    }

    [Fact]
    public void AConfigFileWithATargetNotInThePluginListFailsTheBatchNamingTheFile()
    {
        var failed = FailedBatch(ConfigSource("town_walls", 1, 1), SettingsFor("Missing.esp"));

        Assert.Equal(
            $"Config file {FilePathOf("town_walls")}: Missing.esp is not in your plugin list. Nothing was run and no patch was written.",
            failed.Message);
    }

    [Fact]
    public void AConfigFileWithADisabledTargetFailsTheBatchNamingTheFile()
    {
        var failed = FailedBatch(ConfigSource("town_walls", 1, 1), SettingsFor(), DisabledTargetFacts);

        Assert.Equal(
            $"Config file {FilePathOf("town_walls")}: Target.esp is disabled. Nothing was run and no patch was written.",
            failed.Message);
    }

    [Fact]
    public void AConfigFileWithATargetNotLoadedBySynthesisFailsTheBatchNamingTheFile()
    {
        var failed = FailedBatch(ConfigSource("town_walls", 1, 1), SettingsFor(), NotLoadedTargetFacts);

        Assert.Equal(
            $"Config file {FilePathOf("town_walls")}: Target.esp is enabled in your plugin list but Synthesis did not load it for this group. Nothing was run and no patch was written.",
            failed.Message);
    }

    [Fact]
    public void AConfigFileWithATargetLoadingAfterThePatchFailsTheBatchNamingTheFileAndBothPositions()
    {
        var failed = FailedBatch(ConfigSource("town_walls", 1, 1), SettingsFor(), TargetAfterPatchFacts);

        Assert.Equal(
            $"Config file {FilePathOf("town_walls")}: Target.esp loads after the patch Patch.esp (position 3 > 1): move Patch.esp below it. Nothing was run and no patch was written.",
            failed.Message);
    }

    [Fact]
    public void AConfigFileWithThePatchPluginAsTargetFailsTheBatchNamingTheFile()
    {
        var failed = FailedBatch(ConfigSource("town_walls", 1, 1), SettingsFor("Patch.esp"));

        Assert.Equal(
            $"Config file {FilePathOf("town_walls")}: Patch.esp is the patch this patcher writes. Nothing was run and no patch was written.",
            failed.Message);
    }

    [Fact]
    public void TheFirstConfigFileThatStopsIsTheOneReportedInGroupOrder()
    {
        var read = new ConfigsRead.Ready(
        [
            new ConfiguredSettings(ConfigSource("first", 1, 3), SettingsFor()),
            new ConfiguredSettings(ConfigSource("second", 2, 3), new StandardSettings()),
            new ConfiguredSettings(ConfigSource("third", 3, 3), SettingsFor("Missing.esp")),
        ]);

        var failed = Assert.IsType<BatchRequest.Failed>(BuildAll(read));

        Assert.StartsWith($"Config file {FilePathOf("second")} sets no mod", failed.Message);
    }

    [Fact]
    public void TwoConfigFilesWithTheSameTargetFailTheBatchNamingBothFiles()
    {
        var read = new ConfigsRead.Ready(
        [
            new ConfiguredSettings(ConfigSource("first", 1, 2), SettingsFor("Target.esp")),
            new ConfiguredSettings(ConfigSource("second", 2, 2), SettingsFor("target.ESP")),
        ]);

        var failed = Assert.IsType<BatchRequest.Failed>(BuildAll(read));

        Assert.Equal(
            $"Config files {FilePathOf("first")} and {FilePathOf("second")} both clean up Target.esp; each mod may be cleaned up by one config only. Nothing was run and no patch was written.",
            failed.Message);
    }

    [Fact]
    public void AStandardRequestThatStopsStaysAStopRequest()
    {
        var read = new ConfigsRead.Ready([StandardConfig.Of(new StandardSettings())]);

        var batch = Assert.IsType<BatchRequest.Ready>(BuildAll(read));

        Assert.Equal(new StopReason(StopKind.NoTargetSet, null), Assert.IsType<RunRequest.Stop>(Assert.Single(batch.Requests)).Reason);
    }

    private static string FilePathOf(string name) => Path.Combine("configs", name + ".json");

    private static SettingsSource.ConfigFile ConfigSource(string name, int number, int count) =>
        new(name, name + ".json", FilePathOf(name), number, count, new ConfigFileNotes([], [], []));

    private static BatchRequest.Ready BuildBatch(SettingsSource source, StandardSettings settings) =>
        Assert.IsType<BatchRequest.Ready>(BuildAll(new ConfigsRead.Ready([new ConfiguredSettings(source, settings)])));

    private static BatchRequest.Failed FailedBatch(SettingsSource source, StandardSettings settings, LoadOrderPlugins? facts = null) =>
        Assert.IsType<BatchRequest.Failed>(BuildAll(new ConfigsRead.Ready([new ConfiguredSettings(source, settings)]), facts));

    [Theory]
    [InlineData(50, 50)]
    [InlineData(44, 40)]
    [InlineData(45, 50)]
    [InlineData(5, 10)]
    [InlineData(105, 100)]
    public void PercentIsClampedAndRoundedToWholeTens(int percent, int expected) =>
        Assert.Equal(expected, SettingCorrections.WholeTens(percent, "test percent", new List<SettingWarning>()));

    [Fact]
    public void ANumberThatIsNotANumberBecomesTheDefaultWithAWarning()
    {
        var warnings = new List<SettingWarning>();
        Assert.Equal(0.5f, SettingCorrections.Clamp(float.NaN, 0f, 5f, 0.5f, "test number", warnings));
        Assert.Equal("Warning: test number is not a valid number; using 0.5.", Assert.Single(warnings).Message);
    }

    private const string ChangedCollisionGroup = "Warning: Other mods' objects the cleaned mod changed > Collision handling > ";

    [Fact]
    public void TheChangedObjectsDefaultToRestoringWhenTheOldPlaceIsFreeWithoutResizingAndWithoutProcessingReplacedObjects()
    {
        var ready = BuildReady(SettingsFor());

        Assert.Equal(new ChangedObjectSettings(RestoreMode.RestoreIfNoCollision, RestoreMode.RestoreIfNoCollision), ready.Settings.ChangedObjects);
        Assert.False(ready.Settings.Read.ProcessReplaced);
        Assert.True(ready.Settings.Read.Needs.JudgeOldPlaces);
        Assert.Equal(ready.Settings.Collision with { Fit = FitSettings.Off }, ready.Settings.ChangedObjectCollision.ForMoved());
        Assert.Equal(ready.Settings.Collision with { Fit = FitSettings.Off }, ready.Settings.ChangedObjectCollision.ForReplaced());
        Assert.Equal(ready.Settings.Collision, ready.Settings.ChangedObjectCollision.Section());
        Assert.Empty(ready.Warnings);
    }

    [Fact]
    public void TheChangedObjectsSettingsReachTheRun()
    {
        var settings = SettingsFor();
        settings.ChangedObjects.Moved.WhenRemoved = RestoreMode.Restore;
        settings.ChangedObjects.Replaced = new ReplacedObjectSettings { Process = true, WhenRemoved = RestoreMode.Remove };

        var ready = BuildReady(settings);

        Assert.Equal(new ChangedObjectSettings(RestoreMode.Restore, RestoreMode.Remove), ready.Settings.ChangedObjects);
        Assert.True(ready.Settings.Read.ProcessReplaced);
        Assert.Empty(ready.Warnings);
    }

    [Fact]
    public void AnUndefinedWhenRemovedValueIsReplacedByTheDefaultWithAWarningNamingItsGroup()
    {
        var settings = SettingsFor();
        settings.ChangedObjects.Moved.WhenRemoved = (RestoreMode)99;
        settings.ChangedObjects.Replaced.WhenRemoved = (RestoreMode)98;

        var ready = BuildReady(settings);

        Assert.Equal(new ChangedObjectSettings(SettingDefaults.MovedObjectsWhenRemoved, SettingDefaults.ReplacedObjectsWhenRemoved), ready.Settings.ChangedObjects);
        Assert.Equal(
            [
                "Warning: Other mods' objects the cleaned mod changed > Moved objects > When it would be removed 99 is invalid; using RestoreIfNoCollision.",
                "Warning: Other mods' objects the cleaned mod changed > Replaced objects > When it would be removed 98 is invalid; using RestoreIfNoCollision.",
            ],
            ready.Warnings.Select(warning => warning.Message));
    }

    [Fact]
    public void TheChangedObjectsCollisionIsValidatedLikeTheStandardOneUnderItsOwnGroupPathInScreenOrder()
    {
        var settings = SettingsFor();
        settings.ChangedObjects.Collision.TooClose.NpcHandling = (NpcHandling)99;
        settings.ChangedObjects.Collision.TooClose.BySize.SizeMultiplier = 9f;
        settings.ChangedObjects.Collision.TerrainChange.TerrainCheckedObjects = (TerrainCheckedObjects)99;
        settings.ChangedObjects.Collision.ShrinkLimitPercent = 0;
        settings.ChangedObjects.Collision.TurnLimitDegrees = -10;
        settings.ChangedObjects.Moved.MovedDistanceInUnits = -1f;
        settings.ChangedObjects.Moved.WhenRemoved = (RestoreMode)99;
        settings.Trust = new TrustSettings { MaxOtherMastersOfRequiringModToTrust = 500 };
        settings.TooClose.NpcHandling = (NpcHandling)99;

        var messages = BuildReady(settings).Warnings.Select(warning => warning.Message).ToList();

        string[] expectedStarts =
        [
            "Warning: Other mods' objects the cleaned mod changed > Moved objects > Count as moved when moved farther than -1 is below 0",
            "Warning: Other mods' objects the cleaned mod changed > Moved objects > When it would be removed 99 is invalid",
            ChangedCollisionGroup + "Remove objects too close to other mods' objects > Other mods' NPCs and creatures cause removal 99 is invalid",
            ChangedCollisionGroup + "Remove objects too close to other mods' objects > Margin by object size > Margin on every side 9 is outside 0-5; using 5.",
            ChangedCollisionGroup + "Remove objects on terrain changed by other mods > Which of the cleaned mod's objects are checked 99 is invalid",
            ChangedCollisionGroup + "Shrink down to 0 is below 1; using 1.",
            ChangedCollisionGroup + "Turn by at most -10 is below 0; using 0.",
            "Warning: Which other mods can cause removals > ...only if it has at most this many other masters 500 is outside 0-100",
            "Warning: Remove objects too close to other mods' objects > Other mods' NPCs and creatures cause removal 99 is invalid",
        ];
        Assert.Equal(expectedStarts.Length, messages.Count);
        for (var i = 0; i < expectedStarts.Length; i++)
        {
            Assert.StartsWith(expectedStarts[i], messages[i]);
        }
    }

    [Fact]
    public void TheChangedObjectsCollisionIsValidatedEvenWhenNoObjectIsProcessedWithIt()
    {
        var settings = SettingsFor();
        settings.ChangedObjects.Moved.WhenRemoved = RestoreMode.Remove;
        settings.ChangedObjects.Collision.TooClose.BySize.SizeMultiplier = 9f;

        var ready = BuildReady(settings);

        Assert.Equal(5f, ready.Settings.ChangedObjectCollision.TooClose.Multiplier);
        Assert.Equal(SettingDefaults.SizeMultiplier, ready.Settings.Collision.TooClose.Multiplier);
        Assert.StartsWith(ChangedCollisionGroup + "Remove objects too close to other mods' objects > Margin by object size", Assert.Single(ready.Warnings).Message);
    }

    [Fact]
    public void TheChangedObjectsTooCloseWithNeitherMarginOnGivesAWarningNamingItsGroup()
    {
        var settings = SettingsFor();
        settings.ChangedObjects.Collision.TooClose.BySize.Use = false;
        settings.ChangedObjects.Collision.TooClose.ByDistance.Use = false;

        var ready = BuildReady(settings);

        Assert.False(ready.Settings.ChangedObjectCollision.TooClose.BySize || ready.Settings.ChangedObjectCollision.TooClose.ByDistance);
        Assert.True(ready.Settings.Collision.TooClose.BySize);
        Assert.Equal(
            ChangedCollisionGroup + "neither too-close margin is on; no changed object is found too close, neither where the cleaned mod put it nor at its old place.",
            Assert.Single(ready.Warnings).Message);
    }

    [Fact]
    public void TheChangedObjectsTerrainIsSwitchedOffWithAWarningNamingItsGroupWhenItCountsNothing()
    {
        var settings = SettingsFor();
        settings.ChangedObjects.Collision.TerrainChange = new TerrainChangeSettings
        {
            CountHeightChanges = false,
            CountTerrainTypeChanges = false,
            CountWaterHeightChanges = false,
            CountWaterAddedOrRemoved = false,
        };

        var ready = BuildReady(settings);

        Assert.False(ready.Settings.ChangedObjectCollision.ChangedTerrain.Runs);
        Assert.True(ready.Settings.Collision.ChangedTerrain.Runs);
        Assert.Equal(
            ChangedCollisionGroup + "changed terrain is on, but no height, ground type or water changes are counted; "
            + "no changed object is found on changed terrain, neither where the cleaned mod put it nor at its old place.",
            Assert.Single(ready.Warnings).Message);
    }

    [Fact]
    public void TheChangedObjectsTerrainIsSwitchedOffWithAWarningNamingItsGroupWhenNeitherMarginIsOn()
    {
        var settings = SettingsFor();
        settings.ChangedObjects.Collision.TerrainChange.BySize.Use = false;
        settings.ChangedObjects.Collision.TerrainChange.ByDistance.Use = false;

        var ready = BuildReady(settings);

        Assert.False(ready.Settings.ChangedObjectCollision.ChangedTerrain.Runs);
        Assert.Equal(
            ChangedCollisionGroup + "changed terrain is on, but neither terrain margin is on; "
            + "no changed object is found on changed terrain, neither where the cleaned mod put it nor at its old place.",
            Assert.Single(ready.Warnings).Message);
    }

    [Theory]
    [InlineData(RestoreMode.RestoreIfNoCollision, RestoreMode.Remove, false, true)]
    [InlineData(RestoreMode.RestoreIfNoCollision, RestoreMode.RestoreIfNoCollision, false, true)]
    [InlineData(RestoreMode.Remove, RestoreMode.RestoreIfNoCollision, false, false)]
    [InlineData(RestoreMode.Remove, RestoreMode.RestoreIfNoCollision, true, true)]
    [InlineData(RestoreMode.Restore, RestoreMode.Restore, true, false)]
    [InlineData(RestoreMode.Remove, RestoreMode.Remove, true, false)]
    public void TheOldPlacesAreJudgedOnlyWhenAMovedObjectOrAProcessedReplacedObjectChecksThem(RestoreMode moved, RestoreMode replaced, bool process, bool judged)
    {
        var settings = SettingsFor();
        settings.ChangedObjects.Moved.WhenRemoved = moved;
        settings.ChangedObjects.Replaced = new ReplacedObjectSettings { Process = process, WhenRemoved = replaced };
        settings.RestoreWhereRemoved.RestoreOtherModsObjects = false;

        var read = BuildReady(settings).Settings.Read;

        Assert.Equal((process, judged), (read.ProcessReplaced, read.Needs.JudgeOldPlaces));
    }

    /// <summary>The objects that go back where the cleaned mod was removed are checked against the changes of the world without it.</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void TheOldPlacesAreJudgedWhenOtherModsObjectsGoBackWhereTheCleanedModWasRemovedWhateverTheModes(bool restoreObjects, bool judged)
    {
        var settings = SettingsFor();
        settings.ChangedObjects.Moved.WhenRemoved = RestoreMode.Remove;
        settings.ChangedObjects.Replaced = new ReplacedObjectSettings { Process = true, WhenRemoved = RestoreMode.Restore };
        settings.RestoreWhereRemoved.RestoreOtherModsObjects = restoreObjects;

        Assert.Equal(judged, BuildReady(settings).Settings.Read.Needs.JudgeOldPlaces);
    }

    [Theory]
    [InlineData(false, true, false, true, false, false, false)]
    [InlineData(true, false, false, false, true, false, false)]
    [InlineData(true, true, false, false, true, true, false)]
    [InlineData(true, false, true, false, true, false, true)]
    public void TheTerrainIsReadWhenOnlyTheChangedObjectsTerrainNeedsIt(
        bool on, bool types, bool waterHeights, bool waterAddedOrRemoved, bool versions, bool layers, bool water)
    {
        var settings = SettingsFor();
        settings.TerrainChange.RemoveOnChangedTerrain = false;
        settings.ChangedObjects.Moved.WhenRemoved = RestoreMode.Remove;
        settings.ChangedObjects.Collision.TerrainChange = new TerrainChangeSettings
        {
            RemoveOnChangedTerrain = on,
            CountTerrainTypeChanges = types,
            CountWaterHeightChanges = waterHeights,
            CountWaterAddedOrRemoved = waterAddedOrRemoved,
        };

        var read = BuildReady(settings).Settings.Read.Needs;

        Assert.Equal((versions, layers, water), (read.TerrainVersions, read.TerrainLayers, read.Water));
    }

    [Fact]
    public void TheTerrainIsReadWhenOnlyTheStandardTerrainNeedsItAndWhenBothDo()
    {
        var settings = SettingsFor();
        settings.ChangedObjects.Collision.TerrainChange.RemoveOnChangedTerrain = false;

        Assert.Equal((true, true, true), TerrainReadsOf(settings));

        settings.ChangedObjects.Collision.TerrainChange.RemoveOnChangedTerrain = true;

        Assert.Equal((true, true, true), TerrainReadsOf(settings));
    }

    /// <summary>The restore group reads the records the cleaned mod changed itself, so it does not change when the terrain reads wait for an override.</summary>
    [Theory]
    [InlineData(false, true, false, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, true, true)]
    [InlineData(true, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, false, true, false)]
    public void TheTerrainReadsNeedAnOverrideOnlyWhenNothingButTheChangedObjectsTerrainAsksForThemWhetherOrNotTheRestoreGroupIsOn(
        bool standard, bool changedObjects, bool restoreGroup, bool needsAnOverride)
    {
        var settings = SettingsFor();
        if (!restoreGroup) settings.RestoreWhereRemoved = AllRestoresOff();
        settings.TerrainChange.RemoveOnChangedTerrain = standard;
        settings.ChangedObjects.Collision.TerrainChange.RemoveOnChangedTerrain = changedObjects;

        Assert.Equal(needsAnOverride, BuildReady(settings).Settings.Read.Needs.TerrainForChangedObjectsOnly);
    }

    private static RestoreWhereRemovedSettings AllRestoresOff() =>
        new() { RestoreOtherModsObjects = false, RestoreTerrain = false, RestoreCells = false, RestoreNavmeshes = false };

    private static (bool Versions, bool Layers, bool Water) TerrainReadsOf(StandardSettings settings)
    {
        var read = BuildReady(settings).Settings.Read.Needs;
        return (read.TerrainVersions, read.TerrainLayers, read.Water);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AnObjectIsFittedOnlyWhenItsOwnOriginsResizingSwitchIsOn(bool resizeMoved, bool resizeReplaced)
    {
        var limits = new FitSettings(60, 90);
        var settings = SettingsFor();
        settings.ChangedObjects.Moved.AttemptResizingBeforeRemoval = resizeMoved;
        settings.ChangedObjects.Replaced.AttemptResizingBeforeRemoval = resizeReplaced;
        settings.ChangedObjects.Collision.ShrinkLimitPercent = limits.ShrinkLimitPercent;
        settings.ChangedObjects.Collision.TurnLimitDegrees = limits.TurnLimitDegrees;

        var ready = BuildReady(settings);

        var collision = ready.Settings.ChangedObjectCollision;
        Assert.Equal(resizeMoved ? limits : FitSettings.Off, collision.ForMoved().Fit);
        Assert.Equal(resizeReplaced ? limits : FitSettings.Off, collision.ForReplaced().Fit);
        Assert.Equal(limits, collision.Section().Fit);
        Assert.Equal(new FitSettings(80, 360), ready.Settings.Collision.Fit);
        Assert.Empty(ready.Warnings);
    }

    [Fact]
    public void ThereIsOneCollisionGroupForEachOriginThatHasObjectsInTargetOrderAndNoEmptyGroup()
    {
        var settings = SettingsFor();
        settings.ChangedObjects.Moved.AttemptResizingBeforeRemoval = true;
        var run = BuildReady(settings).Settings;
        TargetObject[] targets =
        [
            TargetOf(0, TargetOrigin.Replaced), TargetOf(1, TargetOrigin.CleanedMod), TargetOf(2, TargetOrigin.Replaced), TargetOf(3, TargetOrigin.CleanedMod),
        ];

        var groups = run.CollisionGroupsOf([.. targets]);

        Assert.Equal([[1, 3], [0, 2]], groups.Select(group => group.Objects.Select(id => id.Index).ToArray()));
        Assert.Equal([run.Collision, run.ChangedObjectCollision.ForReplaced()], groups.Select(group => group.Settings));
        Assert.Empty(run.CollisionGroupsOf([]));
        Assert.Equal(run.ChangedObjectCollision.ForMoved(), Assert.Single(run.CollisionGroupsOf([TargetOf(4, TargetOrigin.Moved)])).Settings);
    }

    private static TargetObject TargetOf(int index, TargetOrigin origin) => TestTargets.Create(index, TestTargets.At(default)) with { Origin = origin };
}
