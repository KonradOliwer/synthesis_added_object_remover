using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// The guards between the kinds that go back where the cleaned mod was removed, repeated until nothing more changes, over in-memory
/// scenes: the cleaned mod raised the base game's flat ground of cell (0, 0) by 80 units and every one of its objects around the cell
/// was removed, so every area there is cleared. Its moved rocks, an edited navmesh, the cell's water level and the ground go back
/// unless what stays keeps them.
/// </summary>
public sealed class RestoreLoopTests
{
    private const float Raised = 80f;
    private const float RockHalfSize = 20f;
    private const float TooCloseDistance = 10f;
    private const float QuarterCentre = ExteriorGrid.CellSize / 4;
    private const float CellCentre = ExteriorGrid.CellSize / 2;
    private const float NavmeshHalfSize = 100f;
    private const float EarlierWater = 50f;
    private const float CleanedModsWater = -100f;
    private const int Side = TerrainGrid.VerticesPerSide;

    private static readonly ModKey ShapeMod = ModKey.FromNameAndExtension("RestoreLoopShapes.esp");
    private static readonly TestStatic Block = new(new FormKey(ShapeMod, 0x801), @"test\restoreloop\block.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-RockHalfSize), new Vector3(RockHalfSize))));
    private static readonly TestStatic House = new(new FormKey(ShapeMod, 0x802), @"test\restoreloop\house.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-100, -100, 0), new Vector3(100, 100, 100))));
    private static readonly TestStatic Tree = new(new FormKey(ShapeMod, 0x803), @"test\restoreloop\tree.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-10, -10, 0), new Vector3(10, 10, 300))));

    /// <summary>An invisible wall: a mesh with collision that stops characters and nothing to render.</summary>
    private static readonly TestStatic CollisionWall = new(
        new FormKey(ShapeMod, 0x804), @"test\restoreloop\collisionwall.nif", [], NiflySharp.Enums.SkyrimLayer.SKYL_STATIC, new Box(new Vector3(-RockHalfSize), new Vector3(RockHalfSize)));

    /// <summary>A base with the engine's marker flag and nothing to render, as a collision box primitive has.</summary>
    private static readonly TestStatic MarkerBase = new(
        new FormKey(ShapeMod, 0x805), @"test\restoreloop\markerbase.nif", [], Collision: null, new Box(new Vector3(-2), new Vector3(2)), IsMarker: true);

    private static readonly IBaseObjectShapes Shapes = TestVisibility.Over(TestShapes.Create(
        TestCollisionLayers.AddTo(new SkyrimMod(ShapeMod, SkyrimRelease.SkyrimSE)), "RestoreLoopData", Block, House, Tree, CollisionWall, MarkerBase));

    private static readonly ModKey BaseGameMod = ModKey.FromNameAndExtension("Base.esm");
    private static readonly ModKey LaterMod = ModKey.FromNameAndExtension("Later.esp");
    private static readonly PluginName BaseGame = BaseGameMod.ToPluginName();
    private static readonly PluginName[] PluginList = [BaseGame, TestTargets.TargetName, TestTargets.PatchName, LaterMod.ToPluginName()];
    private static readonly LoadOrderPlugins Plugins = TestLoadOrderPlugins.Of(
        new KnownPluginNames(PluginList), TestTargets.PatchName, [.. PluginList.Select(plugin => new PluginListing(plugin, true, []))], PluginList, []);
    private static readonly ModTrustTable Trust = TestModTrust.Of(TestTargets.TargetName, TestTargets.PatchName, trusted: [], untrusted: []);
    private static readonly OtherModsObjectsSettings Support = new(OtherModsObjectsHandling.PullObjectToTheGround, TouchGap: 1f, SupportLostFraction: 0.5f);

    private static readonly WhereRemovedRestoreSettings AllOn = new(
        OtherModsObjects: true,
        Terrain: true,
        Cells: true,
        Navmeshes: true,
        new ClearedArea(SearchRadius: 1000f, MaxStandingPercent: 0.1, ClearedAreaMeasure.ObjectCount),
        TerrainEdgeBlendDistance: 0f,
        HeightChangeTolerance: 8f,
        WaterHeightChangeTolerance: 8f,
        new TooCloseSettings(BySize: false, Multiplier: 0f, ByDistance: true, DistanceInUnits: TooCloseDistance, ZoneShape.BoundingBox, NpcHandling.Never),
        new ChangedTerrainSettings(
            On: false, TerrainCheckedObjects.VisibleObjectsOnTheGround, CountHeightChanges: false, 8f, CountTerrainTypeChanges: false, 0,
            CountWaterHeightChanges: false, 0f, CountWaterAddedOrRemoved: false, 0, AreaBySize: false, 0f, AreaByDistance: false, 0f));

    private static readonly RecordKey WaterCell = new(BaseGame, 0x700);
    private static readonly RecordKey NavmeshCell = new(BaseGame, 0x701);
    private static readonly RecordKey NavmeshKey = new(BaseGame, 0x702);

    /// <summary>A later mod's house stands on the raised ground over the navmesh's earlier place.</summary>
    private static readonly Vector3 HousePlace = new(1000, 2000, Raised);

    /// <summary>The cleaned mod moved the navmesh from under the house to here.</summary>
    private static readonly Vector3 NavmeshNow = new(2600, 2600, 0);

    private static readonly Vector3 RockNowPlace = new(3500, 1500, Raised + RockHalfSize);

    /// <summary>A later mod's crate stands next to the rock's old place, so the rock is refused; the ground under the rock where it stands now stays.</summary>
    [Fact]
    public void ARefusedRockKeepsTheGroundUnderItWhereItStandsNow()
    {
        var oldPlace = new Vector3(1500, 1500, RockHalfSize);
        var rockNow = new Vector3(1500, 2500, Raised + RockHalfSize);
        var crate = LaterObject(10, Block.Base, oldPlace + new Vector3(RockHalfSize + TooCloseDistance - 5f, 0, 0));
        var scene = new Scene { Rocks = [Rock(0, oldPlace, rockNow)], Others = [crate, RockNow(0, rockNow)] };

        var result = Plan(scene);

        Assert.Equal(new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, crate.Key, Failure: null), Assert.Single(result.Objects).Refused);
        var land = Assert.Single(result.Lands);
        Assert.Equal(Raised, land.FinalHeights![PointAt(12, 20)]);
        Assert.Contains(land.Kept, kept => kept.Reason == TerrainKeptReason.UnderObjectKeptAsChanged && kept.Points.Contains(PointAt(12, 20)));
        Assert.Equal(0f, land.FinalHeights[PointAt(12, 12)]);
    }

    /// <summary>As above, but the rock stands hidden far below the ground now, so it keeps no ground.</summary>
    [Fact]
    public void ARefusedRockHiddenFarBelowTheGroundKeepsNoGround()
    {
        var oldPlace = new Vector3(1500, 1500, RockHalfSize);
        var rockNow = new Vector3(1500, 2500, -30000f);
        var crate = LaterObject(10, Block.Base, oldPlace + new Vector3(RockHalfSize + TooCloseDistance - 5f, 0, 0));
        var scene = new Scene { Rocks = [Rock(0, oldPlace, rockNow)], Others = [crate, RockNow(0, rockNow)] };

        var result = Plan(scene);

        Assert.Equal(PlacedGuard.TooCloseToWhatStays, Assert.Single(result.Objects).Refused?.Guard);
        var land = Assert.Single(result.Lands);
        Assert.Equal(0f, land.FinalHeights![PointAt(12, 20)]);
        Assert.DoesNotContain(land.Kept, kept => kept.Reason == TerrainKeptReason.UnderObjectKeptAsChanged);
    }

    /// <summary>
    /// Rock A is refused next to a later mod's crate, so the ground under it where it stands now stays raised (grid rows 18 to 21) and
    /// the ground up to two grid steps from there is blended. Rock B's old place lies on that blended ground, which goes back, so B
    /// goes back too.
    /// </summary>
    [Fact]
    public void TheBlendAroundGroundARefusedRockKeepsRefusesNoOtherRock()
    {
        var aOldPlace = new Vector3(1500, 1500, RockHalfSize);
        var aNow = new Vector3(1500, 2500, Raised + RockHalfSize);
        var bOldPlace = new Vector3(12 * TerrainGrid.VertexSpacing, 16 * TerrainGrid.VertexSpacing, RockHalfSize);
        var crate = LaterObject(10, Block.Base, aOldPlace + new Vector3(RockHalfSize + TooCloseDistance - 5f, 0, 0));
        var scene = new Scene
        {
            Rocks = [Rock(0, aOldPlace, aNow), Rock(1, bOldPlace, RockNowPlace)],
            Others = [crate, RockNow(0, aNow), RockNow(1, RockNowPlace)],
            Settings = AllOn with { TerrainEdgeBlendDistance = 2 * TerrainGrid.VertexSpacing },
        };

        var result = Plan(scene);

        Assert.Equal(new PlacedGuard?[] { PlacedGuard.TooCloseToWhatStays, null }, result.Objects.Select(judged => judged.Refused?.Guard));
        var land = Assert.Single(result.Lands);
        Assert.True(land.Heights.Contains(PointAt(12, 17)));
        Assert.NotEqual(0f, land.FinalHeights![PointAt(12, 17)]);
    }

    /// <summary>
    /// A landscape mod after the cleaned mod raised the ground of cell (0, 0), whose LAND record the cleaned mod does not override, by
    /// the given height at the rock's old place.
    /// </summary>
    [Theory]
    [InlineData(150f, false)]
    [InlineData(4f, true)]
    public void ARockGoingBackOntoGroundALaterPluginRaisedIsRefused(float raisedBy, bool restored)
    {
        var flat = new float[TerrainGrid.VertexCount];
        var raised = Enumerable.Repeat(raisedBy, TerrainGrid.VertexCount).ToArray();
        var scene = new Scene
        {
            Rocks = [Rock(0, new Vector3(1500, 1500, RockHalfSize), RockNowPlace)],
            Others = [RockNow(0, RockNowPlace)],
            Lands = [],
            ReadHeights = cell => cell == new ExteriorCell(TestTargets.Space, 0, 0) ? new LandHeights(flat, raised) : new LandHeights(null, null),
        };

        var rock = Assert.Single(Plan(scene).Objects);

        Assert.Equal(restored ? null : new PlacedRefusal(PlacedGuard.OnGroundThatStaysChanged, By: null, Failure: null), rock.Refused);
    }

    /// <summary>A later mod's house stands in cell (−1, 0), on the raised ground; its box reaches the first points inside cell (0, 0).</summary>
    [Fact]
    public void AnObjectStandingInTheNextCellKeepsTheGroundItsBoxReachesInThisOne()
    {
        var house = LaterObject(10, House.Base, new Vector3(-50, 16 * TerrainGrid.VertexSpacing, Raised));

        var land = Assert.Single(Plan(new Scene { Others = [house] }).Lands);

        Assert.Equal((Raised, 0f), (land.FinalHeights![PointAt(1, 16)], land.FinalHeights[PointAt(3, 16)]));
        Assert.Contains(land.Kept, kept => kept.Reason == TerrainKeptReason.UnderObjectPlacedForTheGroundAsItIs && kept.Points.Contains(PointAt(1, 16)));
    }

    /// <summary>The house was placed for the raised ground; the base game's tree stood on the flat ground and the raise buried its foot.</summary>
    [Fact]
    public void ALaterModsHouseOnTheRaisedGroundKeepsItWhileABuriedBaseGameTreeIsFreed()
    {
        var house = LaterObject(10, House.Base, HousePlace);
        var tree = TestShapes.Placed(BaseGameMod, 11, Tree.Base, new Vector3(3000, 1000, 0));
        var scene = new Scene { Others = [house, tree] };

        var land = Assert.Single(Plan(scene).Lands);

        Assert.Equal((Raised, 0f), (land.FinalHeights![PointAt(8, 16)], land.FinalHeights[PointAt(23, 8)]));
        Assert.Contains(land.Kept, kept => kept.Reason == TerrainKeptReason.UnderObjectPlacedForTheGroundAsItIs && kept.Points.Contains(PointAt(8, 16)));
    }

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
        var scene = new Scene { Others = [LaterObject(10, House.Base, HousePlace)], Navmeshes = [MovedNavmesh()] };

        var result = Plan(scene);

        var group = Assert.Single(result.NavmeshGroups);
        Assert.Equal((new NavmeshRefusal(NavmeshGuard.Ground, NavmeshKey, Fault: null), false), (group.Refused, group.IsRestored));
        Assert.Empty(group.Orders);
    }

    /// <summary>The rock goes back into the navmesh's place now, which is free while the navmesh goes back and not once it is taken back.</summary>
    [Fact]
    public void ARockGoingBackIntoANavmeshThatIsTakenBackIsTakenBackToo()
    {
        var result = Plan(RockInTheNavmeshTakenBack());

        var rock = Assert.Single(result.Objects);
        Assert.Equal((new PlacedRefusal(PlacedGuard.StandsInChangedNavmesh, NavmeshKey, Failure: null), false), (rock.Refused, rock.IsRestored));
    }

    /// <summary>A later mod's crate stands between the water level before the cleaned mod and the cleaned mod's lower one, or far above both.</summary>
    [Theory]
    [InlineData(0f, true)]
    [InlineData(500f, false)]
    public void AnObjectBetweenTheTwoWaterLevelsKeepsTheCleanedModsLevelWhileTheCellsOtherPartsGoBack(float crateHeight, bool kept)
    {
        var crate = LaterObject(10, Block.Base, new Vector3(CellCentre + 300, CellCentre, crateHeight));
        var scene = new Scene { Others = [crate], Lands = [], Cells = [ChangedWater()] };

        var cell = Assert.Single(Plan(scene).Cells);

        Assert.Equal(kept ? crate.Key : (RecordKey?)null, cell.WaterKeptBy);
        Assert.Equal(kept ? new[] { CellPart.Music } : new[] { CellPart.Music, CellPart.WaterLevel }, cell.Undone.Select(part => part.Name.Part));
        Assert.True(cell.IsRestored);
    }

    /// <summary>
    /// The rock goes back across the water line. With the cell settings switched off the cleaned mod's water level stays as if every
    /// cell were kept, so the rock stays too; switched on, the level goes back with it.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ARockGoingBackAcrossAWaterLineThatStaysIsRefusedAndTheCellsSwitchedOffKeepEveryLevel(bool cellsOn, bool restored)
    {
        var scene = new Scene
        {
            Rocks = [Rock(0, new Vector3(2600, 2600, 0), RockNowPlace)],
            Others = [RockNow(0, RockNowPlace)],
            Lands = [],
            Cells = [ChangedWater()],
            Settings = AllOn with { Cells = cellsOn },
        };

        var rock = Assert.Single(Plan(scene).Objects);

        Assert.Equal(restored ? null : new PlacedRefusal(PlacedGuard.AtWaterLineThatStaysChanged, WaterCell, Failure: null), rock.Refused);
    }

    /// <summary>The rock stands on ground the cleaned mod raised that stays when the terrain is switched off, and goes back with it when on.</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ARockGoingBackOntoGroundThatStaysChangedIsRefused(bool terrainOn, bool restored)
    {
        var scene = new Scene { Rocks = [Rock(0, new Vector3(1500, 1500, RockHalfSize), RockNowPlace)], Others = [RockNow(0, RockNowPlace)], Settings = AllOn with { Terrain = terrainOn } };

        var rock = Assert.Single(Plan(scene).Objects);

        Assert.Equal(restored ? null : PlacedGuard.OnGroundThatStaysChanged, rock.Refused?.Guard);
        Assert.Equal(restored ? null : RaisedLand().LandKey, rock.Refused?.By);
    }

    /// <summary>
    /// The house keeps its ground, which takes the navmesh back, which takes the rock back, which keeps the ground under it: each pass
    /// but the last adds at least one refusal, protected point or kept water level, so the passes stay within one more than all that
    /// the final state holds.
    /// </summary>
    [Fact]
    public void TheLoopEndsWithinOneMoreThanTheRefusalsProtectedPointsAndKeptLevelsItEndsWith()
    {
        var result = Plan(RockInTheNavmeshTakenBack());

        var added = result.Objects.Count(judged => judged.Refused is not null)
            + result.NavmeshGroups.Count(group => group.Refused is not null)
            + result.Lands.Sum(land => land.Kept.Sum(kept => kept.Points.Count))
            + result.Cells.Count(cell => cell.WaterKeptBy is not null);
        Assert.InRange(result.Passes, 3, added + 1);
    }

    /// <summary>
    /// The cleaned mod disabled the rock and moved it; a later mod's crate stands next to its old place, so it stays disabled and stands
    /// nowhere: the ground where it stood moved keeps no protection, and a navmesh whose earlier triangles pass there goes back.
    /// </summary>
    [Fact]
    public void ARefusedRockTheCleanedModDisabledKeepsNoGroundAndKeepsNoNavmeshOutOfItsPlace()
    {
        var oldPlace = new Vector3(1500, 1500, RockHalfSize);
        var rockNow = new Vector3(1500, 2500, Raised + RockHalfSize);
        var crate = LaterObject(10, Block.Base, oldPlace + new Vector3(RockHalfSize + TooCloseDistance - 5f, 0, 0));
        var navmesh = MovedNavmesh() with { Earlier = Square(BaseGame, rockNow) };
        var rock = Rock(0, oldPlace, rockNow, removedNow: true);
        var scene = new Scene { Rocks = [rock], Others = [crate], Navmeshes = [navmesh], Presence = Disabled(rock.Key) };

        var result = Plan(scene);

        Assert.Equal(PlacedGuard.TooCloseToWhatStays, Assert.Single(result.Objects).Refused?.Guard);
        var land = Assert.Single(result.Lands);
        Assert.Equal(0f, land.FinalHeights![PointAt(12, 20)]);
        Assert.DoesNotContain(land.Kept, kept => kept.Reason == TerrainKeptReason.UnderObjectKeptAsChanged);
        Assert.True(Assert.Single(result.NavmeshGroups).IsRestored);
    }

    /// <summary>
    /// The navmesh's earlier door triangle names a door the cleaned mod disabled and moved. The door goes back unless a later mod's
    /// crate stands next to its old place; then it stays disabled, and the navmesh group naming it stays too.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ANavmeshGroupNamingADoorTheCleanedModDisabledGoesBackOnlyWithTheDoor(bool crateAtTheDoor)
    {
        var doorOldPlace = new Vector3(3000, 500, RockHalfSize);
        var door = Rock(1, doorOldPlace, doorOldPlace + new Vector3(0, 300, 0), removedNow: true);
        var navmesh = MovedNavmesh() with { Earlier = Square(BaseGame, HousePlace with { Z = 0 }) with { DoorTriangles = [new NavmeshDoorTriangleFact(0, door.Key)] } };
        List<OtherObject> crates = crateAtTheDoor ? [LaterObject(10, Block.Base, doorOldPlace + new Vector3(RockHalfSize + TooCloseDistance - 5f, 0, 0))] : [];
        var scene = new Scene { Rocks = [door], Others = crates, Navmeshes = [navmesh], Lands = [], Presence = Disabled(door.Key) };

        var result = Plan(scene);

        Assert.Equal(crateAtTheDoor, Assert.Single(result.Objects).Refused is not null);
        var group = Assert.Single(result.NavmeshGroups);
        Assert.Equal(crateAtTheDoor ? new NavmeshRefusal(NavmeshGuard.Door, door.Key, Fault: null) : null, group.Refused);
    }

    /// <summary>
    /// As above, but a later patch carries the cleaned mod's disable of the door forward, so no part of the door goes back and the run
    /// holds no placed record fact of it: the navmesh's door facts say it is removed, it stays disabled, and the group naming it stays.
    /// </summary>
    [Fact]
    public void ANavmeshGroupNamingADoorALaterPluginKeepsDisabledIsRefused()
    {
        var door = new RecordKey(BaseGame, 0x950);
        var navmesh = MovedNavmesh() with
        {
            Earlier = Square(BaseGame, HousePlace with { Z = 0 }) with { DoorTriangles = [new NavmeshDoorTriangleFact(0, door)] },
            Doors = [new DoorLinkChange(door, new PartOutcome.Unchanged(), Earlier: null, Current: null, InDeletedCell: false)],
        };

        var result = Plan(new Scene { Navmeshes = [navmesh], Lands = [], Presence = Disabled(door) });

        Assert.Equal(new NavmeshRefusal(NavmeshGuard.Door, door, Fault: null), Assert.Single(result.NavmeshGroups).Refused);
        Assert.Equal([door], result.DoorsLeftRemoved.ToList());
    }

    /// <summary>
    /// The navmesh's earlier door triangle names a door the cleaned mod disabled and moved, and the door's position goes back. Its
    /// presence does not go back (a later patch carries the disable forward), or goes back to a version that was disabled too: either
    /// way the door ends disabled, and the navmesh group naming it stays.
    /// </summary>
    [Theory]
    [InlineData(PlacedParts.Position, false)]
    [InlineData(PlacedParts.Position | PlacedParts.Presence, true)]
    public void ANavmeshGroupNamingADoorThatEndsDisabledIsRefusedThoughTheDoorGoesBack(PlacedParts undo, bool removedBefore)
    {
        var doorOldPlace = new Vector3(3000, 500, RockHalfSize);
        var moved = Rock(1, doorOldPlace, doorOldPlace + new Vector3(0, 300, 0), removedNow: true);
        var door = moved with { Earlier = moved.Earlier with { Undo = undo }, PresenceBefore = new PlacedPresence(removedBefore, Parent: null, OppositeOfParent: false) };
        var navmesh = MovedNavmesh() with
        {
            Earlier = Square(BaseGame, HousePlace with { Z = 0 }) with { DoorTriangles = [new NavmeshDoorTriangleFact(0, door.Key)] },
            Doors = [new DoorLinkChange(door.Key, new PartOutcome.Unchanged(), Earlier: null, Current: null, InDeletedCell: false)],
        };

        var result = Plan(new Scene { Rocks = [door], Navmeshes = [navmesh], Lands = [], Presence = Disabled(door.Key) });

        Assert.True(Assert.Single(result.Objects).IsRestored);
        Assert.Equal(new NavmeshRefusal(NavmeshGuard.Door, door.Key, Fault: null), Assert.Single(result.NavmeshGroups).Refused);
        Assert.Equal([door.Key], result.DoorsLeftRemoved.ToList());
    }

    /// <summary>
    /// The navmesh's earlier door triangle names a door that is enabled with a parent, or with the opposite of it. A base game parent
    /// the cleaned mod disabled stays disabled. The door ends disabled, and the group naming it stays, when the parent is disabled
    /// and the door follows it, or the parent stands and the door is the opposite of it.
    /// </summary>
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    public void ANavmeshGroupNamingADoorEnabledWithAParentGoesBackOnlyWhileTheDoorEndsEnabled(bool parentDisabled, bool doorOppositeOfParent, bool refused)
    {
        var door = new RecordKey(BaseGame, 0x950);
        var link = new NavDoorLinkFact(NavmeshKey, Triangle: 0);
        var navmesh = MovedNavmesh() with
        {
            Earlier = Square(BaseGame, HousePlace with { Z = 0 }) with { DoorTriangles = [new NavmeshDoorTriangleFact(link.Triangle, door)] },
            Doors = [new DoorLinkChange(door, new PartOutcome.Unchanged(), link, link, InDeletedCell: false)],
        };

        var result = Plan(new Scene { Navmeshes = [navmesh], Lands = [], Presence = FollowingAParent(door, parentDisabled, doorOppositeOfParent) });

        Assert.Equal(refused ? new NavmeshRefusal(NavmeshGuard.Door, door, Fault: null) : null, Assert.Single(result.NavmeshGroups).Refused);
        Assert.Equal(refused ? [door] : [], result.DoorsLeftRemoved.ToList());
    }

    /// <summary>
    /// A later mod's house stands over the navmesh's earlier place. It is enabled with a parent that stands, or with a base game parent
    /// that stays disabled, which takes the house with it, so the house stands in no navmesh.
    /// </summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void AnObjectEnabledWithAParentRefusesANavmeshGroupOnlyWhileTheParentIsEnabled(bool parentDisabled, bool refused)
    {
        var house = LaterObject(10, House.Base, HousePlace with { Z = -10 });
        var scene = new Scene { Others = [house], Navmeshes = [MovedNavmesh()], Lands = [], Presence = FollowingAParent(house.Key, parentDisabled) };

        var group = Assert.Single(Plan(scene).NavmeshGroups);

        Assert.Equal(refused ? NavmeshGuard.Object : null, group.Refused?.Guard);
    }

    /// <summary>A later mod's crate stands between the two water levels of the cell, enabled with a parent that stands or with one that stays disabled.</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void AnObjectEnabledWithAParentKeepsTheCleanedModsWaterLevelOnlyWhileTheParentIsEnabled(bool parentDisabled, bool kept)
    {
        var crate = LaterObject(10, Block.Base, new Vector3(CellCentre + 300, CellCentre, 0));
        var scene = new Scene { Others = [crate], Lands = [], Cells = [ChangedWater()], Presence = FollowingAParent(crate.Key, parentDisabled) };

        var cell = Assert.Single(Plan(scene).Cells);

        Assert.Equal(kept ? crate.Key : (RecordKey?)null, cell.WaterKeptBy);
    }

    /// <summary>As the first of the refused rocks: a later mod's crate refuses the rock, which is enabled with a parent that stands or one that stays disabled.</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ARefusedRockEnabledWithAParentKeepsTheGroundUnderItOnlyWhileTheParentIsEnabled(bool parentDisabled, bool kept)
    {
        var oldPlace = new Vector3(1500, 1500, RockHalfSize);
        var rockNow = new Vector3(1500, 2500, Raised + RockHalfSize);
        var crate = LaterObject(10, Block.Base, oldPlace + new Vector3(RockHalfSize + TooCloseDistance - 5f, 0, 0));
        var rock = Rock(0, oldPlace, rockNow);
        var scene = new Scene { Rocks = [rock], Others = [crate, RockNow(0, rockNow)], Presence = FollowingAParent(rock.Key, parentDisabled) };

        var land = Assert.Single(Plan(scene).Lands);

        Assert.Equal(kept, land.Kept.Any(points => points.Reason == TerrainKeptReason.UnderObjectKeptAsChanged));
    }

    /// <summary>
    /// Rock P was disabled before the cleaned mod, which enabled and moved it. An object of another plugin, which the cleaned mod
    /// moved, is enabled with P and stands in the middle of the cleared cell on the raised ground. P's restore disables it again and
    /// the object with it, so the ground under it goes back; a later mod's crate beside P's old place refuses P, which stays enabled,
    /// and the object stands, so its ground stays.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void TheGroundUnderAnObjectEnabledWithARockIsKeptOnlyWhenARefusalKeepsTheRockEnabled(bool crateRefusesTheRock, bool kept)
    {
        var oldPlace = new Vector3(1500, 1500, RockHalfSize);
        var rockNow = new Vector3(1500, 2500, Raised + RockHalfSize);
        var rock = WithPresenceRestoredToDisabled(Rock(0, oldPlace, rockNow));
        var scene = new Scene
        {
            Rocks = [rock],
            Others = [RockNow(0, rockNow), .. CrateBesideTheRock(oldPlace, crateRefusesTheRock)],
            Standing = [new Vector3(CellCentre, CellCentre, Raised)],
            Presence = new Dictionary<RecordKey, PlacedPresence> { [FirstStandingTargetKey()] = new(Removed: false, rock.Key, OppositeOfParent: false) },
        };

        var result = Plan(scene);

        Assert.Equal(crateRefusesTheRock ? PlacedGuard.TooCloseToWhatStays : null, Assert.Single(result.Objects).Refused?.Guard);
        var land = Assert.Single(result.Lands);
        Assert.Equal(kept, land.Kept.Any(points => points.Reason == TerrainKeptReason.UnderObjectThatStays && points.Points.Contains(PointAt(16, 16))));
        Assert.Equal(kept ? Raised : 0f, land.FinalHeights![PointAt(16, 16)]);
    }

    /// <summary>
    /// The navmesh's earlier door triangle names a door that is the opposite of rock P, which was disabled before the cleaned mod.
    /// P's restore disables it, which enables the door, so the group naming the door goes back; a later mod's crate beside P's old
    /// place refuses P, which stays enabled, and the door stays disabled with it, so the group is refused in the pass after.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ANavmeshGroupNamingTheOppositeOfARockIsRefusedInThePassAfterTheRockIsRefused(bool crateRefusesTheRock)
    {
        var oldPlace = new Vector3(1500, 1500, RockHalfSize);
        var rockNow = new Vector3(1500, 2500, Raised + RockHalfSize);
        var rock = WithPresenceRestoredToDisabled(Rock(0, oldPlace, rockNow));
        var door = new RecordKey(BaseGame, 0x950);
        var link = new NavDoorLinkFact(NavmeshKey, Triangle: 0);
        var navmesh = MovedNavmesh() with
        {
            Earlier = Square(BaseGame, HousePlace with { Z = 0 }) with { DoorTriangles = [new NavmeshDoorTriangleFact(link.Triangle, door)] },
            Doors = [new DoorLinkChange(door, new PartOutcome.Unchanged(), link, link, InDeletedCell: false)],
        };
        var scene = new Scene
        {
            Rocks = [rock],
            Others = [RockNow(0, rockNow), .. CrateBesideTheRock(oldPlace, crateRefusesTheRock)],
            Navmeshes = [navmesh],
            Lands = [],
            Presence = new Dictionary<RecordKey, PlacedPresence> { [door] = new(Removed: false, rock.Key, OppositeOfParent: true) },
        };

        var result = Plan(scene);

        Assert.Equal(crateRefusesTheRock ? PlacedGuard.TooCloseToWhatStays : null, Assert.Single(result.Objects).Refused?.Guard);
        Assert.Equal(crateRefusesTheRock ? new NavmeshRefusal(NavmeshGuard.Door, door, Fault: null) : null, Assert.Single(result.NavmeshGroups).Refused);
        Assert.Equal(crateRefusesTheRock ? [door] : [], result.DoorsLeftRemoved.ToList());
    }

    /// <summary>A later mod's crate stands next to the rock's old place when <paramref name="present"/>, so the rock is refused.</summary>
    private static List<OtherObject> CrateBesideTheRock(Vector3 oldPlace, bool present) =>
        present ? [LaterObject(10, Block.Base, oldPlace + new Vector3(RockHalfSize + TooCloseDistance - 5f, 0, 0))] : [];

    /// <summary>The base game disabled the rock, and the cleaned mod enabled it and moved it: its restore moves it back and disables it again.</summary>
    private static RecordChangedByCleanedMod WithPresenceRestoredToDisabled(RecordChangedByCleanedMod rock) =>
        rock with { Earlier = rock.Earlier with { Undo = PlacedParts.Position | PlacedParts.Presence }, PresenceBefore = new PlacedPresence(Removed: true, Parent: null, OppositeOfParent: false) };

    /// <summary>The winning presence of a record enabled with a parent, which a base game plugin's disable keeps disabled when <paramref name="parentDisabled"/>.</summary>
    private static Dictionary<RecordKey, PlacedPresence> FollowingAParent(RecordKey record, bool parentDisabled, bool oppositeOfParent = false)
    {
        var parent = new RecordKey(BaseGame, 0x951);
        var presence = parentDisabled ? Disabled(parent) : new Dictionary<RecordKey, PlacedPresence>();
        presence.Add(record, new PlacedPresence(Removed: false, parent, oppositeOfParent));
        return presence;
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

    /// <summary>The cleaned mod stores the worldspace's default water height 0 as the cell's own; a later mod's crate stands at that height.</summary>
    [Fact]
    public void AnObjectAtTheWorldspaceDefaultWaterLevelKeepsNoLevelThatACellOnlyStoresAsItsOwn()
    {
        var crate = LaterObject(10, Block.Base, new Vector3(CellCentre + 300, CellCentre, 0));

        var cell = Assert.Single(Plan(new Scene { Others = [crate], Lands = [], Cells = [WaterSetToTheDefault(WaterCell)] }).Cells);

        Assert.Equal((null, true), (cell.WaterKeptBy, cell.Undone.Any(part => part.Name.Part == CellPart.WaterLevel)));
    }

    /// <summary>As above with the cells switched off, so the level the cell stores stays: the rock going back across height 0 is not refused.</summary>
    [Fact]
    public void ARockGoingBackAcrossTheDefaultWaterLevelThatACellStoresAsItsOwnIsNotRefused()
    {
        var scene = new Scene
        {
            Rocks = [Rock(0, new Vector3(2600, 2600, 0), RockNowPlace)],
            Others = [RockNow(0, RockNowPlace)],
            Lands = [],
            Cells = [WaterSetToTheDefault(WaterCell)],
            Settings = AllOn with { Cells = false },
        };

        Assert.Null(Assert.Single(Plan(scene).Objects).Refused);
    }

    /// <summary>As above, for the cell holding the navmesh: its group is not refused for the water level that stays.</summary>
    [Fact]
    public void ANavmeshGroupInACellThatStoresTheDefaultWaterLevelAsItsOwnGoesBack()
    {
        var scene = new Scene { Navmeshes = [MovedNavmesh()], Lands = [], Cells = [WaterSetToTheDefault(NavmeshCell)], Settings = AllOn with { Cells = false } };

        Assert.True(Assert.Single(Plan(scene).NavmeshGroups).IsRestored);
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
    /// Many rocks going back across the cell, some next to later mods' crates, with the navmesh, the house and the water: the results
    /// are the same with one worker or eight, and whatever the order of the records and objects read.
    /// </summary>
    [Fact]
    public void TheResultsDoNotDependOnTheNumberOfWorkersOrTheOrderOfTheRecordsRead()
    {
        var scene = ManyRocks();
        var random = new Random(7);
        var shuffled = scene with { Rocks = [.. scene.Rocks.OrderBy(_ => random.Next())], Others = [.. scene.Others.OrderBy(_ => random.Next())] };

        var results = new[] { scene with { Workers = 1 }, scene with { Workers = 8 }, shuffled with { Workers = 1 }, shuffled with { Workers = 8 } }.Select(run => Describe(Plan(run))).ToList();

        Assert.All(results, result => Assert.Equal(results[0], result));
        Assert.Contains(Plan(scene).Objects, judged => judged.Refused is not null);
    }

    [Fact]
    public void TheOrdersDoNotDependOnTheNumberOfWorkers()
    {
        var scene = RockInTheNavmeshTakenBack() with
        {
            Others = [.. RockInTheNavmeshTakenBack().Others, TestShapes.Placed(BaseGameMod, 11, Tree.Base, new Vector3(3000, 1000, 0)), LaterObject(12, Block.Base, new Vector3(CellCentre + 300, CellCentre, 0))],
            Cells = [ChangedWater()],
        };

        var results = new[] { 1, 2, 8 }.Select(workers => Describe(Plan(scene with { Workers = workers }))).ToList();

        Assert.All(results, result => Assert.Equal(results[0], result));
    }

    /// <summary>
    /// The cleaned mod moved a base game door and relinked it to another triangle of a base game navmesh it did not change. The door
    /// goes back with its earlier link when the consistency check finds no fault with its restore written; with a fault it keeps the
    /// cleaned mod's place and link, naming the navmesh at fault; a link a later plugin changed again keeps it too.
    /// </summary>
    [Theory]
    [InlineData(false, false, null)]
    [InlineData(true, false, PlacedGuard.DoorLinkDoesNotMatch)]
    [InlineData(false, true, PlacedGuard.DoorLinkChangedLater)]
    public void ADoorTheCleanedModMovedGoesBackOnlyWithItsEarlierNavmeshLinkWhereThatLinkFits(bool fault, bool linkChangedLater, PlacedGuard? refused)
    {
        PartOutcome outcome = linkChangedLater ? new PartOutcome.LeftToLaterPlugin(LaterMod.ToPluginName()) : new PartOutcome.Undo();
        var door = MovedDoor(outcome, new NavDoorLinkFact(UnchangedNavmesh, Triangle: 0), new NavDoorLinkFact(UnchangedNavmesh, Triangle: 1));
        var scene = new Scene
        {
            Rocks = [door],
            Others = [RockNow(DoorIndex, DoorNow)],
            Lands = [],
            FindFault = orders => fault && orders.OfType<CopyEarlierPlacedParts>().Any(order => order.Record == door.Key)
                ? new NavmeshFault(UnchangedNavmesh, NavmeshRule.Doors, NavmeshFaultKind.DoorLinkWithoutDoorTriangle, 0, door.Key)
                : null,
        };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal(refused, judged.Refused?.Guard);
        Assert.Equal(refused == PlacedGuard.DoorLinkDoesNotMatch ? UnchangedNavmesh : null, judged.Refused?.By);
        Assert.Equal(linkChangedLater ? PlacedParts.Position : PlacedParts.Position | PlacedParts.NavDoorLink, judged.Change.Earlier.Undo);
    }

    /// <summary>
    /// The cleaned mod moved a base game door and gave it a door triangle of the navmesh it moved; with the navmeshes switched off that
    /// navmesh stays as the cleaned mod made it and names the door at another triangle than the door's earlier link, so the door stays.
    /// </summary>
    [Fact]
    public void ADoorWhoseEarlierLinkANavmeshThatStaysChangedDoesNotAnswerStays()
    {
        var door = MovedDoor(new PartOutcome.Undo(), new NavDoorLinkFact(UnchangedNavmesh, Triangle: 0), new NavDoorLinkFact(NavmeshKey, Triangle: 1));
        var navmesh = MovedNavmesh() with { Current = Square(TestTargets.TargetName, NavmeshNow) with { DoorTriangles = [new NavmeshDoorTriangleFact(1, door.Key)] } };
        var scene = new Scene { Rocks = [door], Others = [RockNow(DoorIndex, DoorNow)], Navmeshes = [navmesh], Lands = [], Settings = AllOn with { Navmeshes = false } };

        Assert.Equal(new PlacedRefusal(PlacedGuard.DoorLinkDoesNotMatch, NavmeshKey, Failure: null), Assert.Single(Plan(scene).Objects).Refused);
    }

    /// <summary>
    /// The cleaned mod moved a base game door and the navmesh whose door triangle names it, and relinked the door to the moved triangle.
    /// The door and the navmesh go back together: the house standing in the navmesh's earlier place keeps the navmesh, and the door
    /// with it; a crate beside the door's old place keeps the door, and the navmesh with it. The door's restore writes its link, the
    /// navmesh group does not.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ADoorThatTakesItsLinkWithItAndTheNavmeshItNamesGoBackTogetherOrNotAtAll(bool houseStands, bool crateAtTheDoor)
    {
        var earlier = new NavDoorLinkFact(NavmeshKey, Triangle: 0);
        var current = new NavDoorLinkFact(NavmeshKey, Triangle: 1);
        var door = MovedDoor(new PartOutcome.Undo(), earlier, current);
        var navmesh = MovedNavmesh() with
        {
            Earlier = Square(BaseGame, HousePlace with { Z = 0 }) with { DoorTriangles = [new NavmeshDoorTriangleFact(earlier.Triangle, door.Key)] },
            Current = Square(TestTargets.TargetName, NavmeshNow) with { DoorTriangles = [new NavmeshDoorTriangleFact(current.Triangle, door.Key)] },
            Doors = [new DoorLinkChange(door.Key, new PartOutcome.Undo(), earlier, current, InDeletedCell: false)],
        };
        List<OtherObject> others =
        [
            RockNow(DoorIndex, DoorNow),
            .. houseStands ? [LaterObject(10, House.Base, HousePlace with { Z = -10 })] : Array.Empty<OtherObject>(),
            .. CrateBesideTheRock(DoorOldPlace, crateAtTheDoor),
        ];

        var result = Plan(new Scene { Rocks = [door], Others = others, Navmeshes = [navmesh], Lands = [] });

        var judged = Assert.Single(result.Objects);
        var group = Assert.Single(result.NavmeshGroups);
        var together = !houseStands && !crateAtTheDoor;
        Assert.Equal((together, together), (judged.IsRestored, group.IsRestored));
        if (houseStands) Assert.Equal(new PlacedRefusal(PlacedGuard.DoorLinkDoesNotMatch, NavmeshKey, Failure: null), judged.Refused);
        if (crateAtTheDoor) Assert.Equal(new NavmeshRefusal(NavmeshGuard.Door, door.Key, Fault: null), group.Refused);
        Assert.DoesNotContain(group.Orders, order => order.Record == door.Key);
    }

    /// <summary>
    /// The cleaned mod edited the navmesh and deleted the base game door its earlier door triangle names. The consistency check over the
    /// group's orders sees the door brought back by its own restore, so the group goes back with it.
    /// </summary>
    [Fact]
    public void TheConsistencyCheckOfANavmeshGroupSeesTheRestoreOfADoorTheCleanedModDeleted()
    {
        var link = new NavDoorLinkFact(NavmeshKey, Triangle: 0);
        var deleted = Rock(DoorIndex, DoorOldPlace, DoorNow) with
        {
            Earlier = Rock(DoorIndex, DoorOldPlace, DoorNow).Earlier with { Undo = PlacedParts.Presence },
            DoorLink = new DoorLinkChange(DoorKey, new PartOutcome.Undo(), link, Current: null, InDeletedCell: false),
        };
        var navmesh = MovedNavmesh() with
        {
            Earlier = Square(BaseGame, HousePlace with { Z = 0 }) with { DoorTriangles = [new NavmeshDoorTriangleFact(link.Triangle, DoorKey)] },
            Doors = [new DoorLinkChange(DoorKey, new PartOutcome.Undo(), link, Current: null, InDeletedCell: false)],
        };
        var scene = new Scene
        {
            Rocks = [deleted],
            Navmeshes = [navmesh],
            Lands = [],
            Presence = Disabled(DoorKey),
            FindFault = orders => orders.Any(order => order is CopyEarlierPlacedParts { Parts: var parts } undo && undo.Record == DoorKey && parts.HasFlag(PlacedParts.Presence))
                ? null
                : new NavmeshFault(NavmeshKey, NavmeshRule.Doors, NavmeshFaultKind.DoorMissing, 0, DoorKey),
        };

        var result = Plan(scene);

        Assert.True(Assert.Single(result.Objects).IsRestored);
        Assert.True(Assert.Single(result.NavmeshGroups).IsRestored);
    }

    /// <summary>
    /// The cleaned mod deleted two base game doors that a base game navmesh it did not change names at its door triangles. The navmesh
    /// is consistent only with both doors back, so both go back; a crate beside the first door's old place keeps it, and the second
    /// with it.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TwoDeletedDoorsThatANavmeshThatStaysNamesGoBackTogetherOrNotAtAll(bool crateAtTheFirstDoor)
    {
        var secondOldPlace = DoorOldPlace + new Vector3(0, 1000, 0);
        var first = DeletedDoor(DoorIndex, DoorOldPlace, new NavDoorLinkFact(UnchangedNavmesh, Triangle: 0));
        var second = DeletedDoor(DoorIndex + 1, secondOldPlace, new NavDoorLinkFact(UnchangedNavmesh, Triangle: 1));
        var scene = new Scene
        {
            Rocks = [first, second],
            Others = CrateBesideTheRock(DoorOldPlace, crateAtTheFirstDoor),
            Lands = [],
            Presence = Disabled(first.Key, second.Key),
            FindFault = orders => new[] { first.Key, second.Key }.All(door => orders.Any(order => order is CopyEarlierPlacedParts { Parts: var parts } undo && undo.Record == door && parts.HasFlag(PlacedParts.Presence)))
                ? null
                : new NavmeshFault(UnchangedNavmesh, NavmeshRule.Doors, NavmeshFaultKind.DoorMissing, 0, first.Key),
        };

        var objects = Plan(scene).Objects;

        Assert.Equal([!crateAtTheFirstDoor, !crateAtTheFirstDoor], objects.Select(judged => judged.IsRestored));
        if (!crateAtTheFirstDoor) return;
        Assert.Equal(PlacedGuard.TooCloseToWhatStays, objects[0].Refused?.Guard);
        Assert.Equal(new PlacedRefusal(PlacedGuard.DoorLinkDoesNotMatch, UnchangedNavmesh, Failure: null), objects[1].Refused);
    }

    /// <summary>A base game door the cleaned mod deleted, whose presence and navmesh link go back.</summary>
    private static RecordChangedByCleanedMod DeletedDoor(int index, Vector3 place, NavDoorLinkFact link)
    {
        var door = Rock(index, place, place);
        return door with
        {
            Earlier = door.Earlier with { Undo = PlacedParts.Presence },
            DoorLink = new DoorLinkChange(door.Key, new PartOutcome.Undo(), link, Current: null, InDeletedCell: false),
        };
    }

    /// <summary>
    /// A later mod's invisible wall, or a primitive of it on a collision layer that stops characters, stands in the navmesh's earlier
    /// place, so the navmesh stays; a trigger primitive lets characters through, so it goes back.
    /// </summary>
    [Theory]
    [InlineData(false, null, true)]
    [InlineData(true, (uint)CollisionLayer.CollisionBox, true)]
    [InlineData(true, (uint)CollisionLayer.Trigger, false)]
    public void AnInvisibleObjectThatStopsCharactersInTheNavmeshsEarlierPlaceKeepsTheNavmesh(bool primitive, uint? layer, bool refused)
    {
        var blocker = primitive
            ? LaterObject(10, MarkerBase.Base, HousePlace with { Z = 0 }) with { IsPrimitive = true }
            : LaterObject(10, CollisionWall.Base, HousePlace with { Z = 0 });
        var facts = TestPlacedRecords.PlainReference with { IsPrimitive = true, PrimitiveBounds = new Vector3(2 * RockHalfSize), CollisionLayer = layer };
        var scene = new Scene
        {
            Others = [blocker],
            Navmeshes = [MovedNavmesh()],
            Lands = [],
            Primitives = primitive ? new Dictionary<RecordKey, ReferenceFacts> { [blocker.Key] = facts } : new(),
        };

        Assert.Equal(refused ? new NavmeshRefusal(NavmeshGuard.Object, blocker.Key, Fault: null) : null, Assert.Single(Plan(scene).NavmeshGroups).Refused);
    }

    /// <summary>
    /// A later mod's crate stands next to the rock's old place, enabled with one of the cleaned mod's objects the run removes, or on its
    /// own: only the crate that stands in the final world keeps the rock out.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void AnObjectEnabledWithAnObjectTheRunRemovesKeepsNoRockOut(bool enabledWithARemovedObject, bool refused)
    {
        var oldPlace = new Vector3(1500, 1500, RockHalfSize);
        var crate = LaterObject(10, Block.Base, oldPlace + new Vector3(RockHalfSize + TooCloseDistance - 5f, 0, 0));
        var scene = new Scene
        {
            Rocks = [Rock(0, oldPlace, RockNowPlace)],
            Others = [crate, RockNow(0, RockNowPlace)],
            Lands = [],
            Presence = enabledWithARemovedObject ? new Dictionary<RecordKey, PlacedPresence> { [crate.Key] = new(Removed: false, TestTargets.Key(0), OppositeOfParent: false) } : new(),
        };

        Assert.Equal(refused ? PlacedGuard.TooCloseToWhatStays : null, Assert.Single(Plan(scene).Objects).Refused?.Guard);
    }

    /// <summary>
    /// A later mod's crate stands on the raised ground, enabled with one of the cleaned mod's objects the run removes, or on its own:
    /// only the crate that stands in the final world keeps the ground under it raised.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void AnObjectEnabledWithAnObjectTheRunRemovesKeepsNoGroundPlacedForIt(bool enabledWithARemovedObject, bool kept)
    {
        var crate = LaterObject(10, Block.Base, new Vector3(CellCentre + 300, CellCentre + 300, Raised + RockHalfSize));
        var scene = new Scene
        {
            Others = [crate],
            Presence = enabledWithARemovedObject ? new Dictionary<RecordKey, PlacedPresence> { [crate.Key] = new(Removed: false, TestTargets.Key(0), OppositeOfParent: false) } : new(),
        };

        var land = Assert.Single(Plan(scene).Lands);

        Assert.Equal(kept, land.Kept.Any(points => points.Reason == TerrainKeptReason.UnderObjectPlacedForTheGroundAsItIs));
    }

    /// <summary>
    /// Rock C is refused next to a later mod's crate, so it stays where it stands now, next to rock D's old place, and refuses D in the
    /// pass after. The too-close rule checks D again in that pass, but not rock E far away, which nothing new can reach.
    /// </summary>
    [Fact]
    public void TheTooCloseRuleChecksAgainOnlyTheRecordsACauseNoLongerLeftOutCanReach()
    {
        var cOldPlace = new Vector3(1500, 1500, RockHalfSize);
        var cNow = new Vector3(1500, 2500, Raised + RockHalfSize);
        var dOldPlace = cNow + new Vector3(RockHalfSize + TooCloseDistance - 5f, 0, 0);
        var eOldPlace = new Vector3(3000, 3000, RockHalfSize);
        var checks = new ConcurrentQueue<RecordKey>();
        var scene = new Scene
        {
            Rocks = [Rock(0, cOldPlace, cNow), Rock(1, dOldPlace, new Vector3(2500, 500, Raised)), Rock(2, eOldPlace, new Vector3(3200, 3000, Raised))],
            Others = [.. CrateBesideTheRock(cOldPlace, present: true), RockNow(0, cNow), RockNow(1, new Vector3(2500, 500, Raised)), RockNow(2, new Vector3(3200, 3000, Raised))],
            Lands = [],
            TooCloseChecks = checks,
        };

        var result = Plan(scene);

        Assert.Equal(new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, scene.Rocks[0].Key, Failure: null), result.Objects[1].Refused);
        Assert.Null(result.Objects[2].Refused);
        Assert.Equal((2, 1), (checks.Count(key => key == scene.Rocks[1].Key), checks.Count(key => key == scene.Rocks[2].Key)));
    }

    /// <summary>With no ground going back, the objects of other plugins are neither listed nor indexed for the ground placed for them (G2).</summary>
    [Fact]
    public void WithoutRestoredGroundNoObjectIsIndexedForTheGroundPlacedForIt()
    {
        var plan = new TerrainUndoPlan([], _ => new LandHeights(null, null), heightTolerance: 8f, blendDistance: 0f);
        var guards = new TerrainGuards(
            () => throw new InvalidOperationException("The objects were listed."),
            new GroundUnderObjects(Shapes, space => space),
            new Dictionary<RecordKey, ImmutableArray<RecordKey>>(),
            _ => null,
            heightTolerance: 8f,
            new Execution(1));

        Assert.Empty(guards.UnderObjectsPlacedForTheGroundAsItIs(plan, _ => false));
    }

    /// <summary>A plugin deletes the cell a rock or a LAND record would be written into, so neither goes back.</summary>
    [Fact]
    public void NothingGoesBackIntoACellAPluginDeletes()
    {
        var rock = Rock(0, new Vector3(1500, 1500, RockHalfSize), RockNowPlace) with { InDeletedCell = true };
        var scene = new Scene { Rocks = [rock], Others = [RockNow(0, RockNowPlace)], Lands = [RaisedLand() with { CellDeletedInAVersion = true }] };

        var result = Plan(scene);

        Assert.Equal((NeverRestoredReason.InDeletedCell, false), (Assert.Single(result.Objects).Never, result.Objects[0].IsRestored));
        Assert.Equal((NeverRestoredReason.InDeletedCell, false), (Assert.Single(result.Lands).Never, result.Lands[0].IsRestored));
    }

    /// <summary>
    /// The placed guards answer the same over the objects within their reach as over every object: 144 rocks going back, every fifth
    /// next to a later mod's crate and every seventh carrying a later mod's block where it stands now, among later mods' blocks far
    /// away. Only the objects within reach are indexed.
    /// </summary>
    [Fact]
    public void ThePlacedGuardsAnswerTheSameOverTheObjectsWithinTheirReachAsOverEveryObject()
    {
        const float Everywhere = 20000f;
        var scene = ManyRocks();
        List<OtherObject> carried = [.. scene.Rocks.Where((_, index) => index % 7 == 0).Select((rock, index) => LaterObject(3000 + index, Block.Base, rock.WhereItIsNow!.Value.Position + new Vector3(0, 0, 2 * RockHalfSize)))];
        List<OtherObject> farAway = [.. Enumerable.Range(0, 50).Select(index => LaterObject(4000 + index, Block.Base, new Vector3(8000 + 50 * index, 6000, RockHalfSize)))];
        var everything = scene with { Others = [.. scene.Others, .. carried, .. farAway] };

        var (withinReach, indexedWithinReach) = CheckPlacedRecords(everything, new GuardReach(Shapes, AllOn.TooClose, Support.TouchGap));
        var (overEverything, indexedEverywhere) = CheckPlacedRecords(everything, new GuardReach(Shapes, AllOn.TooClose with { DistanceInUnits = Everywhere }, Everywhere));

        Assert.Equal(overEverything, withinReach);
        Assert.Contains(withinReach, refusal => refusal?.Guard == PlacedGuard.TooCloseToWhatStays);
        Assert.Contains(withinReach, refusal => refusal?.Guard == PlacedGuard.CarriesObjectThatStays);
        Assert.True(indexedWithinReach <= indexedEverywhere - farAway.Count);
    }

    /// <summary>The first pass of the placed guards over every rock of the scene going back, and how many objects their caches index.</summary>
    private static (List<PlacedRefusal?> Found, int Indexed) CheckPlacedRecords(Scene scene, GuardReach reach)
    {
        var removedTargets = RemovedTargets();
        ImmutableArray<TargetObject> targets = [.. removedTargets];
        var collected = TestScenes.CreateWorld(targets, [.. scene.Others.Select((other, index) => other with { Id = new OtherId(index) })]) with { ChangedRecords = [.. scene.Rocks] };
        var final = TestSeededDecisions.StartDisablingAll(ObjectsToKeep.Build(targets, [], TestTargets.References(targets.Length), TestBaseFacts.Unresolved), targets.Length)
            .Apply(RoundKind.TooClose, [.. removedTargets.Select(target => new ProposedRemoval(target.Id, new RemovalReason.TooClose(new OtherId(0), new TooCloseCause.ObjectSize())))]);
        List<RecordChangedByCleanedMod> cleared = [.. scene.Rocks.OrderBy(rock => rock.Key, RecordKeyOrder.Comparer)];
        var world = AfterRemovalWorld.Of(collected, final, OtherModsObjectsResult.SwitchedOff, TestTargets.TargetName, cleared, reach);
        var rules = TestPlacedGuardRules.Create(Shapes, Trust, Plugins, Support, TestGround.NoTerrain(), threads: 1);
        var caches = rules.WorldCaches.Of(world.World);
        var inputs = new PlacedGuardInputs(
            caches,
            excluded => rules.TooClose(world.World, caches, excluded, AllOn.TooClose),
            rules.HoldersOfObjectsThatStay.Over(world.World, world.Now),
            Shapes,
            new ObjectsThatBlockActors(Shapes, new Dictionary<RecordKey, ReferenceFacts>()),
            new NavmeshIndex(_ => null),
            space => space,
            AllOn.ChangedTerrain);
        var presence = FinalPresence.Of(collected, removedTargets.Select(target => target.Key).ToHashSet(), cleared);
        var guards = new PlacedGuards(world, inputs, reach, presence, new DoorLinkGuard(cleared, [], _ => null), new Execution(1));
        var plans = new OtherKindsAsPlanned(
            new TerrainUndoPlan([], _ => new LandHeights(null, null), AllOn.HeightChangeTolerance, AllOn.TerrainEdgeBlendDistance),
            new WaterUndoPlan([], [], AllOn.WaterHeightChangeTolerance),
            [],
            new HashSet<RecordKey>());
        var found = guards.CheckGoingBack(Enumerable.Range(0, cleared.Count).ToHashSet(), plans, causesLeftOutBefore: null).Found;
        return ([.. found], world.World.OtherModAndSupportOnlyObjects.Count());
    }

    private const int DoorIndex = 1;

    /// <summary>A base game navmesh the cleaned mod did not change.</summary>
    private static readonly RecordKey UnchangedNavmesh = new(BaseGame, 0x703);

    private static readonly Vector3 DoorOldPlace = new(3000, 500, RockHalfSize);
    private static readonly Vector3 DoorNow = DoorOldPlace + new Vector3(0, 300, 0);
    private static readonly RecordKey DoorKey = new FormKey(BaseGameMod, 0x900 + DoorIndex).ToRecordKey();

    /// <summary>A base game door the cleaned mod moved, whose navmesh link it changed as <paramref name="outcome"/> says.</summary>
    private static RecordChangedByCleanedMod MovedDoor(PartOutcome outcome, NavDoorLinkFact earlier, NavDoorLinkFact current) =>
        Rock(DoorIndex, DoorOldPlace, DoorNow) with { DoorLink = new DoorLinkChange(DoorKey, outcome, earlier, current, InDeletedCell: false) };

    private static Scene RockInTheNavmeshTakenBack() => new()
    {
        Rocks = [Rock(0, NavmeshNow + new Vector3(0, 0, RockHalfSize / 2), RockNowPlace)],
        Others = [LaterObject(10, House.Base, HousePlace), RockNow(0, RockNowPlace)],
        Navmeshes = [MovedNavmesh()],
    };

    private static string Describe(RestoreWhereRemovedResult result) => string.Join(
        Environment.NewLine,
        result.Objects.Select(judged => $"{judged.Change.Key} {judged.Refused} {judged.IsRestored}")
            .Concat(result.Cells.Select(cell => $"{cell.Change.Cell} {string.Join(",", cell.Undone.Select(part => part.Name))} {cell.WaterKeptBy} {cell.IsRestored}"))
            .Concat(result.Lands.Select(land =>
                $"{land.Change.Cell} {string.Join(",", land.Heights.Points)} {string.Join(";", land.Kept.Select(kept => $"{kept.Reason}:{string.Join(",", kept.Points.Points)}"))} {string.Join(",", land.FinalHeights ?? [])}"))
            .Concat(result.NavmeshGroups.Select(group => $"{string.Join(",", group.Members)} {group.Refused} {string.Join(",", group.Orders)}"))
            .Append($"{result.Passes}"));

    private static int PointAt(int x, int y) => y * Side + x;

    /// <summary>The cleaned mod's removed objects: at every quarter centre of cell (0, 0) and its neighbour to the east, and at the centre of cell (0, 0).</summary>
    private static List<TargetObject> RemovedTargets()
    {
        List<Vector3> places = [new(CellCentre, CellCentre, Raised)];
        for (var cellX = 0; cellX <= 1; cellX++)
        {
            foreach (var (quarterX, quarterY) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) })
            {
                places.Add(new Vector3(cellX * ExteriorGrid.CellSize + (2 * quarterX + 1) * QuarterCentre, (2 * quarterY + 1) * QuarterCentre, Raised));
            }
        }
        return [.. places.Select((place, index) => TestTargets.Create(index, TestTargets.At(place), Block.Base, TestTargets.Space))];
    }

