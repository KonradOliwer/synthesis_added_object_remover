using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Rules;

using static RestoreLoopScenes;

/// <summary>
/// The refusals in the guards between the kinds that go back where the cleaned mod was removed, repeated until nothing more changes,
/// over in-memory scenes: rocks refused for the ground, doors, enable parents and too-close targets, and the queries behind them.
/// </summary>
public sealed class RestoreLoopRefusalTests
{
    /// <summary>
    /// The rock's old place lies on the flat ground the cleaned mod raised, which goes back when the terrain is switched on and stays
    /// when off: then the rock would go back into the cleaned mod's ground, so it is refused, trusted or not.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void ARockGoingBackOntoGroundTheCleanedModRaisedIsRefusedOnlyWhenTheGroundStays(bool terrainOn, bool baseGameTrusted)
    {
        var scene = new Scene
        {
            Rocks = [Rock(0, new Vector3(1500, 1500, RockHalfSize), RockNowPlace)],
            Others = [RockNow(0, RockNowPlace)],
            Ground = RaisedGround(),
            Trust = baseGameTrusted ? BaseGameTrusted : Trust,
            Settings = AllOn with { Terrain = terrainOn },
        };

        var rock = Assert.Single(Plan(scene).Objects);

        Assert.Equal(terrainOn ? (true, null) : (false, PlacedGuard.ArrivesInGround), (rock.IsRestored, rock.Refused?.Guard));
    }

    /// <summary>
    /// The cleaned mod lowered cell (0, 0) from 80 units to flat ground, which stays while the terrain is switched off, and the rock's old
    /// place stood on the ground before. Going back, it would float over the cleaned mod's ground: a base game rock, trusted in full, is
    /// pulled down onto it or removed where it arrives; an untrusted one is refused.
    /// </summary>
    [Theory]
    [InlineData(false, OtherModsObjectsHandling.PullObjectToTheGround)]
    [InlineData(true, OtherModsObjectsHandling.PullObjectToTheGround)]
    [InlineData(true, OtherModsObjectsHandling.RemoveFloatingObject)]
    public void ARockGoingBackOverGroundTheCleanedModLoweredIsPulledDownRemovedOrRefused(bool baseGameTrusted, OtherModsObjectsHandling handling)
    {
        var rockNow = RockNowPlace with { Z = RockHalfSize };
        var scene = new Scene
        {
            Rocks = [Rock(0, new Vector3(1500, 1500, Raised + RockHalfSize), rockNow)],
            Others = [RockNow(0, rockNow)],
            Lands = [RaisedLand() with { Heights = new LandHeights(HeightsAll(Raised), HeightsAll(0f)) }],
            Ground = GroundAll(0f),
            Trust = baseGameTrusted ? BaseGameTrusted : Trust,
            Support = Support with { Handling = handling },
            Settings = AllOn with { Terrain = false },
        };

        var result = Plan(scene);

        var rock = Assert.Single(result.Objects);
        if (!baseGameTrusted)
        {
            Assert.Equal((false, PlacedGuard.ArrivesWithoutSupport), (rock.IsRestored, rock.Refused?.Guard));
            return;
        }
        Assert.Equal((true, null), (rock.IsRestored, rock.Refused));
        var followed = Assert.Single(result.FollowUp.Others);
        Assert.Equal(rock.Change.Key, followed.Outcome.Object.Key);
        if (handling == OtherModsObjectsHandling.RemoveFloatingObject) Assert.True(followed.Outcome.IsRemoved);
        else Assert.Equal((OtherModsObjectFate.PulledDown, RockHalfSize), (followed.Outcome.Fate, MathF.Round(followed.Outcome.NewPosition.Z)));
    }

