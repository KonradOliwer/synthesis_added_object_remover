using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>Every restore scene of <see cref="RestoreWhereRemovedRunTests"/>, run a second time on the world its first run's patch leaves (<see cref="RestoredWorldStaysClean"/>).</summary>
public sealed class RestoredWorldStaysCleanTests
{
    private static readonly Dictionary<string, (Func<Settings> Settings, Func<string, IReadOnlyList<ISkyrimMod>> World)> Scenes = CreateScenes();

    public static TheoryData<string> SceneNames() => [.. Scenes.Keys];

    [Theory]
    [MemberData(nameof(SceneNames))]
    public void ASecondRunOnTheRestoredWorldFindsNothingToCleanUp(string scene)
    {
        var (settings, world) = Scenes[scene];

        var judged = RestoredWorldStaysClean.Check(settings(), world, SettingDefaults.HeightChangeTolerance);

        Assert.Equal(ScenesWhereTheSecondRunJudgesTheWorld.Contains(scene), judged);
    }

    [Theory]
    [InlineData(-20f, 15f, true)]
    [InlineData(20f, -15f, true)]
    [InlineData(5f, 40f, true)]
    [InlineData(64f, 0f, false)]
    [InlineData(-20f, -5f, false)]
    [InlineData(0f, 0f, false)]
    public void TheCheckFlagsGroundThatEndsFartherFromTheObjectOrOnItsOtherSide(float heightBefore, float heightNow, bool flagged) =>
        Assert.Equal(flagged, RestoredWorldStaysClean.LeftTheObject(heightBefore, heightNow, heightTolerance: 1f));

    [Fact]
    public void TheCheckAcceptsGroundThatEndsOnTheOtherSideWithinTheTolerance() =>
        Assert.False(RestoredWorldStaysClean.LeftTheObject(heightBefore: -20f, heightNow: 15f, SettingDefaults.HeightChangeTolerance));

    /// <summary>The scenes whose first run leaves objects of the cleaned mod standing, so the second run checks (a) and (b); in the others it stops and only (c) checks them.</summary>
    private static readonly HashSet<string> ScenesWhereTheSecondRunJudgesTheWorld =
    [
        "Village",
        "Terrain",
        "SiteWithMists",
        "RowWithALoneCrate",
        "RowWithALoneCrateCarryingAPad",
        "TerrainWithTheChangedTerrainStep",
        "CampfireRing",
        "CampfireRingWithALandmark",
        "ClearedCampWithAKeptCampfire",
    ];

    private static Dictionary<string, (Func<Settings> Settings, Func<string, IReadOnlyList<ISkyrimMod>> World)> CreateScenes()
    {
        Func<Settings> restoreOn = () => RestoreWhereRemovedRunTests.SettingsOf();
        var scenes = new Dictionary<string, (Func<Settings> Settings, Func<string, IReadOnlyList<ISkyrimMod>> World)>
        {
            ["Village"] = (restoreOn, RestoreWhereRemovedWorlds.CreateVillage),
            ["VillageWithAPatchOfTheCleanedMod"] = (restoreOn, RestoreWhereRemovedWorlds.CreateVillageWithAPatchOfTheCleanedMod),
            ["VillageWithATableOfAnotherModWhereARockStood"] = (restoreOn, RestoreWhereRemovedWorlds.CreateVillageWithATableOfAnotherModWhereARockStood),
            ["Terrain"] = (RestoreWhereRemovedRunTests.TerrainSettings, RestoreWhereRemovedWorlds.CreateTerrain),
            ["TerrainWithTheChangedTerrainStep"] = (WithTheChangedTerrainStep, RestoreWhereRemovedWorlds.CreateTerrain),
            ["CampfireRing"] = (RestoreWhereRemovedRunTests.TerrainSettings, RestoreWhereRemovedWorlds.CreateCampfireRing),
            ["CampfireRingWithALandmark"] = (RestoreWhereRemovedRunTests.TerrainSettings, RestoreWhereRemovedWorlds.CreateCampfireRingWithALandmark),
            ["ClearedCampWithAKeptCampfire"] = (WithTheDirectionMethod, dataFolder => RestoreWhereRemovedWorlds.CreateClearedCampWithAKeptCampfire(dataFolder, persistentRock: false)),
            ["SiteWithMists"] = (restoreOn, RestoreWhereRemovedWorlds.CreateSiteWithMists),
            ["RowWithALoneCrate"] = (restoreOn, RestoreWhereRemovedWorlds.CreateRowWithALoneCrate),
            ["RowWithALoneCrateCarryingAPad"] = (restoreOn, dataFolder => RestoreWhereRemovedWorlds.CreateRowWithALoneCrateCarryingAPad(dataFolder).LoadOrder),
        };
        foreach (var scenario in Enum.GetValues<NavmeshAndLandScenario>()) scenes.Add($"NavmeshAndLand.{scenario}", (restoreOn, dataFolder => NavmeshAndLandWorld.Create(dataFolder, scenario)));
        return scenes;
    }

    /// <summary>The terrain scene's settings with the left-behind direction method on and the area method off.</summary>
    internal static Settings WithTheDirectionMethod()
    {
        var settings = RestoreWhereRemovedRunTests.TerrainSettings();
        settings.Standard.LeftoverObjects = new LeftoverObjectSettings { RemoveBySurroundingDirections = true, RemoveObjectsInClearedAreas = false };
        return settings;
    }

    /// <summary>The terrain scene's settings with the changed-terrain step on, counting height, ground type and water changes, so (f) has hits to find.</summary>
    private static Settings WithTheChangedTerrainStep()
    {
        var settings = RestoreWhereRemovedRunTests.TerrainSettings();
        var terrain = settings.Standard.TerrainChange;
        terrain.RemoveOnChangedTerrain = true;
        terrain.CountTerrainTypeChanges = true;
        terrain.CountWaterHeightChanges = true;
        return settings;
    }
}
