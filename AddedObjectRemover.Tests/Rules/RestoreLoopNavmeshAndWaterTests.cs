using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Rules;

using static RestoreLoopScenes;

/// <summary>
/// The navmeshes and the water levels in the guards between the kinds that go back where the cleaned mod was removed, repeated until
/// nothing more changes, over in-memory scenes: navmeshes lying on kept ground, rocks going back into navmeshes, and objects at the
/// water line.
/// </summary>
public sealed class RestoreLoopNavmeshAndWaterTests
{
    /// <summary>A base game navmesh the cleaned mod did not change lies on the raised ground, or far above it.</summary>
    [Theory]
    [InlineData(Raised, true)]
    [InlineData(Raised + 500f, false)]
    public void TheGroundUnderANavmeshLyingOnItAsItIsIsKept(float navmeshHeight, bool kept)
    {
        var square = Square(BaseGame, new Vector3(1500, 1500, navmeshHeight));
        var scene = new Scene { Winning = [.. Enumerable.Range(0, square.Triangles.Length).Select(triangle => new NavmeshWorldTriangle(NavmeshKey, square.CornersOf(triangle)!.Value))] };

        var land = Assert.Single(Plan(scene).Lands);

        Assert.Equal(kept ? Raised : 0f, land.FinalHeights![PointAt(12, 12)]);
        Assert.Equal(kept, land.Kept.Any(points => points.Reason == TerrainKeptReason.UnderNavmesh && points.Points.Contains(PointAt(12, 12))));
    }

    /// <summary>The navmesh's earlier place lies under the house, whose ground the first pass keeps; the next pass takes the navmesh back.</summary>
    [Fact]
    public void ANavmeshGroupIsTakenBackOnceTheGroundUnderItsEarlierPlaceIsKept()
    {
        var scene = WithAHouseKeepingItsGround(new Scene { Navmeshes = [MovedNavmesh()] });

        var result = Plan(scene);

        var group = Assert.Single(result.NavmeshGroups);
        Assert.Equal(new NavmeshRefusal(NavmeshGuard.Ground, NavmeshKey, Fault: null), group.Refused);
        Assert.Null(group.Write);
    }

    /// <summary>The rock goes back into the navmesh's place now, which is free while the navmesh goes back and not once it is taken back.</summary>
    [Fact]
    public void ARockGoingBackIntoANavmeshThatIsTakenBackIsTakenBackToo()
    {
        var result = Plan(RockInTheNavmeshTakenBack());

        var rock = Assert.Single(result.Objects);
        Assert.Equal((new PlacedRefusal(PlacedGuard.StandsInChangedNavmesh, NavmeshKey, Failure: null), false), (rock.Refused, rock.IsRestored));
    }

    /// <summary>
    /// A mist of the cleaned mod that stays stands across the line between the water level before the cleaned mod and its lower one.
    /// An object at the water line keeps no level, so the water still goes back and the mist then stands out of it.
    /// </summary>
    [Fact]
    public void AStandingMistOfTheCleanedModAtTheWaterLineKeepsNoWaterLevel()
    {
        var scene = new Scene { Standing = [new Vector3(CellCentre + 300, CellCentre, 0)], StandingModel = Mist.Base, Lands = [], Cells = [ChangedWater()] };

        var cell = Assert.Single(Plan(scene).Cells);

        Assert.Equal(((RecordKey?)null, true), (cell.WaterKeptBy, cell.IsRestored));
    }

    /// <summary>
    /// Two base game rocks the cleaned mod moved go back to old places 30 units apart; the first keeps a turn a later plugin gave it,
    /// so the two overlap once back. The restore does not clean anything between other mods' objects: both go back.
    /// </summary>
    [Fact]
    public void TwoRocksGoingBackThatOverlapOnceBackBothGoBack()
    {
        var first = Rock(0, new Vector3(1500, 1500, RockHalfSize), RockNowPlace);
        var turned = first with
        {
            WhereItIsNow = new EulerPlacement(RockNowPlace, new Vector3(0, 0, 0.5f), 1f),
            Earlier = first.Earlier with { LeftToLaterPlugins = ImmutableSortedDictionary<PlacedParts, PluginName>.Empty.Add(PlacedParts.Rotation, LaterMod.ToPluginName()) },
        };
        var secondNow = RockNowPlace + new Vector3(0, 500, 0);
        var scene = new Scene
        {
            Rocks = [turned, Rock(1, new Vector3(1530, 1500, RockHalfSize), secondNow)],
            Others = [RockNow(0, RockNowPlace) with { Rotation = new Vector3(0, 0, 0.5f) }, RockNow(1, secondNow)],
            Lands = [],
        };

        Assert.All(Plan(scene).Objects, judged => Assert.Equal((true, null), (judged.IsRestored, judged.Refused)));
    }