    /// <summary>
    /// A block the cleaned mod moved, which no round removes, stays on the raised ground and keeps the ground under it (decision 29), and
    /// a base game rock the cleaned mod moved goes back beside it, onto the slope from the kept point down to the flat ground going back:
    /// it would go back into the cleaned mod's ground, so it is refused, trusted or not.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARockGoingBackBesideACleanedModsObjectThatKeepsItsGroundIsRefused(bool baseGameTrusted)
    {
        var standing = new Vector3(12 * TerrainGrid.VertexSpacing, 12 * TerrainGrid.VertexSpacing, Raised + RockHalfSize);
        var scene = new Scene
        {
            Rocks = [Rock(0, standing with { X = standing.X + 60f, Z = RockHalfSize }, RockNowPlace)],
            Others = [RockNow(0, RockNowPlace)],
            Standing = [standing],
            Ground = RaisedGround(),
            Trust = baseGameTrusted ? BaseGameTrusted : Trust,
        };

        var result = Plan(scene);

        var land = Assert.Single(result.Lands);
        Assert.Contains(land.Kept, kept => kept.Reason == TerrainKeptReason.UnderObjectThatStays && kept.Points.Contains(PointAt(12, 12)));
        Assert.Equal((false, PlacedGuard.ArrivesInGround), (Assert.Single(result.Objects).IsRestored, result.Objects[0].Refused?.Guard));
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
        Assert.NotNull(Assert.Single(result.NavmeshGroups).Write);
    }

    /// <summary>
    /// A later mod's crate refuses the rock, which stands now in the navmesh's earlier place: the group is refused in the pass after,
    /// the first in which the rock stays.
    /// </summary>
    [Fact]
    public void ARockRefusedInOnePassRefusesTheNavmeshGroupWhoseEarlierPlaceItStandsInInTheNext()
    {
        var oldPlace = new Vector3(1500, 1500, RockHalfSize);
        var rockNow = new Vector3(1500, 2500, Raised + RockHalfSize);
        var rock = Rock(0, oldPlace, rockNow);
        var scene = new Scene
        {
            Rocks = [rock],
            Others = [RockNow(0, rockNow), .. CrateBesideTheRock(oldPlace, present: true)],
            Navmeshes = [MovedNavmesh() with { Earlier = Square(BaseGame, rockNow) }],
            Lands = [],
        };

        var result = Plan(scene);

        Assert.Equal(PlacedGuard.TooCloseToWhatStays, Assert.Single(result.Objects).Refused?.Guard);
        Assert.Equal(new NavmeshRefusal(NavmeshGuard.Object, rock.Key, Fault: null), Assert.Single(result.NavmeshGroups).Refused);
    }