    /// <summary>A base game object the cleaned mod moved, which no round removes, so it stands where it is.</summary>
    private static TargetObject StandingTarget(int index, Vector3 position) =>
        TestTargets.Create(index, TestTargets.At(position), Block.Base, TestTargets.Space) with { Origin = TargetOrigin.Moved };

    /// <summary>The key of the first object of <see cref="Scene.Standing"/>.</summary>
    private static RecordKey FirstStandingTargetKey() => TestTargets.Key(RemovedTargets().Count);

    /// <summary>The cleaned mod raised every point of the base game's flat ground of cell (0, 0).</summary>
    private static LandChangedByCleanedMod RaisedLand()
    {
        var all = PointSet.Of(Enumerable.Range(0, TerrainGrid.VertexCount));
        return new LandChangedByCleanedMod(
            new ExteriorCell(TestTargets.Space, 0, 0),
            new RecordKey(BaseGame, 0x600),
            all,
            PointSet.Of([]),
            default,
            UndoRest: false,
            new LandLeftToLater(PointSet.Of([]), PointSet.Of([]), default, Rest: false, []),
            TestTargets.TargetName,
            new LandHeights([.. Enumerable.Repeat(0f, TerrainGrid.VertexCount)], [.. Enumerable.Repeat(Raised, TerrainGrid.VertexCount)]));
    }

