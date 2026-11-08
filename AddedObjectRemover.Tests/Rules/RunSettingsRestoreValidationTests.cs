namespace AddedObjectRemover.Tests.Rules;

using static RunSettingsValidationScene;

public class RunSettingsRestoreValidationTests
{
    private const string AreaMethodGroup = "Objects left behind > Area method: when the area around it counts as cleared > ";

    [Fact]
    public void TheAreaMethodIsOnByDefaultWithTheRestoresDefaultArea()
    {
        var ready = BuildReady(SettingsFor());

        Assert.Equal(
            new ObjectsInClearedAreasSettings(new ClearedArea(5000f, MaxStandingSizePercent: 1, MaxStandingCountPercent: 5), MinObjectsAround: 5), ready.Settings.ObjectsInClearedAreas);
        Assert.Null(ready.Settings.ObjectsInClearedAreasOff);
        Assert.Empty(ready.Warnings);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(101, 100)]
    [InlineData(1, 1)]
    [InlineData(100, 100)]
    public void TheMinimumOfObjectsAroundIsLimitedTo1To100AndSharedByBothMethods(int configured, int expected)
    {
        var settings = SettingsFor();
        settings.LeftoverObjects.MinObjectsAround = configured;

        var ready = BuildReady(settings);

        Assert.Equal((expected, expected), (ready.Settings.VisibleLeftBehind!.MinObjectsAround, ready.Settings.ObjectsInClearedAreas!.MinObjectsAround));
        var warnings = ready.Warnings.Select(warning => warning.Message).ToList();
        Assert.Equal(
            configured == expected ? [] : [$"Warning: Objects left behind > Fewest of the cleaned mod's other objects around it, for both methods {configured} is outside 1-100; using {expected}."],
            warnings);
    }

