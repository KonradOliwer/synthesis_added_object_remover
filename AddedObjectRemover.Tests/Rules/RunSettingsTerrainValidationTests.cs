namespace AddedObjectRemover.Tests.Rules;

using static RunSettingsValidationScene;

public class RunSettingsTerrainValidationTests
{
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

        var neverRemove = BuildReady(settings).Settings.NeverRemovedInvisibleKinds;

        Assert.Equal(new[] { InvisibleObjectKind.Lights, InvisibleObjectKind.SoundMarkers }, neverRemove.Kinds.Order());
    }

    [Fact]
    public void TickedAlwaysKeepKindsStandWhileTheLeftBehindStepsAreSwitchedOff()
    {
        var settings = SettingsFor();
        settings.LeftoverObjects = new LeftoverObjectSettings
        {
            RemoveBySurroundingDirections = false,
            AlwaysKeep = new KeptVisibleObjectSettings { Containers = true },
        };
        settings.LeftoverInvisibleObjects.RemoveLeftoverInvisibleObjects = false;
        settings.LeftoverInvisibleObjects.AlwaysKeep = new KeptInvisibleObjectSettings { Lights = true };

        var ready = BuildReady(settings).Settings;

        Assert.Null(ready.VisibleLeftBehind);
        Assert.Null(ready.InvisibleLeftBehind);
        Assert.Equal([VisibleObjectKind.Containers], ready.NeverRemovedVisibleKinds.Kinds);
        Assert.Equal([InvisibleObjectKind.Lights], ready.NeverRemovedInvisibleKinds.Kinds);
    }

    [Fact]
    public void NothingIsKeptUntilAKindIsTicked()
    {
        Assert.Empty(BuildReady(SettingsFor()).Settings.NeverRemovedInvisibleKinds.Kinds);
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
        Assert.Null(runSettings.InvisibleLeftBehind);
    }
}
