using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using NiflySharp.Enums;
using Noggog;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>The TooCloseObjects facade gives the same hits, work and NPC summary as the searches it wraps, for every zone and NPC setting.</summary>
public class TooCloseObjectsTests
{
    private const int Threads = 4;
    private const float Multiplier = 1.5f;
    private const float Distance = 40f;
    private const int TargetCount = 60;
    private const int OtherModObjectCount = 90;
    private const int InvisibleEvery = 5;
    private const int ObjectOtherModEvery = 3;
    private const float SceneExtent = 600f;
    private const int NpcHalfWidth = 15;
    private const int NpcHeight = 60;

    private static readonly ModKey Mod = ModKey.FromNameAndExtension("Clashes.esp");
    private static readonly FormKey Race = new(Mod, 0x601);
    private static readonly FormKey Npc = new(Mod, 0x602);
    private static readonly FormKey MeshNotFound = new(Mod, 0x706);

    private static readonly TestStatic Table = new(
        new FormKey(Mod, 0x701), @"test\table.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20, -20, 0), new Vector3(20, 20, 10))));

    private static readonly TestStatic Boulder = new(
        new FormKey(Mod, 0x703), @"test\boulder.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-50, -50, -20), new Vector3(50, 50, 40))));

    private static readonly TestStatic CollidingBoulder = new(
        new FormKey(Mod, 0x704), @"test\collidingboulder.nif", Boulder.Triangles, SkyrimLayer.SKYL_STATIC);

    private static readonly TestStatic Fern = new(
        new FormKey(Mod, 0x705), @"test\fern.nif", Boulder.Triangles, SkyrimLayer.SKYL_NONCOLLIDABLE);

    private const string UnreadableName = "unreadable";

    private static readonly TestStatic Unreadable = new(new FormKey(Mod, 0x707), $@"test\{UnreadableName}.nif", Table.Triangles);

    private const string BrokenPartsName = "brokenparts";

    /// <summary>A table whose mesh, as read by <see cref="WithBrokenParts"/>, throws once its parts are tested for lying inside another mesh.</summary>
    private static readonly TestStatic BrokenParts = new(new FormKey(Mod, 0x708), $@"test\{BrokenPartsName}.nif", Table.Triangles);

    /// <summary>The tried table's x in <see cref="CreateSideBySideScene"/>: where it touches only B, both, only A, or neither.</summary>
    private const float OnlyB = 110f;
    private const float OnBoth = 90f;
    private const float OnlyA = 50f;
    private const float OnNeither = -100f;
    private const float TableAX = 60f;
    private const float TableBX = 100f;

    /// <summary>
    /// The distance zone in <see cref="CreateSideBySideScene"/>, and the tried table's spots near it (every table is 40 units
    /// square). At y <see cref="BesideY"/> the table is 7 units beside A and B along Y: at x <see cref="BesideBothX"/> it
    /// spans both; at <see cref="BesideOnlyBX"/> its corner is 12 units from A's; at <see cref="BesideAOnlyX"/> its corner is
    /// 11 units from B's, while the zone's box, grown 10 units each way, still overlaps B's box, so B's mesh is tested. At x
    /// <see cref="RightOfBX"/> and y 0 it touches B's right side.
    /// </summary>
    private const float NearDistance = 10f;
    private const float BesideY = 47f;
    private const float BesideBothX = 80f;
    private const float BesideOnlyBX = 110f;
    private const float BesideAOnlyX = 51f;
    private const float RightOfBX = 140f;

    private static readonly OtherId TableA = new(0);
    private static readonly OtherId TableB = new(1);

    /// <summary>The zone is the object's own mesh, so a try clashes only with what its mesh really reaches.</summary>
    private static readonly TooCloseSettings SizeOnlyMeshZone =
        new(BySize: true, Multiplier: 0f, ByDistance: false, DistanceInUnits: 0f, ZoneShape.ObjectShape, NpcHandling.Never);

    /// <summary>The zone is the object's own mesh grown by <see cref="NearDistance"/>, so a try clashes with what comes that close.</summary>
    private static readonly TooCloseSettings DistanceOnlyMeshZone =
        new(BySize: false, Multiplier: 0f, ByDistance: true, DistanceInUnits: NearDistance, ZoneShape.ObjectShape, NpcHandling.Never);

    private static readonly SkyrimMod Records = CreateRecords();
    private static readonly IBaseObjectShapes Shapes = TestVisibility.Over(TestShapes.Create(Records, "ClashesData", Table, Boulder, CollidingBoulder, Fern, Unreadable, BrokenParts));

    private sealed record TooCloseScene(List<TargetObject> Targets, List<OtherObject> OtherModObjects);

    [Theory]
    [InlineData(ZoneShape.BoundingBox, NpcHandling.AlwaysLikeAnyObject)]
    [InlineData(ZoneShape.BoundingBox, NpcHandling.OnlyWhenStuckInsideTheObject)]
    [InlineData(ZoneShape.BoundingBox, NpcHandling.OnlyWhenStuckInsideTheObjectWithCollision)]
    [InlineData(ZoneShape.BoundingBox, NpcHandling.Never)]
    [InlineData(ZoneShape.ObjectShape, NpcHandling.AlwaysLikeAnyObject)]
    [InlineData(ZoneShape.ObjectShape, NpcHandling.OnlyWhenStuckInsideTheObject)]
    [InlineData(ZoneShape.ObjectShape, NpcHandling.OnlyWhenStuckInsideTheObjectWithCollision)]
    [InlineData(ZoneShape.ObjectShape, NpcHandling.Never)]
    public void FindGivesTheSameResultAsTheSearches(ZoneShape zone, NpcHandling npcs)
    {
        var scene = CreateScene();
        var settings = new TooCloseSettings(BySize: true, Multiplier, ByDistance: false, DistanceInUnits: 0f, zone, npcs);

        var expected = FindWithSearches(scene, settings);
        var actual = FindWithFacade(scene, settings);

        Assert.NotEmpty(expected.Search.Hits);
        Assert.Equal(expected.Search.Hits, actual.Hits);
        Assert.Equal(expected.Search.Work, actual.Work);
        Assert.Equal(expected.Search.Hits.Select(hit => hit.TargetIndex), actual.Proposals.Select(proposal => proposal.Target.Index));
        Assert.Equal(expected.LargeOtherObjects, actual.LargeOtherObjects);
        Assert.Equal(expected.Summary is null, actual.Npc is null);
        if (expected.Summary is { } summary) Assert.Equal(Describe(summary), Describe(actual.Npc!));
    }

    [Theory]
    [InlineData(ZoneShape.BoundingBox, NpcHandling.AlwaysLikeAnyObject)]
    [InlineData(ZoneShape.ObjectShape, NpcHandling.OnlyWhenStuckInsideTheObject)]
    public void ThePlacementRuleAtAnObjectsOwnPlacementGivesTheSameReasonAsFind(ZoneShape zone, NpcHandling npcs)
    {
        var scene = WithStoredHeadings(CreateScene());
        var settings = new TooCloseSettings(BySize: true, Multiplier, ByDistance: true, DistanceInUnits: Distance, zone, npcs);
        var rule = CreatePlacementRule(scene, settings);

        var found = FindWithFacade(scene, settings).Proposals.ToDictionary(proposal => proposal.Target, proposal => proposal.Reason);

        Assert.NotEmpty(found);
        Assert.All(scene.Targets, target => Assert.Equal(found.GetValueOrDefault(target.Id), CheckOnce(rule, target.Id, target.Placement).Clash));
    }

    [Fact]
    public void ThePlacementRuleJudgesTheObjectWhereItIsPlaced()
    {
        var scene = CreateScene();
        var settings = new TooCloseSettings(BySize: true, Multiplier, ByDistance: false, DistanceInUnits: 0f, ZoneShape.ObjectShape, NpcHandling.Never);
        var rule = CreatePlacementRule(scene, settings);
        var hit = FindWithFacade(scene, settings).Proposals[0].Target;
        var farAway = new EulerPlacement(new Vector3(-10 * SceneExtent, 0, 0), Vector3.Zero, 1f);

        Assert.Equal(new PlacementCheck(null, null), CheckOnce(rule, hit, farAway));
    }

    [Fact]
    public void ThePlacementRuleFindsNothingWhenNoZoneIsOn()
    {
        var scene = CreateScene();
        var rule = CreatePlacementRule(scene, new TooCloseSettings(BySize: false, Multiplier, ByDistance: false, DistanceInUnits: 0f, ZoneShape.ObjectShape, NpcHandling.AlwaysLikeAnyObject));

        Assert.All(scene.Targets, target => Assert.Null(CheckOnce(rule, target.Id, target.Placement).Clash));
    }

    [Theory]
    [InlineData(ZoneShape.BoundingBox)]
    [InlineData(ZoneShape.ObjectShape)]
    public void FindIsSwitchedOffAndLooksAtNothingWhenNoZoneIsOn(ZoneShape zone)
    {
        var settings = new TooCloseSettings(BySize: false, Multiplier, ByDistance: false, DistanceInUnits: Distance, zone, NpcHandling.OnlyWhenStuckInsideTheObject);

        var result = FindWithFacade(CreateScene(), settings);

        Assert.Equal(StepOutcome.SwitchedOff, result.Outcome);
        Assert.Empty(result.Hits);
        Assert.Empty(result.Proposals);
        Assert.Empty(result.Failures);
        Assert.Equal(default, result.Work);
        Assert.Null(result.Npc);
        Assert.Equal(0, result.LargeOtherObjects);
    }

    [Theory]
    [InlineData(ZoneShape.BoundingBox)]
    [InlineData(ZoneShape.ObjectShape)]
    public void AStuckNpcIsFoundByTheObjectsOwnSizeWhicheverZonesAreOn(ZoneShape zone)
    {
        var settings = new TooCloseSettings(BySize: false, Multiplier, ByDistance: true, DistanceInUnits: 0f, zone, NpcHandling.OnlyWhenStuckInsideTheObject);

        var result = FindWithFacade(CreateScene(), settings);

        var npcHits = result.Hits.Where(hit => hit.TooCloseTo.IsPlacedNpc).ToList();
        Assert.NotEmpty(npcHits);
        Assert.All(npcHits, hit => Assert.Equal(new TooCloseCause.ObjectSize(), hit.Cause));
        Assert.All(result.Hits.Where(hit => !hit.TooCloseTo.IsPlacedNpc), hit => Assert.Equal(new TooCloseCause.Distance(0f), hit.Cause));
    }

    /// <summary>
    /// An NPC stands in each of a boulder with blocking collision, a fern-like object of the same shape with only non-colliding
    /// collision, a boulder whose mesh has no collision at all, and a boulder whose mesh is not found, so its collision is unknown.
    /// </summary>
    [Theory]
    [InlineData(ZoneShape.BoundingBox)]
    [InlineData(ZoneShape.ObjectShape)]
    public void WithCollisionRequiredAStuckNpcRemovesOnlyObjectsWhoseCollisionStopsCharacters(ZoneShape zone)
    {
        var scene = CreateStuckNpcScene();
        var settings = new TooCloseSettings(BySize: true, Multiplier: 0f, ByDistance: false, DistanceInUnits: 0f, zone, NpcHandling.OnlyWhenStuckInsideTheObjectWithCollision);

        var withCollision = FindWithFacade(scene, settings);
        var anyObject = FindWithFacade(scene, settings with { Npcs = NpcHandling.OnlyWhenStuckInsideTheObject });

        Assert.Equal([0, 3], withCollision.Hits.Select(hit => hit.TargetIndex));
        Assert.Equal([0, 1, 2, 3], anyObject.Hits.Select(hit => hit.TargetIndex));
        Assert.Equal((4, 2), (withCollision.Npc!.Conflicts, withCollision.Npc.ConflictsWithoutBlockingCollision));
        Assert.Equal((4, 0), (anyObject.Npc!.Conflicts, anyObject.Npc.ConflictsWithoutBlockingCollision));
    }

    [Fact]
    public void TheResultNamesTheZonesItTestedInOrder()
    {
        var both = new TooCloseSettings(BySize: true, Multiplier, ByDistance: true, Distance, ZoneShape.BoundingBox, NpcHandling.Never);

        var result = FindWithFacade(CreateScene(), both);

        TooCloseCause[] expected = [new TooCloseCause.ObjectSize(), new TooCloseCause.Distance(Distance)];
        Assert.Equal(expected, result.ZonesTested.ToArray());
        Assert.Empty(FindWithFacade(CreateScene(), both with { BySize = false, ByDistance = false }).ZonesTested);
    }

    /// <summary>A target without mesh triangles uses its box in each zone it is tested with, and still counts once.</summary>
    [Fact]
    public void WithBothZonesOnEachTargetWithoutMeshTrianglesCountsOnce()
    {
        var scene = CreateScene();
        var sizeOnly = new TooCloseSettings(BySize: true, Multiplier: 0f, ByDistance: false, Distance, ZoneShape.ObjectShape, NpcHandling.Never);
        var withoutMesh = TestVisibility.TaggedBase(ObjectVisibility.Visible);

        var both = FindWithFacade(scene, sizeOnly with { ByDistance = true }).Work.Zone;

        Assert.Equal(scene.Targets.Count(target => target.Base == withoutMesh), both.BoxZoneTargets);
        Assert.Equal(FindWithFacade(scene, sizeOnly).Work.Zone.BoxZoneTargets, both.BoxZoneTargets);
    }

    /// <summary>Each object is tried shrunk, turned and moved, so a try's earlier hit reaches it again on some tries and not on others.</summary>
    [Theory]
    [InlineData(ZoneShape.BoundingBox, NpcHandling.AlwaysLikeAnyObject)]
    [InlineData(ZoneShape.ObjectShape, NpcHandling.AlwaysLikeAnyObject)]
    [InlineData(ZoneShape.ObjectShape, NpcHandling.OnlyWhenStuckInsideTheObject)]
    public void EveryTryOfAnObjectClashesWhereItsOwnCheckClashes(ZoneShape zone, NpcHandling npcs)
    {
        var scene = CreateScene();
        var rule = CreatePlacementRule(scene, new TooCloseSettings(BySize: true, Multiplier, ByDistance: true, DistanceInUnits: Distance, zone, npcs));
        var clashes = new List<bool>();

        foreach (var target in scene.Targets)
        {
            using var tries = rule.StartTries(target.Id);
            foreach (var placement in TriedPlacements(target))
            {
                var own = CheckOnce(rule, target.Id, placement);
                var tried = tries.Check(placement);

                Assert.Equal((own.Clash is null, own.Failure), (tried.Clash is null, tried.Failure));
                clashes.Add(own.Clash is not null);
            }
        }
        Assert.Contains(true, clashes);
        Assert.Contains(false, clashes);
    }

    /// <summary>
    /// Other-mod tables A (id 0) and B (id 1) stand side by side; the tried table touches only B, then both, then only A, then
    /// neither. Once a try hit B, the next try tests B first, and names B where its own check names A.
    /// </summary>
    [Fact]
    public void ATryTestsTheObjectTheLastHitFoundFirstAndOtherwiseSearchesAgain()
    {
        var scene = CreateSideBySideScene(Table.Base, Table.Base);
        var rule = CreatePlacementRule(scene, SizeOnlyMeshZone);
        using var tries = rule.StartTries(new TargetId(0));

        Assert.Equal(ReasonOf(TableB), tries.Check(Beside(OnlyB)).Clash);
        Assert.Equal(ReasonOf(TableA), CheckOnce(rule, new TargetId(0), Beside(OnBoth)).Clash);
        Assert.Equal(ReasonOf(TableB), tries.Check(Beside(OnBoth)).Clash);
        Assert.Equal(ReasonOf(TableA), tries.Check(Beside(OnlyA)).Clash);
        Assert.Equal(new PlacementCheck(null, null), tries.Check(Beside(OnNeither)));
        Assert.Equal(ReasonOf(TableA), tries.Check(Beside(OnBoth)).Clash);
    }

    /// <summary>
    /// One of the two cases where a try records a different failure than its own check: A's mesh cannot be read, and B, hit by
    /// the last try, still reaches the zone. The own check fails on A; the try is a clash by B and records no failure.
    /// </summary>
    [Fact]
    public void ATryWhoseFirstTestedObjectReachesRecordsNoFailureOfALowerObject()
    {
        var scene = CreateSideBySideScene(Unreadable.Base, Table.Base);
        var triangles = new TriangleStore(path => path.Contains(UnreadableName, StringComparison.OrdinalIgnoreCase) ? throw new IOException(UnreadableName) : Shapes.ReadTriangles(path));
        var rule = CreatePlacementRule(scene, SizeOnlyMeshZone, triangles);
        using var tries = rule.StartTries(new TargetId(0));

        Assert.Equal(ReasonOf(TableB), tries.Check(Beside(OnlyB)).Clash);
        Assert.NotNull(CheckOnce(rule, new TargetId(0), Beside(OnBoth)).Failure);
        Assert.Equal(new PlacementCheck(ReasonOf(TableB), null), tries.Check(Beside(OnBoth)));
        Assert.NotNull(tries.Check(Beside(OnlyA)).Failure);
    }

    /// <summary>
    /// The other case: B, hit by the last try, has a mesh whose test throws when B does not touch the zone. The own check hits
    /// A first and never tests B; the try tests B first and records a failure.
    /// </summary>
    [Fact]
    public void ATryWhoseFirstTestedObjectThrowsRecordsAFailureWhereItsOwnCheckHitsALowerObject()
    {
        var scene = CreateSideBySideScene(Table.Base, BrokenParts.Base);
        var rule = CreatePlacementRule(scene, DistanceOnlyMeshZone, new TriangleStore(WithBrokenParts));
        using var tries = rule.StartTries(new TargetId(0));

        Assert.Equal(DistanceReasonOf(TableB), tries.Check(At(RightOfBX, 0f)).Clash);
        Assert.Equal(new PlacementCheck(DistanceReasonOf(TableA), null), CheckOnce(rule, new TargetId(0), At(BesideAOnlyX, BesideY)));
        var tried = tries.Check(At(BesideAOnlyX, BesideY));
        Assert.Null(tried.Clash);
        Assert.NotNull(tried.Failure);
    }

    /// <summary>
    /// With both zones on, a try the size zone misses tests the object the last hit found in the distance zone first: B, hit
    /// there by the last try, names the try where the own check names A.
    /// </summary>
    [Fact]
    public void ATryTestsTheObjectTheLastHitFoundInTheDistanceZoneFirst()
    {
        var scene = CreateSideBySideScene(Table.Base, Table.Base);
        var rule = CreatePlacementRule(scene, SizeOnlyMeshZone with { ByDistance = true, DistanceInUnits = NearDistance });
        using var tries = rule.StartTries(new TargetId(0));

        Assert.Equal(DistanceReasonOf(TableB), tries.Check(At(BesideOnlyBX, BesideY)).Clash);
        Assert.Equal(DistanceReasonOf(TableA), CheckOnce(rule, new TargetId(0), At(BesideBothX, BesideY)).Clash);
        Assert.Equal(DistanceReasonOf(TableB), tries.Check(At(BesideBothX, BesideY)).Clash);
    }

    /// <summary>The whole search and each object's tries end every mesh lease they take, also when a mesh cannot be read.</summary>
    [Fact]
    public void EveryMeshLeaseIsEndedAfterTheSearchAndAfterEachObjectsTries()
    {
        var scene = CreateScene();
        var settings = new TooCloseSettings(BySize: true, Multiplier, ByDistance: true, DistanceInUnits: Distance, ZoneShape.ObjectShape, NpcHandling.Never);
        var searched = new LeaseCountingMeshes(Shapes.ReadTriangles);
        var tried = new LeaseCountingMeshes(path => path.EndsWith(Boulder.Model, StringComparison.OrdinalIgnoreCase) ? throw new IOException(Boulder.Model) : Shapes.ReadTriangles(path));
        var rule = CreatePlacementRule(scene, settings, tried);
        var failures = 0;

        FindWithFacade(scene, settings, searched);
        foreach (var target in scene.Targets)
        {
            using var tries = rule.StartTries(target.Id);
            failures += TriedPlacements(target).Count(placement => tries.Check(placement).Failure is not null);
        }

        Assert.Equal((0, 0), (searched.Open, tried.Open));
        Assert.True(searched.Acquired > 0 && tried.Acquired > 0 && failures > 0);
    }

    private static (TooCloseSearchResult Search, int LargeOtherObjects, NpcStuckSummary? Summary) FindWithSearches(TooCloseScene scene, TooCloseSettings settings)
    {
        var replacements = Replacements.None(scene.OtherModObjects.Count);
        var world = CreateWorld(scene);
        var otherModObjects = world.ObjectsThatCanCauseRemovals(replacements, settings.Npcs);
        var npcRule = NpcTooCloseRule.Create(
            settings.Npcs,
            onlyWithBlockingCollision => NpcStuckSearch.Create(
                world.VisibleTargets, world.NpcsThatCanSpawn(replacements), Shapes, NewTriangles(), onlyWithBlockingCollision));
        var order = TargetWorkOrder.Of(scene.Targets);
        var execution = new Execution(Threads);
        var largeOtherObjects = 0;
        var search = settings.Zone switch
        {
            ZoneShape.BoundingBox => TooCloseSearch.FindTooCloseTargets(
                scene.Targets, otherModObjects, Shapes, ZoneGrowth.Active(settings), npcRule, order, execution),
            _ => FindInShapeZone(scene, settings, world.VisibleTargets, otherModObjects, npcRule, order, execution, out largeOtherObjects),
        };
        return (search, largeOtherObjects, npcRule.StuckSearch?.GetSummary(search.Work.Npcs));
    }

    private static TooCloseSearchResult FindInShapeZone(
        TooCloseScene scene, TooCloseSettings settings, IVisibleTargetObjects visibleTargets, IObjectsThatCanCauseRemovals otherModObjects, NpcTooCloseRule npcRule, WorkOrder order, Execution execution, out int largeOtherObjects)
    {
        var zone = ShapeZoneSearch.Create(scene.Targets, visibleTargets, otherModObjects, Shapes, NewTriangles(), ZoneGrowth.Active(settings), npcRule);
        largeOtherObjects = zone.LargeOtherObjects;
        return zone.FindTooCloseTargets(order, execution);
    }

    private static TooCloseResult FindWithFacade(TooCloseScene scene, TooCloseSettings settings) => FindWithFacade(scene, settings, NewTriangles());

    private static TooCloseResult FindWithFacade(TooCloseScene scene, TooCloseSettings settings, ITriangleMeshes triangles)
    {
        var replacements = Replacements.None(scene.OtherModObjects.Count);
        var caches = CreateWorld(scene);
        return TooCloseObjects.Find(
            TestScenes.CreateWorld(scene.Targets, scene.OtherModObjects),
            caches.VisibleTargets,
            caches.ObjectsThatCanCauseRemovals(replacements, settings.Npcs),
            caches.PlacedNpcs,
            replacements,
            Shapes,
            triangles,
            new Execution(Threads),
            settings);
    }

    /// <summary>
    /// The scene's targets turn only about Z, but store no Euler rotation; each gets the stored rotation of its turn, and the
    /// transform made from it, so the target at its own placement is the target itself.
    /// </summary>
    private static TooCloseScene WithStoredHeadings(TooCloseScene scene) =>
        scene with
        {
            Targets =
            [
                .. scene.Targets.Select(target => target.PlacedAt(new EulerPlacement(
                    target.Transform.Position,
                    new Vector3(0, 0, MathF.Atan2(-target.Transform.Rotation.M21, target.Transform.Rotation.M11)),
                    target.Transform.Scale))),
            ],
        };

    private static IPlacementRule CreatePlacementRule(TooCloseScene scene, TooCloseSettings settings) =>
        CreatePlacementRule(scene, settings, NewTriangles());

    private static IPlacementRule CreatePlacementRule(TooCloseScene scene, TooCloseSettings settings, ITriangleMeshes triangles)
    {
        var replacements = Replacements.None(scene.OtherModObjects.Count);
        var caches = CreateWorld(scene);
        return TooCloseObjects.CreatePlacementRule(
            TestScenes.CreateWorld(scene.Targets, scene.OtherModObjects),
            caches.VisibleTargets,
            caches.ObjectsThatCanCauseRemovals(replacements, settings.Npcs),
            caches.PlacedNpcs,
            replacements,
            Shapes,
            triangles,
            settings);
    }

    private static PlacementCheck CheckOnce(IPlacementRule rule, TargetId target, EulerPlacement placement)
    {
        using var tries = rule.StartTries(target);
        return tries.Check(placement);
    }

    /// <summary>The target shrunk, turned and moved a little each way, in a fixed order.</summary>
    private static IEnumerable<EulerPlacement> TriedPlacements(TargetObject target) =>
        from scale in new[] { 1f, 0.8f, 0.6f, 0.4f }
        from turn in new[] { 0f, MathF.PI / 4, MathF.PI / 2 }
        from shift in new[] { 0f, Distance, -Distance }
        select new EulerPlacement(target.Transform.Position + new Vector3(shift, 0, 0), new Vector3(0, 0, turn), scale);

    /// <summary>A table (target 0) at the origin, and other-mod tables A (id 0) and B (id 1) beside each other along X.</summary>
    private static TooCloseScene CreateSideBySideScene(BaseKey tableABase, BaseKey tableBBase) => new(
        [TestTargets.Create(0, TestTargets.At(Vector3.Zero, 0f), Table.Base, TestTargets.Space)],
        [TestShapes.Placed(Mod, TableA.Index, tableABase, new Vector3(TableAX, 0, 0)), TestShapes.Placed(Mod, TableB.Index, tableBBase, new Vector3(TableBX, 0, 0))]);

    private static EulerPlacement Beside(float x) => At(x, 0f);

    private static EulerPlacement At(float x, float y) => new(new Vector3(x, y, 0), Vector3.Zero, 1f);

    private static RemovalReason ReasonOf(OtherId other) => new RemovalReason.TooClose(other, new TooCloseCause.ObjectSize());

    private static RemovalReason DistanceReasonOf(OtherId other) => new RemovalReason.TooClose(other, new TooCloseCause.Distance(NearDistance));

    /// <summary>The brokenparts mesh lists a part starting past its last triangle, so testing whether that part lies inside another mesh throws.</summary>
    private static MeshTriangles? WithBrokenParts(string meshPath)
    {
        var triangles = Shapes.ReadTriangles(meshPath);
        if (triangles is null || !meshPath.Contains(BrokenPartsName, StringComparison.OrdinalIgnoreCase)) return triangles;
        return new MeshTriangles(triangles.Vertices, triangles.Indices, [triangles.TriangleCount]);
    }

    /// <summary>Visible targets with and without meshes, every few of them invisible, among other mods' objects and placed NPCs.</summary>
    private static TooCloseScene CreateScene()
    {
        var random = new Random(52);
        BaseKey[] targetBases = [Table.Base, Boulder.Base, TestVisibility.TaggedBase(ObjectVisibility.Visible)];
        var xMarker = TestVisibility.TaggedBase(ObjectVisibility.Invisible(InvisibleObjectKind.XMarkers));
        var targets = Enumerable.Range(0, TargetCount)
            .Select(i => TestTargets.Create(
                i,
                TestTargets.At(RandomGroundPoint(random), TestMeshes.RandomAngle(random)),
                i % InvisibleEvery == 0 ? xMarker : targetBases[i % targetBases.Length],
                TestTargets.Space))
            .ToList();
        var otherModObjects = Enumerable.Range(0, OtherModObjectCount)
            .Select(i => i % ObjectOtherModEvery == 0
                ? TestShapes.Placed(Mod, i, Table.Base, RandomGroundPoint(random), TestMeshes.RandomAngle(random))
                : TestNpcs.Place(Mod, i, Npc, RandomGroundPoint(random), new Vector3(0, 0, TestMeshes.RandomAngle(random))))
            .ToList();
        return new TooCloseScene(targets, otherModObjects);
    }

    /// <summary>Targets far apart, each with another mod's NPC standing at its origin, sunk into it.</summary>
    private static TooCloseScene CreateStuckNpcScene()
    {
        BaseKey[] bases = [CollidingBoulder.Base, Fern.Base, Boulder.Base, new(MeshNotFound.ToRecordKey(), BaseLinkKind.PlaceableObject)];
        var positions = bases.Select((_, i) => new Vector3(i * SceneExtent, 0, 0)).ToList();
        var targets = bases.Select((baseKey, i) => TestTargets.Create(i, TestTargets.At(positions[i], 0f), baseKey, TestTargets.Space)).ToList();
        var npcs = positions.Select((position, i) => TestNpcs.Place(Mod, i, Npc, position, Vector3.Zero)).ToList();
        return new TooCloseScene(targets, npcs);
    }

    private static ObjectCaches CreateWorld(TooCloseScene scene) =>
        TestScenes.Create(scene.Targets, scene.OtherModObjects, Shapes, bodies: NewBodies(), threads: Threads);

    private static Vector3 RandomGroundPoint(Random random) =>
        new((float)random.NextDouble() * SceneExtent, (float)random.NextDouble() * SceneExtent, 0);

    private static string Describe(NpcStuckSummary summary) =>
        $"{summary.Sizes} {summary.PairsTested} {summary.CoreTests} {summary.Conflicts} {summary.PointFallbacks.Count}";

    private static TriangleStore NewTriangles() => new(Shapes.ReadTriangles);

    private static NpcBodyCache NewBodies() =>
        TestNpcBodies.Create(Records.ToImmutableLinkCache(), Shapes);

    /// <summary>Non-playable race without a skin, so the NPC is sized by its Object Bounds.</summary>
    private static SkyrimMod CreateRecords()
    {
        var mod = new SkyrimMod(Mod, SkyrimRelease.SkyrimSE);
        mod.Races.Add(TestNpcs.CreateRace(Race, skin: null, playable: false, armorRace: null));
        var npc = TestNpcs.CreateNpc(Npc, Race, female: false, template: null);
        npc.ObjectBounds = new ObjectBounds
        {
            First = new P3Int16(-NpcHalfWidth, -NpcHalfWidth, 0),
            Second = new P3Int16(NpcHalfWidth, NpcHalfWidth, NpcHeight),
        };
        mod.Npcs.Add(npc);
        mod.Statics.Add(new Static(MeshNotFound, SkyrimRelease.SkyrimSE)
        {
            Model = new Model { File = @"test\notfound.nif" },
            ObjectBounds = new ObjectBounds { First = new P3Int16(-50, -50, -20), Second = new P3Int16(50, 50, 40) },
        });
        return mod;
    }
}