    /// <summary>Cell (0, 0), whose music and water level the cleaned mod changed: it lowered the water from 50 to −100.</summary>
    private static CellChangedByCleanedMod ChangedWater() => new(
        WaterCell,
        TestTargets.Space,
        (0, 0),
        [new PartChange(new CellPartName(CellPart.Music, null), new PartOutcome.Undo()), new PartChange(new CellPartName(CellPart.WaterLevel, null), new PartOutcome.Undo())],
        LinkedFromCleanedMod: false,
        TestTargets.TargetName,
        new CellWaterCopy(BaseGame, HasWater: true, EarlierWater),
        new CellWaterCopy(TestTargets.TargetName, HasWater: true, CleanedModsWater),
        DefaultWaterLevel: null);

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

    /// <summary>
    /// 144 base game rocks the cleaned mod moved a little and raised, every fifth with a later mod's crate next to its old place, with
    /// the house, the moved navmesh and the water cell.
    /// </summary>
    private static Scene ManyRocks()
    {
        const int perSide = 12;
        const float spacing = 300f;
        List<RecordChangedByCleanedMod> rocks = [];
        List<OtherObject> others = [LaterObject(1000, House.Base, HousePlace)];
        for (var index = 0; index < perSide * perSide; index++)
        {
            var oldPlace = new Vector3(spacing * (1 + index % perSide), spacing * (1 + index / perSide), RockHalfSize);
            var now = oldPlace + new Vector3(spacing / 2, 0, Raised);
            rocks.Add(Rock(index, oldPlace, now));
            others.Add(RockNow(index, now));
            if (index % 5 == 0) others.Add(LaterObject(2000 + index, Block.Base, oldPlace + new Vector3(0, RockHalfSize + TooCloseDistance - 5f, 0)));
        }
        return new Scene { Rocks = rocks, Others = others, Navmeshes = [MovedNavmesh()], Cells = [ChangedWater()] };
    }