    /// <summary>
    /// A later mod's crate stands between the water level before the cleaned mod and the cleaned mod's lower one, or far above both:
    /// another mod's object at the water line keeps no level, so the level goes back with the cell's other parts.
    /// </summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(500f)]
    public void AnObjectBetweenTheTwoWaterLevelsKeepsNoLevelAndTheLevelGoesBackWithTheCellsOtherParts(float crateHeight)
    {
        var crate = LaterObject(10, Block.Base, new Vector3(CellCentre + 300, CellCentre, crateHeight));
        var scene = new Scene { Others = [crate], Lands = [], Cells = [ChangedWater()] };

        var cell = Assert.Single(Plan(scene).Cells);

        Assert.Null(cell.WaterKeptBy);
        Assert.Equal([CellPart.Music, CellPart.WaterLevel], cell.Undone.Select(part => part.Name.Part));
        Assert.True(cell.IsRestored);
    }

    /// <summary>
    /// A base game rock the cleaned mod moved is refused next to a later mod's crate at its old place, so it stays where the cleaned mod
    /// put it: across the line between the water level before the cleaned mod and its lower one, or far above both. Across the line it
    /// keeps the cleaned mod's level, although no changed-terrain check counts water; above both, the level goes back.
    /// </summary>
    [Theory]
    [InlineData(0f, true)]
    [InlineData(500f, false)]
    public void ARefusedRockAtTheWaterLineKeepsTheCleanedModsWaterLevel(float rockNowHeight, bool kept)
    {
        var oldPlace = new Vector3(1500, 1500, 500f);
        var rockNow = new Vector3(1500, 2500, rockNowHeight);
        var crate = LaterObject(10, Block.Base, oldPlace + new Vector3(RockHalfSize + TooCloseDistance - 5f, 0, 0));
        var scene = new Scene { Rocks = [Rock(0, oldPlace, rockNow)], Others = [crate, RockNow(0, rockNow)], Lands = [], Cells = [ChangedWater()] };

        var result = Plan(scene);

        var rock = Assert.Single(result.Objects);
        Assert.Equal(PlacedGuard.TooCloseToWhatStays, rock.Refused?.Guard);
        var cell = Assert.Single(result.Cells);
        Assert.Equal(kept ? (rock.Change.Key, WaterKeptReason.ObjectAtWaterLine) : (null, WaterKeptReason.NavmeshAtWaterLine), (cell.WaterKeptBy, cell.WaterKeptFor));
        Assert.True(cell.IsRestored);
    }

    /// <summary>
    /// The rock of <see cref="CopyScene"/> goes back with a copy left where the cleaned mod put it, across the water line or above both
    /// levels, and the cleaned mod's own changed-terrain check is off. Across the line the copy keeps the cleaned mod's level, named by
    /// its original; above both, the level goes back.
    /// </summary>
    [Theory]
    [InlineData(0f, true)]
    [InlineData(Raised + RockHalfSize, false)]
    public void ACopyAtTheWaterLineKeepsTheCleanedModsWaterLevel(float copyHeight, bool kept)
    {
        var scene = CopyScene(cleanedModsTerrainCheck: null, copyPlace: RockNowPlace with { Z = copyHeight }) with { Cells = [ChangedWater()], Lands = [] };

        var result = Plan(scene);

        var judged = Assert.Single(result.Objects);
        Assert.Equal((true, new CloneJudged(RefusedBy: null)), (judged.IsRestored, judged.Cloning));
        var cell = Assert.Single(result.Cells);
        Assert.Equal(
            kept ? (FirstStandingTargetKey(), WaterKeptReason.ObjectAtWaterLine) : (null, WaterKeptReason.NavmeshAtWaterLine), (cell.WaterKeptBy, cell.WaterKeptFor));
    }