    [Fact]
    public void AnInvalidMinimumOfObjectsAroundIsReportedAlsoWithBothMethodsOff()
    {
        var settings = SettingsFor();
        settings.LeftoverObjects = new LeftoverObjectSettings { MinObjectsAround = 0, RemoveBySurroundingDirections = false, RemoveObjectsInClearedAreas = false };

        var ready = BuildReady(settings);

        Assert.Single(ready.Warnings, warning => warning.Message.Contains("Fewest of the cleaned mod's other objects around it", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(100f, 5f, ObjectsInClearedAreasOff.SizeLimit)]
    [InlineData(150f, 5f, ObjectsInClearedAreasOff.SizeLimit)]
    [InlineData(1f, 100f, ObjectsInClearedAreasOff.CountLimit)]
    [InlineData(1f, 150f, ObjectsInClearedAreasOff.CountLimit)]
    [InlineData(100f, 100f, ObjectsInClearedAreasOff.BothLimits)]
    public void ALimitOf100PercentOrMoreSwitchesTheAreaMethodOffWithoutAWarning(float sizeLimit, float countLimit, ObjectsInClearedAreasOff expected)
    {
        var settings = SettingsFor();
        settings.LeftoverObjects.ClearedArea = new LeftoverClearedAreaSettings { MaxStandingSizePercent = sizeLimit, MaxStandingCountPercent = countLimit };

        var ready = BuildReady(settings);

        Assert.Null(ready.Settings.ObjectsInClearedAreas);
        Assert.Equal(expected, ready.Settings.ObjectsInClearedAreasOff);
        Assert.Empty(ready.Warnings);
    }

    [Fact]
    public void TheAreaMethodSwitchOffSwitchesItOffEvenWithALimitOf100Percent()
    {
        var settings = SettingsFor();
        settings.LeftoverObjects.RemoveObjectsInClearedAreas = false;
        settings.LeftoverObjects.ClearedArea.MaxStandingCountPercent = 100f;

        var ready = BuildReady(settings);

        Assert.Equal((null, ObjectsInClearedAreasOff.Switch), (ready.Settings.ObjectsInClearedAreas, ready.Settings.ObjectsInClearedAreasOff));
    }

    [Fact]
    public void ANegativeAreaMethodLimitBecomes0WithAWarningAndAnOutOfRangeRadiusIsCorrected()
    {
        var settings = SettingsFor();
        settings.LeftoverObjects.ClearedArea = new LeftoverClearedAreaSettings { SearchRadius = 20000f, MaxStandingSizePercent = -1f, MaxStandingCountPercent = 2.5f };

        var ready = BuildReady(settings);

        Assert.Equal(new ClearedArea(16384f, MaxStandingSizePercent: 0, MaxStandingCountPercent: 2.5), ready.Settings.ObjectsInClearedAreas!.ClearedArea);
        Assert.Equal(
            [
                $"Warning: {AreaMethodGroup}Look this far around it 20000 is outside 1024-16384; using 16384.",
                $"Warning: {AreaMethodGroup}Cleared when at most this share of their size still stands -1 is below 0; using 0.",
            ],
            ready.Warnings.Select(warning => warning.Message));
    }

    private const string RestoreGroup = "Restore what the cleaned mod changed where it was removed > ";
    private const string ClearedAreaGroup = RestoreGroup + "When an area counts as cleared > ";

    [Fact]
    public void TheRestoreGroupIsOnByDefaultWithItsDefaultsAndTheReusedCollisionSettings()
    {
        var ready = BuildReady(SettingsFor());

        var restore = ready.Settings.RestoreWhereRemoved!;
        Assert.Equal((true, true, true, true), (restore.OtherModsObjects, restore.Terrain, restore.Cells, restore.Navmeshes));
        Assert.Equal(new ClearedArea(5000f, MaxStandingSizePercent: 1, MaxStandingCountPercent: 5), restore.ClearedArea);
        Assert.Equal(256f, restore.TerrainEdgeBlendDistance);
        Assert.Equal(
            (ready.Settings.Collision.ChangedTerrain.HeightTolerance, ready.Settings.Collision.ChangedTerrain.WaterHeightTolerance),
            (restore.HeightChangeTolerance, restore.WaterHeightChangeTolerance));
        Assert.Equal(ready.Settings.ChangedObjectCollision.TooClose, restore.TooClose);
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
    public void EachMaxStandingPercentIsLimitedTo0To100(float configured, double expected)
    {
        var settings = SettingsFor();
        settings.RestoreWhereRemoved.ClearedArea.MaxStandingSizePercent = configured;
        settings.RestoreWhereRemoved.ClearedArea.MaxStandingCountPercent = configured;

        var ready = BuildReady(settings);

        var area = ready.Settings.RestoreWhereRemoved!.ClearedArea;
        string[] warnings = configured == expected
            ? []
            : [
                $"Warning: {ClearedAreaGroup}Cleared when at most this share of their size still stands {configured} is outside 0-100; using {expected}.",
                $"Warning: {ClearedAreaGroup}Cleared when at most this share of their number still stands {configured} is outside 0-100; using {expected}.",
            ];
        Assert.Equal((expected, expected), (area.MaxStandingSizePercent, area.MaxStandingCountPercent));
        Assert.Equal(warnings, ready.Warnings.Select(warning => warning.Message));
    }

    [Theory]
    [InlineData(0.1f, 0.1)]
    [InlineData(0.7f, 0.7)]
    [InlineData(12.5f, 12.5)]
    public void ADecimalMaxStandingPercentIsKeptAsTyped(float configured, double expected)
    {
        var settings = SettingsFor();
        settings.RestoreWhereRemoved.ClearedArea.MaxStandingSizePercent = configured;
        settings.RestoreWhereRemoved.ClearedArea.MaxStandingCountPercent = configured;

        var ready = BuildReady(settings);

        var area = ready.Settings.RestoreWhereRemoved!.ClearedArea;
        Assert.Equal((expected, expected), (area.MaxStandingSizePercent, area.MaxStandingCountPercent));
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
        settings.RestoreWhereRemoved.ClearedArea.MaxStandingSizePercent = float.NaN;
        settings.RestoreWhereRemoved.ClearedArea.MaxStandingCountPercent = float.NaN;
        settings.RestoreWhereRemoved.TerrainEdgeBlendDistanceInUnits = float.NaN;

        var ready = BuildReady(settings);

        var restore = ready.Settings.RestoreWhereRemoved!;
        Assert.Equal((new ClearedArea(5000f, 1, 5), 256f), (restore.ClearedArea, restore.TerrainEdgeBlendDistance));
        Assert.Equal(
            [
                $"Warning: {ClearedAreaGroup}Look this far around the place is not a valid number; using 5000.",
                $"Warning: {ClearedAreaGroup}Cleared when at most this share of their size still stands is not a valid number; using 1.",
                $"Warning: {ClearedAreaGroup}Cleared when at most this share of their number still stands is not a valid number; using 5.",
                $"Warning: {RestoreGroup}Blend terrain edges over is not a valid number; using 256.",
            ],
            ready.Warnings.Select(warning => warning.Message));
    }

    [Fact]
    public void InvalidRestoreSettingsAreReportedInOrderEvenWhenAllSwitchesAreOff()
    {
        var settings = SettingsFor();
        settings.RestoreWhereRemoved = AllRestoresOff();
        settings.RestoreWhereRemoved.ClearedArea = new ClearedAreaSettings { SearchRadius = 1f, MaxStandingSizePercent = 101f, MaxStandingCountPercent = -1f };
        settings.RestoreWhereRemoved.TerrainEdgeBlendDistanceInUnits = 2000f;

        var ready = BuildReady(settings);

        Assert.False(ready.Settings.RestoreWhereRemoved.Runs);
        Assert.Equal(
            [
                $"Warning: {ClearedAreaGroup}Look this far around the place 1 is outside 1024-16384; using 1024.",
                $"Warning: {ClearedAreaGroup}Cleared when at most this share of their size still stands 101 is outside 0-100; using 100.",
                $"Warning: {ClearedAreaGroup}Cleared when at most this share of their number still stands -1 is outside 0-100; using 0.",
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
}
