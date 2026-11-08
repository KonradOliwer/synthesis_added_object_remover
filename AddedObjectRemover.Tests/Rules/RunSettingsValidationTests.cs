namespace AddedObjectRemover.Tests.Rules;

using static RunSettingsValidationScene;

public class RunSettingsValidationTests
{
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

        Assert.Equal(expected, ready.Settings.InvisibleLeftBehind!.Surroundings.MinOccupiedDirections);
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
        settings.LeftoverObjects = new LeftoverObjectSettings
        {
            RemoveBySurroundingDirections = true,
            SurroundingObjects = new VisibleSurroundingObjectsSettings { MinOccupiedDirections = configured },
        };

        var ready = BuildReady(settings);

        Assert.Equal(expected, ready.Settings.VisibleLeftBehind!.Surroundings.MinOccupiedDirections);
        Assert.Equal(
            configured != expected,
            ready.Warnings.Any(warning => warning.Message.StartsWith("Warning: Objects left behind > ", StringComparison.Ordinal) && warning.Message.Contains("directions have objects")));
    }

    [Fact]
    public void TheVisibleStepIsOnByDefaultWithItsOwnDefaultsAndNullWhenSwitchedOff()
    {
        var settings = SettingsFor();
        settings.LeftoverObjects = new LeftoverObjectSettings { RemoveBySurroundingDirections = false };

        var off = BuildReady(settings);
        var on = BuildReady(SettingsFor());

        Assert.Null(off.Settings.VisibleLeftBehind);
        var visible = on.Settings.VisibleLeftBehind!;
        Assert.Equal(new LeftBehindSurroundings(1024f, 6, 80, 80), visible.Surroundings);
        Assert.Equal(5, visible.MinObjectsAround);
        Assert.Empty(on.Settings.NeverRemovedVisibleKinds.Kinds);
        Assert.Empty(on.Warnings);
    }

    [Fact]
    public void InvalidVisibleSettingsAreReportedAndCorrectedEvenWhenTheStepIsOff()
    {
        var settings = SettingsFor();
        settings.LeftoverObjects = new LeftoverObjectSettings
        {
            RemoveBySurroundingDirections = false,
            SurroundingObjects = new VisibleSurroundingObjectsSettings { SearchRadius = 1f, DirectionThresholdPercent = 45, ClearedDirectionsPercent = 44 },
        };

        var messages = BuildReady(settings).Warnings.Select(warning => warning.Message).ToList();

        const string group = "Warning: Objects left behind > Direction method: when the cleaned mod's visible objects around it were removed > ";
        Assert.Equal(3, messages.Count);
        Assert.StartsWith(group + "Look this far around it", messages[0]);
        Assert.StartsWith(group + "A direction is cleared when this much of it was removed 45 is not a multiple", messages[1]);
        Assert.StartsWith(group + "Remove when this share of the directions with objects is cleared 44 is not a multiple", messages[2]);
    }

    [Fact]
    public void TheTickedVisibleKindsAreTheOnesNeverRemoved()
    {
        var settings = SettingsFor();
        settings.LeftoverObjects = new LeftoverObjectSettings
        {
            RemoveBySurroundingDirections = true,
            AlwaysKeep = new KeptVisibleObjectSettings { Containers = true, Furniture = true, Activators = true },
        };

        var ready = BuildReady(settings);

        Assert.Equal(
            [VisibleObjectKind.Containers, VisibleObjectKind.Furniture, VisibleObjectKind.Activators],
            ready.Settings.NeverRemovedVisibleKinds.Kinds.Order());
    }

    [Fact]
    public void SupportThresholdBecomesAFraction()
    {
        var settings = SettingsFor();
        settings.FollowUpRemoval = new FollowUpRemovalSettings { AnchoringThresholdPercent = 25f };
        Assert.Equal(0.25f, BuildReady(settings).Settings.AlsoRemove.SupportLostFraction);
    }

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
}
