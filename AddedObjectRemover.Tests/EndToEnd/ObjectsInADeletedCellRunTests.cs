using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// Objects that stand in an exterior cell a later plugin deletes, through whole runs on a small world of their own: the base game,
/// the cleaned mod, a trusted mod and the city mod, which deletes the east cell. The patch writes nothing into a deleted cell, so
/// what stands there is left as it is although the rest of the run treats it as standing.
/// </summary>
public class ObjectsInADeletedCellRunTests
{
    private const int Workers = 4;
    private const string BaseGamePlugin = "Skyrim.esm";
    private const string TrustedPlugin = "Trusted.esp";
    private const string CityPlugin = "CityMod.esp";
    private const string HomeCellEditorId = "AorHomeCell";
    private const string EastCellEditorId = "AorEastCell";
    private const int HomeGridX = 5;
    private const int EastGridX = HomeGridX + 1;
    private const float MarkerHeight = 10f;

    /// <summary>A point on the border between the home cell and the east cell.</summary>
    private static readonly Vector3 AtTheBorder = new(EastGridX * ExteriorGrid.CellSize, 3000, 0);

    private static readonly Vector3 InTheEastCell = new(EastGridX * ExteriorGrid.CellSize + 1000, 1000, 0);

    /// <summary>
    /// The cleaned mod's crate in the live home cell is removed for a city wall 5 units across the border, in the deleted east cell; a
    /// lantern of the trusted mod in the deleted cell rests on that crate. The lantern would lose its support, but it stands in a
    /// deleted cell, so with either choice it is left as it is, the log and the report file name the cell and the plugin, and the
    /// patch holds only the crate's disable.
    /// </summary>
    [Theory]
    [InlineData(OtherModsObjectsHandling.RemoveFloatingObject)]
    [InlineData(OtherModsObjectsHandling.PullObjectToTheGround)]
    public void AnotherModsObjectInADeletedCellThatLosesItsSupportIsLeftAsItIs(OtherModsObjectsHandling handling)
    {
        PlacedObject? crate = null;
        var (outcome, run) = FixtureRun.Capture(
            CreateSettings(handling),
            Workers,
            dataFolder => CreateWorld(dataFolder, scene => crate = scene.PlaceCrateRestingAcrossTheBorder()));

        var left = Assert.Single(outcome.OtherModsObjects.Objects);
        Assert.Equal(OtherModsObjectFate.LeftInDeletedCell, left.Fate);
        Assert.Equal((EastCellEditorId, new PluginName(CityPlugin)), (left.Object.CellDeleted?.Cell.EditorId, left.Object.CellDeleted?.DeletedBy));
        Assert.Contains(
            run.Log,
            line => line.Contains("left as it is, in a deleted cell (" + EastCellEditorId + " [", StringComparison.Ordinal)
                && line.Contains($"deleted by {CityPlugin})", StringComparison.Ordinal));
        var row = Assert.Single(Assert.Single(run.Reports, report => report.Name == "other-mods-objects-left-without-support.csv").Lines.Skip(1));
        Assert.Contains($"its cell {EastCellEditorId} [", row, StringComparison.Ordinal);
        Assert.Contains($"is deleted by {CityPlugin}", row, StringComparison.Ordinal);
        Assert.StartsWith($"{crate!.FormKey} in ", Assert.Single(run.Patch), StringComparison.Ordinal);
    }

    /// <summary>
    /// A position marker of the cleaned mod stands inside a city wall in the deleted east cell, and its kind is ticked as never removed
    /// as left behind. It is not moved, nothing is written, and the log names the cell and the plugin.
    /// </summary>
    [Fact]
    public void AKeptMarkerInsideAnObjectInADeletedCellIsNotMovedAndTheLogNamesTheCellAndThePlugin()
    {
        var (outcome, run) = FixtureRun.Capture(
            CreateSettings(OtherModsObjectsHandling.PullObjectToTheGround),
            Workers,
            dataFolder => CreateWorld(dataFolder, scene => scene.PlaceMarkerInsideACityWall()));

        Assert.Empty(outcome.MarkerMoves.Moves);
        Assert.Empty(outcome.MarkerMoves.LeftInPlace);
        Assert.Single(outcome.MarkerMoves.LeftInDeletedCell);
        Assert.Contains(
            run.Log,
            line => line.StartsWith("  Left kept T_Marker ", StringComparison.Ordinal)
                && line.Contains($"its cell {EastCellEditorId} [", StringComparison.Ordinal)
                && line.EndsWith($"is deleted by {CityPlugin}.", StringComparison.Ordinal));
        Assert.Empty(run.Patch);
    }

