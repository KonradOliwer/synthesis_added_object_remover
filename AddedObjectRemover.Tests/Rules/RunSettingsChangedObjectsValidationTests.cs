using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Rules;

using static RunSettingsValidationScene;

public class RunSettingsChangedObjectsValidationTests
{
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