    /// <summary>A base game navmesh, a flat square under the house, that the cleaned mod moved to <see cref="NavmeshNow"/>.</summary>
    private static NavmeshChangedByCleanedMod MovedNavmesh() => new(
        NavmeshKey,
        TestTargets.Space,
        NavmeshCell,
        TestTargets.TargetName,
        NavmeshCase.Edited,
        new PartOutcome.Undo(),
        new PartOutcome.Unchanged(),
        new PartOutcome.Unchanged(),
        Square(BaseGame, HousePlace with { Z = 0 }),
        Square(TestTargets.TargetName, NavmeshNow),
        ReplacedBy: [],
        Neighbours: [],
        Doors: [],
        NamedInPreferredPaths: false,
        WinningMapMod: null,
        CellDeletedInAVersion: false,
        LaterWinner: null,
        WinningMapEntry: null);

    private static NavmeshShape Square(PluginName plugin, Vector3 centre) => new(
        plugin,
        NavmeshCell,
        IsDeleted: false,
        [
            centre + new Vector3(-NavmeshHalfSize, -NavmeshHalfSize, 0), centre + new Vector3(NavmeshHalfSize, -NavmeshHalfSize, 0),
            centre + new Vector3(NavmeshHalfSize, NavmeshHalfSize, 0), centre + new Vector3(-NavmeshHalfSize, NavmeshHalfSize, 0),
        ],
        [new NavmeshTriangleFact(0, 1, 2, Preferred: false), new NavmeshTriangleFact(0, 2, 3, Preferred: false)],
        [],
        [],
        MapEntry: null);

