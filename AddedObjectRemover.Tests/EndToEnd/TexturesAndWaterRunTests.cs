using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// Whole runs over <see cref="TexturesAndWaterWorld"/>: the ground textures and the water level the changed-terrain step reads around
/// the cleaned mod's crate that stays are kept when their earlier values are not accepted, so a second run finds nothing to clean up,
/// and go back when the base game holds them, since a second run accepts those.
/// </summary>
public sealed class TexturesAndWaterRunTests
{
    private const int SouthWest = 0;
    private const int SouthEast = 1;

    [Fact]
    public void TexturesAndWaterOfAnUntrustedEarlierPluginStayWhereTheStepReadsThemAroundACrateThatStaysAndASecondRunCleansNothing()
    {
        var outcome = FixtureRun.Execute(Settings(), workers: 1, World(earlierFromTheBaseGame: false));

        var land = Assert.Single(outcome.RestoreWhereRemoved.Lands);
        Assert.Equal((true, false), (land.Textures.Contains(SouthWest), land.Textures.Contains(SouthEast)));
        Assert.True(Assert.Single(land.ChangedQuarters, quarter => quarter.Quarter == SouthEast).TexturesKeptForObjectThatStays);
        var cell = Assert.Single(outcome.RestoreWhereRemoved.Cells);
        var standing = Assert.Single(outcome.Collected.Targets, target => target.EditorId == TexturesAndWaterWorld.StandingCrate);
        Assert.Equal(((RecordKey?)standing.Key, WaterKeptReason.AroundObjectThatStays), (cell.WaterKeptBy, cell.WaterKeptFor));
        Assert.True(RestoredWorldStaysClean.Check(Settings(), World(earlierFromTheBaseGame: false), SettingDefaults.HeightChangeTolerance));
    }

    [Fact]
    public void TexturesAndWaterOfTheBaseGameGoBackAndASecondRunCleansNothing()
    {
        var outcome = FixtureRun.Execute(Settings(), workers: 1, World(earlierFromTheBaseGame: true));

        var land = Assert.Single(outcome.RestoreWhereRemoved.Lands);
        Assert.Equal((true, true), (land.Textures.Contains(SouthWest), land.Textures.Contains(SouthEast)));
        var cell = Assert.Single(outcome.RestoreWhereRemoved.Cells);
        Assert.Equal(((RecordKey?)null, true), (cell.WaterKeptBy, cell.IsRestored));
        Assert.True(RestoredWorldStaysClean.Check(Settings(), World(earlierFromTheBaseGame: true), SettingDefaults.HeightChangeTolerance));
    }

    private static Func<string, IReadOnlyList<ISkyrimMod>> World(bool earlierFromTheBaseGame) =>
        dataFolder => TexturesAndWaterWorld.Create(dataFolder, earlierFromTheBaseGame);

    /// <summary>
    /// The crates beside pads are removed; the changed-terrain step counts ground type and water changes within
    /// <see cref="TexturesAndWaterWorld.CheckDistance"/> of an object; a plugin loading before the cleaned mod is not trusted.
    /// </summary>
    private static Settings Settings()
    {
        var settings = RestoreWhereRemovedRunTests.SettingsOf(restore => restore.ClearedArea.SearchRadius = TexturesAndWaterWorld.SearchRadius);
        settings.Standard.Trust = new TrustSettings { TrustModsLoadingBeforeCleanedMod = false, TrustModsRequiringCleanedModAndTheirMasters = false };
        var terrain = settings.Standard.TerrainChange;
        terrain.RemoveOnChangedTerrain = true;
        terrain.CountTerrainTypeChanges = true;
        terrain.MinTerrainTypeChangePercent = 10;
        terrain.CountWaterHeightChanges = true;
        terrain.CountWaterAddedOrRemoved = true;
        terrain.MinWaterCoverChangePercent = 10;
        terrain.BySize.Use = false;
        terrain.ByDistance.Use = true;
        terrain.ByDistance.DistanceInUnits = TexturesAndWaterWorld.CheckDistance;
        return settings;
    }
}
