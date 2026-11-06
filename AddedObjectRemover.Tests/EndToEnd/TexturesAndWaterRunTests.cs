using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// Whole runs over <see cref="TexturesAndWaterWorld"/>: the ground textures and the water level around the cleaned mod's crate that
/// stays go back, whether their earlier values are the base game's or an untrusted plugin's, since the next patcher of the group reads
/// them as given back; a second run handed the restored parts finds nothing to clean up.
/// </summary>
public sealed class TexturesAndWaterRunTests
{
    private const int SouthWest = 0;
    private const int SouthEast = 1;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TexturesAndWaterGoBackAroundACrateThatStaysAndASecondRunCleansNothing(bool earlierFromTheBaseGame)
    {
        var outcome = FixtureRun.Execute(Settings(), workers: 1, World(earlierFromTheBaseGame));

        var land = Assert.Single(outcome.RestoreWhereRemoved.Lands);
        Assert.Equal((true, true), (land.Textures.Contains(SouthWest), land.Textures.Contains(SouthEast)));
        var cell = Assert.Single(outcome.RestoreWhereRemoved.Cells);
        Assert.Equal(((RecordKey?)null, true), (cell.WaterKeptBy, cell.IsRestored));
        Assert.True(RestoredWorldStaysClean.Check(Settings(), World(earlierFromTheBaseGame), SettingDefaults.HeightChangeTolerance));
    }

    private static Func<string, IReadOnlyList<ISkyrimMod>> World(bool earlierFromTheBaseGame) =>
        dataFolder => TexturesAndWaterWorld.Create(dataFolder, earlierFromTheBaseGame);

    /// <summary>
    /// The crates beside pads are removed; the changed-terrain step counts ground type and water changes within
    /// <see cref="TexturesAndWaterWorld.CheckDistance"/> of an object; a plugin loading before the cleaned mod is not trusted.
    /// </summary>
    internal static Settings Settings()
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