    private static Settings CreateSettings(OtherModsObjectsHandling handling)
    {
        var settings = new Settings
        {
            DetailedLogAndReports = true,
            Standard = new StandardSettings
            {
                CleanedMod = ModKey.FromNameAndExtension(FixtureWorld.TargetName),
                TerrainChange = new TerrainChangeSettings { RemoveOnChangedTerrain = false },
                TooClose = new TooCloseRemovalSettings
                {
                    BySize = new TooCloseBySizeSettings { Use = true, SizeMultiplier = 0.1f },
                    ByDistance = new TooCloseByDistanceSettings { Use = true, DistanceInUnits = 32f },
                },
                Trust = new TrustSettings
                {
                    TrustedModsAndTheirMasters = [ModKey.FromNameAndExtension(TrustedPlugin)],
                    TrustModsRequiringCleanedModAndTheirMasters = false,
                    TrustModsLoadingBeforeCleanedMod = false,
                },
                FollowUpRemoval = new FollowUpRemovalSettings { Mode = FollowUpRemovalMode.Nothing, OtherModsObjectsLeftWithoutSupport = handling },
            },
        };
        var keep = settings.Standard.LeftoverInvisibleObjects.AlwaysKeep;
        (keep.XMarkers, keep.IdleMarkers, keep.OtherMarkers) = (true, true, true);
        return settings.WithTheCollisionOfTheCleanedModsObjects();
    }

    /// <summary>Load order: Skyrim.esm, Target.esp, Trusted.esp, CityMod.esp, which deletes the east cell.</summary>
    private static IReadOnlyList<ISkyrimMod> CreateWorld(string dataFolder, Action<Scene> fill)
    {
        FixtureMeshes.WriteAll(dataFolder);
        var baseGame = new FixtureMod(BaseGamePlugin);
        var target = new FixtureMod(FixtureWorld.TargetName, baseGame);
        var trusted = new FixtureMod(TrustedPlugin, baseGame);
        var city = new FixtureMod(CityPlugin, baseGame);
        var spaces = FixtureSpaces.CreateIn(baseGame);
        foreach (var mod in new[] { target, trusted, city }) spaces.AddOverridesTo(mod);
        var (homeKey, eastKey) = (baseGame.NextKey(), baseGame.NextKey());
        var cells = new[] { baseGame, target, trusted, city }
            .ToDictionary(mod => mod, mod => (Home: mod.AddExteriorCell(homeKey, HomeCellEditorId, HomeGridX), East: mod.AddExteriorCell(eastKey, EastCellEditorId, EastGridX)));

        fill(new Scene(target, trusted, city, cells[target], cells[trusted].East, cells[city].East));
        cells[city].East.Record.IsDeleted = true;
        return [baseGame.Mod, target.Mod, trusted.Mod, city.Mod];
    }

    private sealed record Scene(
        FixtureMod Target,
        FixtureMod Trusted,
        FixtureMod City,
        (FixtureCell Home, FixtureCell East) TargetCells,
        FixtureCell TrustedEast,
        FixtureCell CityEast)
    {
        /// <returns>The crate.</returns>
        public PlacedObject PlaceCrateRestingAcrossTheBorder()
        {
            var crate = TargetCells.Home.Place("T_BorderCrate", Target.AddModeledStatic("T_BorderCrateBase", FixtureMeshes.CrateModel), AtTheBorder - new Vector3(38, 0, 0));
            CityEast.Place("C_BorderWall", City.AddModeledStatic("C_BorderWallBase", FixtureMeshes.PadModel), AtTheBorder + new Vector3(37, 0, 0));
            TrustedEast.Place("Trusted_Lantern", Trusted.AddModeledStatic("Trusted_LanternBase", FixtureMeshes.PadModel), AtTheBorder + new Vector3(1, 0, FixtureMeshes.CrateSize));
            return crate;
        }

        public void PlaceMarkerInsideACityWall()
        {
            CityEast.Place("C_Wall", City.AddModeledStatic("C_WallBase", FixtureMeshes.BuildingModel), InTheEastCell);
            TargetCells.East.Place("T_Marker", Target.AddMarkerStatic("T_MarkerBase"), InTheEastCell + new Vector3(0, 0, MarkerHeight));
        }
    }
}
