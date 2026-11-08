using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.Rules;

using static RestoreLoopScenes;

/// <summary>
/// The doors, the invisible blockers and the independence from the number of workers in the guards between the kinds that go back
/// where the cleaned mod was removed, repeated until nothing more changes, over in-memory scenes.
/// </summary>
public sealed class RestoreLoopDoorsAndWorkersTests
{
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
        var scene = SceneOfSeveralPasses();

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
            FindFault = trial => fault && trial.RestoredParts.Any(restored => restored.Record == door.Key)
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
        Assert.Equal((together, together), (judged.IsRestored, group.Write is not null));
        if (houseStands) Assert.Equal(new PlacedRefusal(PlacedGuard.DoorLinkDoesNotMatch, NavmeshKey, Failure: null), judged.Refused);
        if (crateAtTheDoor) Assert.Equal(new NavmeshRefusal(NavmeshGuard.Door, door.Key, Fault: null), group.Refused);
        Assert.False(group.Write is { } write && write.DoorLinksCopied.Concat(write.DoorLinksCleared).Contains(door.Key));
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
            FindFault = trial => trial.RestoredParts.Any(restored => restored.Record == DoorKey && restored.Parts.HasFlag(PlacedParts.Presence))
                ? null
                : new NavmeshFault(NavmeshKey, NavmeshRule.Doors, NavmeshFaultKind.DoorMissing, 0, DoorKey),
        };

        var result = Plan(scene);

        Assert.True(Assert.Single(result.Objects).IsRestored);
        Assert.NotNull(Assert.Single(result.NavmeshGroups).Write);
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
            FindFault = trial => new[] { first.Key, second.Key }.All(door => trial.RestoredParts.Any(restored => restored.Record == door && restored.Parts.HasFlag(PlacedParts.Presence)))
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
        var scene = BlockerInTheNavmeshsEarlierPlace(primitive, layer);

        Assert.Equal(refused ? new NavmeshRefusal(NavmeshGuard.Object, scene.Others[0].Key, Fault: null) : null, Assert.Single(Plan(scene).NavmeshGroups).Refused);
    }

    /// <summary>
    /// The collision layer records cannot be parsed: the step stops with that failure once the object guard first asks about the
    /// primitive's layer, and judges nothing, so the run goes on and the patch is written without anything of this step.
    /// </summary>
    [Fact]
    public void AMalformedCollisionLayerRecordTheObjectGuardMeetsStopsTheStepWithItsFailureAndJudgesNothing()
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var mod = TestCollisionLayers.AddTo(new SkyrimMod(ShapeMod, SkyrimRelease.SkyrimSE));
            var dataPath = TestShapes.AddStatics(mod, ShapeDataFolder, Statics);
            using var plugin = BrokenCollisionLayerData.WriteAndOpen(mod, folder);
            var scene = BlockerInTheNavmeshsEarlierPlace(primitive: true, (uint)CollisionLayer.CollisionBox) with
            {
                Shapes = TestVisibility.Over(TestShapes.Catalog(plugin.ToImmutableLinkCache(), dataPath, ShapeMod)),
            };

            var result = Plan(scene);

            Assert.Equal(StepOutcome.StoppedByUserDataFailure, result.Outcome);
            Assert.Empty(result.NavmeshGroups);
            var failure = Assert.Single(result.Failures);
            Assert.Equal(RestoreWhereRemovedParts.Step, failure.Part);
            Assert.StartsWith(nameof(MalformedRecordException), failure.Failure, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>A later mod's invisible wall, or a primitive of it on the given collision layer, stands in the navmesh's earlier place.</summary>
    private static Scene BlockerInTheNavmeshsEarlierPlace(bool primitive, uint? layer)
    {
        var blocker = primitive
            ? LaterObject(10, MarkerBase.Base, HousePlace with { Z = 0 }) with { IsPrimitive = true }
            : LaterObject(10, CollisionWall.Base, HousePlace with { Z = 0 });
        var facts = TestPlacedRecords.PlainReference with { IsPrimitive = true, PrimitiveBounds = new Vector3(2 * RockHalfSize), CollisionLayer = layer };
        return new Scene
        {
            Others = [blocker],
            Navmeshes = [MovedNavmesh()],
            Lands = [],
            Primitives = primitive ? new Dictionary<RecordKey, ReferenceFacts> { [blocker.Key] = facts } : new(),
        };
    }

    /// <summary>
    /// The placed guards answer the same over the objects within their reach as over every object: 144 rocks going back, every fifth
    /// next to a later mod's crate and every seventh carrying a later mod's block where it stands now, which refuses nothing, among
    /// later mods' blocks far away. Only the objects within reach are indexed.
    /// </summary>
    [Fact]
    public void ThePlacedGuardsAnswerTheSameOverTheObjectsWithinTheirReachAsOverEveryObject()
    {
        const float Everywhere = 20000f;
        var scene = ManyRocks();
        List<OtherObject> carried = [.. scene.Rocks.Where((_, index) => index % 7 == 0).Select((rock, index) => LaterObject(3000 + index, Block.Base, rock.WhereItIsNow!.Value.Position + new Vector3(0, 0, 2 * RockHalfSize)))];
        List<OtherObject> farAway = [.. Enumerable.Range(0, 50).Select(index => LaterObject(4000 + index, Block.Base, new Vector3(8000 + 50 * index, 6000, RockHalfSize)))];
        var everything = scene with { Others = [.. scene.Others, .. carried, .. farAway] };

        var (withinReach, indexedWithinReach) = CheckPlacedRecords(everything, AllOn.TooClose, Support.TouchGap);
        var (overEverything, indexedEverywhere) = CheckPlacedRecords(everything, AllOn.TooClose with { DistanceInUnits = Everywhere }, Everywhere);

        Assert.Equal(overEverything, withinReach);
        Assert.Contains(withinReach, refusal => refusal?.Guard == PlacedGuard.TooCloseToWhatStays);
        Assert.True(indexedWithinReach <= indexedEverywhere - farAway.Count);
    }

    /// <summary>The first pass of the placed guards over every rock of the scene going back, and how many objects their caches index.</summary>
    /// <param name="reachSettings">The too-close settings that set how far the guards look.</param>
    /// <param name="touchGap">The touch gap that sets how far the guards look.</param>
    private static (List<PlacedRefusal?> Found, int Indexed) CheckPlacedRecords(Scene scene, TooCloseSettings reachSettings, float touchGap)
    {
        var removedTargets = RemovedTargets();
        ImmutableArray<TargetObject> targets = [.. removedTargets];
        var collected = WorldOf(targets, scene.Others) with { ChangedRecords = [.. scene.Rocks] };
        var final = TestSeededDecisions.StartDisablingAll(ObjectsToKeep.Build(targets, [], TestTargets.References(targets.Length), TestBaseFacts.Unresolved), targets.Length)
            .Apply(RoundKind.TooClose, [.. removedTargets.Select(target => new ProposedRemoval(target.Id, new TooClose(new OtherId(0), new TooCloseCause.ObjectSize())))]);
        List<RecordChangedByCleanedMod> cleared = [.. scene.Rocks.OrderBy(rock => rock.Key, RecordKeyOrder.Comparer)];
        var rules = TestPlacedGuardRules.Create(Shapes, Trust, Plugins, Support, TestGround.NoTerrain(), threads: 1);
        var reach = new GuardReach(Shapes, rules.TooCloseToWhatStays, reachSettings, touchGap);
        var world = AfterRemovalWorld.Of(collected, final, OtherModsObjectsResult.SwitchedOff, TestTargets.TargetName, cleared, reach);
        var blocking = new ObjectsThatBlockActors(Shapes, new Dictionary<RecordKey, ReferenceFacts>());
        var presence = FinalPresence.Of(collected, removedTargets.Select(target => target.Key).ToHashSet(), cleared);
        var inputs = new PlacedGuardInputs(
            (checkedWorld, excluded) => rules.TooClose(checkedWorld, rules.WorldCaches.Of(checkedWorld), excluded, AllOn.TooClose),
            blocking,
            new NavmeshIndex(_ => null),
            CloneGuards.Of(world, blocking, [], [], new Execution(1)));
        var guards = new PlacedGuards(world, inputs, new DoorLinkGuard(cleared, [], _ => null), new Execution(1));
        var plans = new OtherKindsAsPlanned([], new HashSet<RecordKey>());
        var goingBack = Enumerable.Range(0, cleared.Count).ToHashSet();
        var (withClone, pass) = (new HashSet<int>(), new PresenceInPass(presence, goingBack.Contains));
        var followUp = new FollowUpOfGoingBack(rules.FollowUpOver(collected, TestPlacedGuardRules.DefaultFollowUp), final, OtherModsObjectsResult.SwitchedOff.TakenAway(), cleared, collected, landUnder: (_, _) => null);
        var followUpChecks = followUp.Check(goingBack, withClone, pass, land: null);
        var standing = StandingTooCloseChecks.Of(collected, OtherModsObjectsResult.SwitchedOff, world, cleared, [], new HashSet<int>(), TestTargetGroups.None.TooClose, TestTargetGroups.None.CleanedModsObjects.TooClose, presence, rules.TooCloseToWhatStays, new Execution(1))
            .Check(goingBack, withClone, pass, followUpChecks);
        var found = guards.CheckGoingBack(goingBack, withClone, plans, pass, followUpChecks, standing).Found;
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

    private static string Describe(RestoreWhereRemovedResult result) => string.Join(
        Environment.NewLine,
        result.Objects.Select(judged => $"{judged.Change.Key} {judged.Refused} {judged.IsRestored}")
            .Concat(result.Cells.Select(cell => $"{cell.Change.Cell} {string.Join(",", cell.Undone.Select(part => part.Name))} {cell.WaterKeptBy} {cell.IsRestored}"))
            .Concat(result.Lands.Select(land =>
                $"{land.Change.Cell} {string.Join(",", land.Heights.Points)} {string.Join(";", land.Kept.Select(kept => $"{kept.Reason}:{string.Join(",", kept.Points.Points)}"))} {string.Join(",", land.FinalHeights ?? [])}"))
            .Concat(result.NavmeshGroups.Select(group => $"{string.Join(",", group.Members)} {group.Refused} {NavmeshWriteDescription.Of(group.Write)}"))
            .Append($"{result.Passes}"));

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
}