    /// <summary>
    /// The rock goes back across the water line. Switched on, the cell's water level goes back with it, so the rock goes back. With the
    /// cell settings switched off the cleaned mod's water level stays as if every cell were kept, and with a navmesh that stays changed
    /// at the water line it stays too: the rock would go back into the cleaned mod's water line, so it is refused, trusted or not.
    /// </summary>
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public void ARockGoingBackAcrossAWaterLineIsRefusedOnlyWhenTheLevelStays(bool cellsOn, bool navmeshAtTheLine, bool baseGameTrusted)
    {
        var scene = new Scene
        {
            Rocks = [Rock(0, new Vector3(1000, 1000, 0), RockNowPlace)],
            Others = [RockNow(0, RockNowPlace)],
            Lands = [],
            Cells = [ChangedWater()],
            Navmeshes = navmeshAtTheLine ? [NavmeshInTheWaterCell(NavmeshNow, laterWinnerAt: null)] : [],
            Trust = baseGameTrusted ? BaseGameTrusted : Trust,
            Settings = AllOn with { Cells = cellsOn, Navmeshes = !navmeshAtTheLine },
        };

        var result = Plan(scene);

        var rock = Assert.Single(result.Objects);
        var levelStays = !cellsOn || navmeshAtTheLine;
        Assert.Equal(levelStays ? (false, PlacedGuard.ArrivesAtWaterLine) : (true, null), (rock.IsRestored, rock.Refused?.Guard));
        if (navmeshAtTheLine) Assert.Equal(NavmeshKey, Assert.Single(result.Cells).WaterKeptBy);
    }

    /// <summary>
    /// The rock goes back across the line between the water level before the cleaned mod and the cleaned mod's, which a later plugin
    /// set again after it. That level is the later plugin's (decision 33) and never goes back, but it refuses nothing: the rock goes
    /// back, cell settings on or off.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARockGoingBackAcrossAWaterLevelALaterPluginSetAgainGoesBack(bool cellsOn)
    {
        var setAgain = ChangedWater() with
        {
            Parts = [new PartChange(new CellPartName(CellPart.Music, null), new PartOutcome.Undo()), new PartChange(new CellPartName(CellPart.WaterLevel, null), new PartOutcome.LeftToLaterPlugin(LaterMod.ToPluginName()))],
            CurrentWater = new CellWaterCopy(LaterMod.ToPluginName(), HasWater: true, CleanedModsWater),
        };
        var scene = new Scene
        {
            Rocks = [Rock(0, new Vector3(1000, 1000, 0), RockNowPlace)],
            Others = [RockNow(0, RockNowPlace)],
            Lands = [],
            Cells = [setAgain],
            Settings = AllOn with { Cells = cellsOn },
        };

        var rock = Assert.Single(Plan(scene).Objects);

        Assert.Equal((true, null), (rock.IsRestored, rock.Refused));
    }

    /// <summary>
    /// The cleaned mod moved the base game navmesh and added one of its own, which the patch sinks when it goes back. A later plugin's
    /// navmesh info map, not its navmesh record, merges the base game navmesh's entry with the added one, so the added one stays.
    /// </summary>
    [Fact]
    public void AnAddedNavmeshThatAChangedNavmeshsWinningMapEntryMergesWithStays()
    {
        var added = MovedNavmesh() with
        {
            Navmesh = new RecordKey(TestTargets.TargetName, 0x703),
            Case = NavmeshCase.Added,
            Geometry = new PartOutcome.Unchanged(),
            Presence = new PartOutcome.Undo(),
            Earlier = null,
        };
        var map = new RecordKey(LaterMod.ToPluginName(), 0x704);
        var moved = MovedNavmesh() with { WinningMapEntry = new NavmeshMapEntryFact(LaterMod.ToPluginName(), map, [added.Navmesh], PreferredMerges: [], LinkedDoors: []) };

        var result = Plan(new Scene { Navmeshes = [moved, added], Lands = [] });

        var group = Assert.Single(result.NavmeshGroups, group => group.Members.Contains(added.Navmesh));
        Assert.Equal(new NavmeshRefusal(NavmeshGuard.MapEntry, NavmeshKey, Fault: null), group.Refused);
    }

    /// <summary>
    /// The cleaned mod stores the worldspace's default water height 0 as the cell's own, with the cells switched off: the navmesh
    /// group of the cell holding the navmesh is not refused for the water level that stays.
    /// </summary>
    [Fact]
    public void ANavmeshGroupInACellThatStoresTheDefaultWaterLevelAsItsOwnGoesBack()
    {
        var scene = new Scene { Navmeshes = [MovedNavmesh()], Lands = [], Cells = [WaterSetToTheDefault(NavmeshCell)], Settings = AllOn with { Cells = false } };

        Assert.NotNull(Assert.Single(Plan(scene).NavmeshGroups).Write);
    }

    /// <summary>With the navmeshes switched off, the cleaned mod's navmesh stays; it lies in the water cell between the two levels.</summary>
    [Fact]
    public void WithTheNavmeshesSwitchedOffAChangedNavmeshAcrossTheWaterLineKeepsTheCleanedModsLevel()
    {
        var scene = new Scene { Navmeshes = [NavmeshInTheWaterCell(NavmeshNow, laterWinnerAt: null)], Lands = [], Cells = [ChangedWater()], Settings = AllOn with { Navmeshes = false } };

        Assert.Equal(NavmeshKey, Assert.Single(Plan(scene).Cells).WaterKeptBy);
    }