    /// <summary>
    /// The trial write meets a record the game library cannot parse: the step stops with that failure and judges nothing, so the run
    /// goes on and the patch is written without anything of this step.
    /// </summary>
    [Fact]
    public void AMalformedRecordTheTrialWriteMeetsStopsTheStepWithItsFailureAndJudgesNothing()
    {
        var scene = new Scene { Navmeshes = [MovedNavmesh()], FindFault = _ => throw new MalformedRecordException(new ArgumentOutOfRangeException()) };

        var result = Plan(scene);

        Assert.Equal(StepOutcome.StoppedByUserDataFailure, result.Outcome);
        Assert.Empty(result.Objects);
        Assert.Empty(result.NavmeshGroups);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(RestoreWhereRemovedParts.Step, failure.Part);
        Assert.StartsWith(nameof(MalformedRecordException), failure.Failure, StringComparison.Ordinal);
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

    /// <summary>The base game disabled the rock, and the cleaned mod enabled it and moved it: its restore moves it back and disables it again.</summary>
    private static RecordChangedByCleanedMod WithPresenceRestoredToDisabled(RecordChangedByCleanedMod rock) =>
        rock with { Earlier = rock.Earlier with { Undo = PlacedParts.Position | PlacedParts.Presence }, PresenceBefore = new PlacedPresence(Removed: true, Parent: null, OppositeOfParent: false) };

    /// <summary>A target of the cleaned mod's run that stays, judged by the too-close step, away from the rocks' old places.</summary>
    private static readonly Vector3 StandingPlace = new(3000, 3000, RockHalfSize);

    private static readonly Vector3 RockAOldPlace = new(1500, 1500, RockHalfSize);
    private static readonly Vector3 RockANow = new(1500, 2500, Raised + RockHalfSize);
    private static readonly Vector3 RockBOldPlace = new(1500, 3800, RockHalfSize);
    private static readonly Vector3 RockBNow = new(500, 3500, Raised + RockHalfSize);

    /// <summary>A later mod's object set to the opposite of its enable parent, at the given place.</summary>
    private static (OtherObject Object, KeyValuePair<RecordKey, PlacedPresence> Presence) OppositeOf(RecordKey parent, int index, Vector3 position)
    {
        var placed = LaterObject(index, Block.Base, position);
        return (placed, KeyValuePair.Create(placed.Key, new PlacedPresence(Removed: false, parent, OppositeOfParent: true)));
    }

    /// <summary>
    /// A later mod's object X is the opposite of rock A, which the base game disabled and the cleaned mod enabled: A's restore disables
    /// it again, so X stands then, beside a target that stays and that the too-close step judges. The next patcher would remove the
    /// target, so A is refused, naming the target, and X stays disabled. When another later object already stands too close to the
    /// target, the target is removed anyway and A goes back.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARockWhoseRestoreEnablesAnObjectTooCloseToATargetThatStaysIsRefused(bool alreadyTooClose)
    {
        var rock = WithPresenceRestoredToDisabled(Rock(0, RockAOldPlace, RockANow));
        var x = OppositeOf(rock.Key, 11, StandingPlace + new Vector3(25, 0, 0));
        var scene = new Scene
        {
            Rocks = [rock],
            Others = [RockNow(0, RockANow), x.Object, .. alreadyTooClose ? [LaterObject(12, Block.Base, StandingPlace - new Vector3(25, 0, 0))] : new List<OtherObject>()],
            Standing = [StandingPlace],
            StandingTooClose = AllOn.TooClose,
            Lands = [],
            Presence = new Dictionary<RecordKey, PlacedPresence>([x.Presence]),
        };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal(alreadyTooClose ? null : new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, FirstStandingTargetKey(), Failure: null), judged.Refused);
    }