    /// <summary>
    /// A base game rock the cleaned mod moved from <paramref name="oldPlace"/> to <paramref name="now"/>, whose position goes back;
    /// <paramref name="removedNow"/> when the cleaned mod also disabled it, so it stands nowhere now (see <see cref="Disabled"/>).
    /// </summary>
    private static RecordChangedByCleanedMod Rock(int index, Vector3 oldPlace, Vector3 now, bool removedNow = false) =>
        new(
            new FormKey(BaseGameMod, 0x900 + (uint)index).ToRecordKey(),
            EditorId: null,
            TestTargets.Space,
            new CellFact(TestTargets.Space, null),
            TestTargets.TargetName,
            new EulerPlacement(now, Vector3.Zero, 1f),
            Block.Base,
            TestPlacedRecords.PlainReference,
            IsPersistent: false,
            TestTargets.EarlierVersionAt(BaseGame, new EulerPlacement(oldPlace, Vector3.Zero, 1f), Block.Base) with { Undo = removedNow ? PlacedParts.Position | PlacedParts.Presence : PlacedParts.Position },
            Target: null,
            LinkedFromCleanedMod: false,
            PresenceBefore: default);

    /// <summary>The winning presence of records the game does not have.</summary>
    private static Dictionary<RecordKey, PlacedPresence> Disabled(params RecordKey[] records) =>
        records.ToDictionary(record => record, _ => new PlacedPresence(Removed: true, Parent: null, OppositeOfParent: false));