    /// <summary>
    /// The cleaned mod's version of the navmesh lies far above both water levels, but a later plugin's version, which wins, lies between
    /// them; the later version keeps the cleaned mod's level.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AWaterLineIsKeptByTheTrianglesOfTheNavmeshsWinningVersion(bool laterWinnerAtTheWaterLine)
    {
        var above = NavmeshNow with { Z = 500 };
        var navmesh = NavmeshInTheWaterCell(above, laterWinnerAt: laterWinnerAtTheWaterLine ? NavmeshNow : null);
        var scene = new Scene { Navmeshes = [navmesh], Lands = [], Cells = [ChangedWater()], Settings = AllOn with { Navmeshes = false } };

        Assert.Equal(laterWinnerAtTheWaterLine ? NavmeshKey : null, Assert.Single(Plan(scene).Cells).WaterKeptBy);
    }

    /// <summary>The cleaned mod changed only the navmesh's map entry, so its triangles lie where they did before; the rock goes back into them.</summary>
    [Fact]
    public void ARockGoingBackIntoANavmeshWhoseOnlyChangeIsItsMapEntryIsNotRefused()
    {
        var entryOnly = MovedNavmesh() with { Geometry = new PartOutcome.Unchanged(), MapEntry = new PartOutcome.Undo(), Earlier = Square(BaseGame, NavmeshNow) };
        var scene = new Scene
        {
            Rocks = [Rock(0, NavmeshNow + new Vector3(0, 0, RockHalfSize / 2), RockNowPlace)],
            Others = [RockNow(0, RockNowPlace)],
            Navmeshes = [entryOnly],
            Lands = [],
            Settings = AllOn with { Navmeshes = false },
        };

        Assert.Null(Assert.Single(Plan(scene).Objects).Refused);
    }

    /// <summary>With the navmeshes switched off, the cleaned mod's navmesh stays where it moved it, so a rock going back into it is refused.</summary>
    [Fact]
    public void WithTheNavmeshesSwitchedOffARockGoingBackIntoAChangedNavmeshIsRefused()
    {
        var scene = RockInTheNavmeshTakenBack() with { Others = [RockNow(0, RockNowPlace)], Lands = [], Settings = AllOn with { Navmeshes = false } };

        Assert.Equal(new PlacedRefusal(PlacedGuard.StandsInChangedNavmesh, NavmeshKey, Failure: null), Assert.Single(Plan(scene).Objects).Refused);
    }

    /// <summary>The cleaned mod moved the navmesh onto its raised ground; switched off, the navmesh stays there and keeps that ground.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void TheGroundUnderTheCleanedModsNavmeshIsKeptWhileTheNavmeshesAreSwitchedOff(bool navmeshesOn, bool kept)
    {
        var onTheRaisedGround = MovedNavmesh() with { Current = Square(TestTargets.TargetName, NavmeshNow with { Z = Raised }) };
        var scene = new Scene { Navmeshes = [onTheRaisedGround], Settings = AllOn with { Navmeshes = navmeshesOn } };

        var land = Assert.Single(Plan(scene).Lands);

        Assert.Equal(kept ? Raised : 0f, land.FinalHeights![PointAt(20, 20)]);
    }

    /// <summary>
    /// The cell, whose music and water level the cleaned mod changed: before it the cell used the worldspace's default water height 0,
    /// and the cleaned mod stores 0 as the cell's own height, so the level stays the same.
    /// </summary>
    private static CellChangedByCleanedMod WaterSetToTheDefault(RecordKey cell) => ChangedWater() with
    {
        Cell = cell,
        EarlierWater = new CellWaterCopy(BaseGame, HasWater: true, OwnLevel: null),
        CurrentWater = new CellWaterCopy(TestTargets.TargetName, HasWater: true, 0f),
        DefaultWaterLevel = 0f,
    };

    /// <summary>
    /// The navmesh the cleaned mod moved, held in the water cell: its version at <paramref name="at"/>, and a later plugin's version
    /// at <paramref name="laterWinnerAt"/> that wins when given.
    /// </summary>
    private static NavmeshChangedByCleanedMod NavmeshInTheWaterCell(Vector3 at, Vector3? laterWinnerAt) => MovedNavmesh() with
    {
        Cell = WaterCell,
        Current = Square(TestTargets.TargetName, at) with { Cell = WaterCell },
        LaterWinner = laterWinnerAt is { } later ? Square(LaterMod.ToPluginName(), later) with { Cell = WaterCell } : null,
    };
}