    /// <summary>
    /// As <see cref="ARockWhoseRestoreEnablesAnObjectTooCloseToATargetThatStaysIsRefused"/>, but X is the opposite of rock A, which
    /// follows rock B, and both A's and B's restores disable them: X stands when either goes back, so leaving out one alone does not
    /// remove it. Both are refused, one pass after the other.
    /// </summary>
    [Fact]
    public void RocksThatEachEnableAnObjectTooCloseToATargetThatStaysAreAllRefused()
    {
        var rockB = WithPresenceRestoredToDisabled(Rock(1, RockBOldPlace, RockBNow));
        var rockA = WithPresenceRestoredToDisabled(Rock(0, RockAOldPlace, RockANow));
        var x = OppositeOf(rockA.Key, 11, StandingPlace + new Vector3(25, 0, 0));
        var scene = new Scene
        {
            Rocks = [rockA, rockB],
            Others = [RockNow(0, RockANow), RockNow(1, RockBNow), x.Object],
            Standing = [StandingPlace],
            StandingTooClose = AllOn.TooClose,
            Lands = [],
            Presence = new Dictionary<RecordKey, PlacedPresence>([x.Presence, KeyValuePair.Create(rockA.Key, new PlacedPresence(Removed: false, rockB.Key, OppositeOfParent: false))]),
        };

        var result = Plan(scene);

        Assert.All(result.Objects, judged => Assert.Equal(new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, FirstStandingTargetKey(), Failure: null), judged.Refused));
        Assert.Equal(2, result.Objects.Length);
    }

    /// <summary>
    /// Rock A's restore enables X beside the target that stays, and rock B's restore enables Y far from it. The target's too-close
    /// check stops, with nothing going back too: A, whose X lies within the target's zones, is refused with the failure; B goes back.
    /// </summary>
    [Fact]
    public void AStoppedCheckOfATargetThatStaysRefusesOnlyTheRocksEnablingObjectsWithinItsReach()
    {
        var rockA = WithPresenceRestoredToDisabled(Rock(0, RockAOldPlace, RockANow));
        var rockB = WithPresenceRestoredToDisabled(Rock(1, RockBOldPlace, RockBNow));
        var x = OppositeOf(rockA.Key, 11, StandingPlace + new Vector3(25, 0, 0));
        var y = OppositeOf(rockB.Key, 12, new Vector3(3000, 500, RockHalfSize));
        var scene = new Scene
        {
            Rocks = [rockA, rockB],
            Others = [RockNow(0, RockANow), RockNow(1, RockBNow), x.Object, y.Object],
            Standing = [StandingPlace],
            StandingTooClose = AllOn.TooClose,
            TooCloseStopsFor = FirstStandingTargetKey(),
            Lands = [],
            Presence = new Dictionary<RecordKey, PlacedPresence>([x.Presence, y.Presence]),
        };

        var result = Plan(scene);

        Assert.Equal(
            [new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, By: null, StoppedCheck), null],
            result.Objects.OrderBy(judged => judged.Change.Key, RecordKeyOrder.Comparer).Select(judged => judged.Refused));
    }

    /// <summary>One more pass from the loop's end state adds nothing: the end state is a fixed point.</summary>
    [Fact]
    public void OneMorePassFromTheEndStateAddsNothing()
    {
        var planned = WhereRemovedRestorer.BuildPlans(InputOf(SceneOfSeveralPasses()));
        var guards = RestoreGuardsBuilder.Build(planned.Inputs, planned.Plans);
        var loop = new RestoreLoop(guards.Placed, guards.Terrain, guards.LoopInputs, () => TimeSpan.Zero);

        var (final, _, passes, _, _) = loop.Run(planned.Plans.Start);
        var (again, _, passesAgain, _, _) = loop.Run(final);

        Assert.True(passes > 1);
        Assert.Equal((1, final.Count), (passesAgain, again.Count));
        Assert.Equal(final.Placed, again.Placed);
        Assert.Equal(final.Clones, again.Clones);
    }

    /// <summary>A rock of the cleaned mod at <see cref="StandingPlace"/>, judged in a world of later mods' objects.</summary>
    private static (TestPlacedGuardRules Rules, CollectedObjects World, TargetObject Target) RuleWorld(params Vector3[] others)
    {
        var target = StandingTarget(0, StandingPlace);
        var world = TestScenes.CreateWorld([target], [.. others.Select((position, index) => LaterObject(index, Block.Base, position) with { Id = new OtherId(index) })]);
        return (TestPlacedGuardRules.Create(Shapes, Trust, Plugins, Support, TestGround.NoTerrain(), threads: 1), world, target);
    }

    [Fact]
    public void TheEveryClashQueryFindsEveryCauseTheObjectIsTooCloseToButThoseLeftOut()
    {
        var (rules, world, target) = RuleWorld(StandingPlace + new Vector3(25, 0, 0), StandingPlace + new Vector3(500, 0, 0), StandingPlace - new Vector3(25, 0, 0));
        var rule = rules.TooCloseToWhatStays;

        Assert.Equal(new TooCloseClashes([new OtherId(0), new OtherId(2)], Failure: null), rule.FindEveryClash(world, [], AllOn.TooClose, target), TooCloseClashesComparer);
        Assert.Equal(new TooCloseClashes([new OtherId(2)], Failure: null), rule.FindEveryClash(world, [new OtherId(0)], AllOn.TooClose, target), TooCloseClashesComparer);
    }

    [Fact]
    public void TheEveryClashQueryNamesTheFailureOfACheckThatStops()
    {
        var (rules, world, target) = RuleWorld(StandingPlace + new Vector3(25, 0, 0));
        var rule = rules.TooCloseRule((checkedWorld, caches, excluded, settings) => new StoppingForRule(rules.TooClose(checkedWorld, caches, excluded, settings), target.Key));

        Assert.Equal(new TooCloseClashes([], StoppedCheck), rule.FindEveryClash(world, [], AllOn.TooClose, target), TooCloseClashesComparer);
    }

    /// <summary>The zone reach holds the distance zone around the box, and the reach queries find what it overlaps, and every placed NPC.</summary>
    [Fact]
    public void TheReachQueriesFindWhatTheZonesReach()
    {
        var (rules, world, target) = RuleWorld(StandingPlace + new Vector3(25, 0, 0), StandingPlace + new Vector3(500, 0, 0));
        var rule = rules.TooCloseToWhatStays;
        var far = StandingTarget(1, StandingPlace + new Vector3(0, 2000, 0));
        var npc = world.OtherModObjects[1] with { Base = Block.Base with { Kind = BaseLinkKind.Npc } };

        var reach = rule.ZoneReachOf(target, AllOn.TooClose);

        Assert.True(reach.Contains(StandingPlace + new Vector3(RockHalfSize + TooCloseDistance)) && reach.Contains(StandingPlace - new Vector3(RockHalfSize + TooCloseDistance)));
        Assert.False(reach.Contains(StandingPlace + new Vector3(500, 0, 0)));
        Assert.Equal([0], rule.FindObjectsReaching([(target, AllOn.TooClose), (far, AllOn.TooClose)], [world.OtherModObjects[0]]).ToList());
        Assert.Equal([0, 1], rule.FindObjectsReaching([(target, AllOn.TooClose), (far, AllOn.TooClose)], [npc]).ToList());
        Assert.Equal([0], rule.FindOthersReached([(target, AllOn.TooClose)], [.. world.OtherModObjects]).ToList());
        Assert.Equal([0, 1], rule.FindOthersReached([(target, AllOn.TooClose)], [world.OtherModObjects[0], npc]).ToList());
    }

    private static readonly IEqualityComparer<TooCloseClashes> TooCloseClashesComparer =
        EqualityComparer<TooCloseClashes>.Create((one, other) => one!.Others.SequenceEqual(other!.Others) && one.Failure == other.Failure);

    /// <summary>The winning presence of a record enabled with a parent, which a base game plugin's disable keeps disabled when <paramref name="parentDisabled"/>.</summary>
    private static Dictionary<RecordKey, PlacedPresence> FollowingAParent(RecordKey record, bool parentDisabled, bool oppositeOfParent = false)
    {
        var parent = new RecordKey(BaseGame, 0x951);
        var presence = parentDisabled ? Disabled(parent) : new Dictionary<RecordKey, PlacedPresence>();
        presence.Add(record, new PlacedPresence(Removed: false, parent, oppositeOfParent));
        return presence;
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
    /// Rock C is refused next to a later mod's crate, so it stays where it stands now, next to rock D's old place, and refuses D in the
    /// pass after. Rock E far away goes back.
    /// </summary>
    [Fact]
    public void ARockRefusedInOnePassRefusesTheRockWhoseOldPlaceItStandsBesideInTheNext()
    {
        var cOldPlace = new Vector3(1500, 1500, RockHalfSize);
        var cNow = new Vector3(1500, 2500, Raised + RockHalfSize);
        var dOldPlace = cNow + new Vector3(RockHalfSize + TooCloseDistance - 5f, 0, 0);
        var eOldPlace = new Vector3(3000, 3000, RockHalfSize);
        var scene = new Scene
        {
            Rocks = [Rock(0, cOldPlace, cNow), Rock(1, dOldPlace, new Vector3(2500, 500, Raised)), Rock(2, eOldPlace, new Vector3(3200, 3000, Raised))],
            Others = [.. CrateBesideTheRock(cOldPlace, present: true), RockNow(0, cNow), RockNow(1, new Vector3(2500, 500, Raised)), RockNow(2, new Vector3(3200, 3000, Raised))],
            Lands = [],
        };

        var result = Plan(scene);

        Assert.Equal(new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, scene.Rocks[0].Key, Failure: null), result.Objects[1].Refused);
        Assert.Null(result.Objects[2].Refused);
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
}