    /// <summary>The rock where the cleaned mod put it: its override is a change in the world without the cleaned mod.</summary>
    private static OtherObject RockNow(int index, Vector3 now) =>
        TestShapes.Placed(BaseGameMod, index, Block.Base, now) with { ChangeWithoutCleanedMod = new ObjectChange(TestTargets.TargetName, BaseGame) };

    private static OtherObject LaterObject(int index, BaseKey model, Vector3 position) =>
        TestShapes.Placed(LaterMod, index, model, position) with { ChangeWithoutCleanedMod = new ObjectChange(LaterMod.ToPluginName(), AcceptedFrom: null) };

    private static RestoreWhereRemovedResult Plan(Scene scene)
    {
        var removedTargets = RemovedTargets();
        ImmutableArray<TargetObject> targets = [.. removedTargets, .. scene.Standing.Select((position, index) => StandingTarget(removedTargets.Count + index, position))];
        var collected = TestScenes.CreateWorld(targets, [.. scene.Others.Select((other, index) => other with { Id = new OtherId(index) })]) with
        {
            ChangedRecords = [.. scene.Rocks],
            ChangedCells = [.. scene.Cells],
            ChangedLands = [.. scene.Lands],
            ChangedNavmeshes = [.. scene.Navmeshes],
            WinningPresence = scene.Presence,
            WinningPrimitives = scene.Primitives,
        };
        var rules = TestPlacedGuardRules.Create(Shapes, Trust, Plugins, Support, TestGround.NoTerrain(), scene.Workers);
        Func<CollectedObjects, IObjectCaches, IEnumerable<OtherId>, TooCloseSettings, IPlacementRule> tooClose = scene.TooCloseChecks is { } checks
            ? (world, caches, excluded, settings) => new CountingRule(rules.TooClose(world, caches, excluded, settings), checks)
            : rules.TooClose;
        ImmutableArray<NavmeshWorldTriangle> winning =
        [
            .. scene.Navmeshes.SelectMany(navmesh => Enumerable.Range(0, navmesh.Winner.Triangles.Length)
                .Select(triangle => new NavmeshWorldTriangle(navmesh.Navmesh, navmesh.Winner.CornersOf(triangle)!.Value))),
            .. scene.Winning,
        ];
        var final = TestSeededDecisions.StartDisablingAll(ObjectsToKeep.Build(targets, [], TestTargets.References(targets.Length), TestBaseFacts.Unresolved), targets.Length)
            .Apply(RoundKind.TooClose, [.. removedTargets.Select(target => new ProposedRemoval(target.Id, new RemovalReason.TooClose(new OtherId(0), new TooCloseCause.ObjectSize())))]);
        return WhereRemovedRestorer.Plan(
            new(
                collected,
                final,
                OtherModsObjectsResult.SwitchedOff,
                TestScenes.Create(targets, [], Shapes).VisibleTargets,
                rules.WorldCaches,
                rules.TooCloseRule(tooClose),
                rules.HoldersOfObjectsThatStay,
                new TestNavmeshCheck(scene.FindFault),
                rules.Support,
                Shapes,
                new NavmeshIndex(bucket => bucket.Grid is null && !winning.IsEmpty ? [.. winning] : null),
                new TestLandPlugin(space => space, scene.ReadHeights),
                Plugins,
                TestTargets.TargetName,
                new Execution(scene.Workers),
                scene.Settings));
    }

    /// <summary>What the cleaned mod changed besides its own removed objects, and the other plugins' objects that stand now, numbered in this order.</summary>
    private sealed record Scene
    {
        public List<RecordChangedByCleanedMod> Rocks { get; init; } = [];

        /// <summary>The places of base game objects the cleaned mod moved that no round removes, numbered after the removed objects.</summary>
        public List<Vector3> Standing { get; init; } = [];
        public List<OtherObject> Others { get; init; } = [];
        public List<LandChangedByCleanedMod> Lands { get; init; } = [RaisedLand()];
        public List<CellChangedByCleanedMod> Cells { get; init; } = [];
        public List<NavmeshChangedByCleanedMod> Navmeshes { get; init; } = [];

        /// <summary>The winning presence of the placed records the game does not have or that follow an enable parent.</summary>
        public Dictionary<RecordKey, PlacedPresence> Presence { get; init; } = new();

        /// <summary>The triangles of winning navmeshes the cleaned mod did not change.</summary>
        public List<NavmeshWorldTriangle> Winning { get; init; } = [];

        /// <summary>The heights of a cell's LAND record below the cleaned mod and at the winner, for cells without a LAND fact.</summary>
        public Func<ExteriorCell, LandHeights> ReadHeights { get; init; } = _ => new LandHeights(null, null);

        /// <summary>What the winning version of each primitive says about the reference itself.</summary>
        public Dictionary<RecordKey, ReferenceFacts> Primitives { get; init; } = new();

        /// <summary>The consistency check's first fault over what the given orders alone would leave; none by default.</summary>
        public Func<IReadOnlyList<WriteOrder>, NavmeshFault?> FindFault { get; init; } = _ => null;

        /// <summary>When set, collects every placed record the too-close rule checks, once per check.</summary>
        public ConcurrentQueue<RecordKey>? TooCloseChecks { get; init; }

        public WhereRemovedRestoreSettings Settings { get; init; } = AllOn;
        public int Workers { get; init; } = 1;
    }

    /// <summary>A too-close rule that notes each record it checks.</summary>
    private sealed class CountingRule(IPlacementRule rule, ConcurrentQueue<RecordKey> checks) : IPlacementRule
    {
        public IPlacementTries StartTries(TargetObject target)
        {
            checks.Enqueue(target.Key);
            return rule.StartTries(target);
        }
    }
}
