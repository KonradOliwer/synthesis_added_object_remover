using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.Spatial;

/// <summary>What each narrow view of the scene lets through, in which order, and that concurrent first use changes nothing.</summary>
public class SceneViewTests
{
    private const int SequentialThreads = 1;
    private const int ParallelThreads = 8;
    private const int SpaceCount = 4;
    private const int TargetsPerSpace = 40;
    private const int OtherModObjectsPerSpace = 30;
    private const int SupportOnlyObjectsPerSpace = 20;
    private const int InvisibleEvery = 4;
    private const float SceneExtent = 1500f;
    private const float QueryRadius = 150f;

    private static readonly ModKey Mod = ModKey.FromNameAndExtension("SceneViews.esp");
    private static readonly FormKey Race = new(Mod, 0x601);
    private static readonly FormKey Npc = new(Mod, 0x602);
    private static readonly BaseKey MissingBase = new(new FormKey(Mod, 0x7FF).ToRecordKey(), BaseLinkKind.PlaceableObject);

    private static readonly TestStatic Crate = new(
        new FormKey(Mod, 0x801), @"test\crate.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20), new Vector3(20))));

    /// <summary>More than a thousand units across: its box lies in many grid cells.</summary>
    private static readonly TestStatic Slab = new(
        new FormKey(Mod, 0x802), @"test\slab.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-500, -600, 0), new Vector3(500, 600, 10))));

    private static readonly SkyrimMod Records = CreateRecords();
    private static readonly IBaseObjectShapes Shapes = TestShapes.Create(Records, "SceneViewData", Crate, Slab);

    [Fact]
    public void VisibleTargetsComeInIdOrderWhereverTheGridFindsThemFirst()
    {
        // The grid meets the slab (id 1) in the cell west of the crate (id 0) before it meets the crate.
        List<TargetObject> targets =
        [
            TestTargets.Create(0, TestTargets.At(new Vector3(1000, 0, 0)), Crate.Base, TestTargets.Space),
            TestTargets.Create(1, TestTargets.At(new Vector3(600, 0, 0)), Slab.Base, TestTargets.Space),
        ];
        var visibleTargets = TestScenes.Create(targets, [], Shapes).VisibleTargets;
        var point = new Vector3(600, 0, 5);
        List<VisibleTargetNear> neighbours = [];

        visibleTargets.Around(TestTargets.Space, point, radius: 450f, neighbours);

        Assert.Equal(new[] { new TargetId(0), new TargetId(1) }, neighbours.Select(neighbour => neighbour.Target));
        Assert.Equal(
            targets.Select(target => OrientedBox.FromLocal(Shapes.Of(target.Base).Box, target.Transform).Center),
            visibleTargets.BoxesNear(TestTargets.Space, point, radius: 450f, include: _ => true).Select(box => box.Center));
    }

    [Theory]
    [InlineData(nameof(NpcHandling.Ignore), false)]
    [InlineData(nameof(NpcHandling.OnlyWhenStuckInObject), false)]
    [InlineData(nameof(NpcHandling.CountLikeObjects), true)]
    public void ObjectsThatCanCauseRemovalsHoldPlacedNpcsOnlyWhenTheyCountLikeObjects(string handling, bool npcIsActive)
    {
        List<OtherObject> otherModObjects = [TestShapes.Placed(Mod, 0, Crate.Base, Vector3.Zero), PlaceNpc(1, Vector3.Zero)];
        var active = TestScenes.Create([], otherModObjects, Shapes, bodies: NewBodies())
            .ObjectsThatCanCauseRemovals(Replacements.None(otherModObjects.Count), Enum.Parse<NpcHandling>(handling));
        List<OtherId> found = [];

        active.Overlapping(TestTargets.Space, Upright(new Box(new Vector3(-5), new Vector3(5))), new ObjectQueryScratch(), found);

        Assert.Equal(npcIsActive ? [new OtherId(0), new OtherId(1)] : new[] { new OtherId(0) }, found);
    }

    [Fact]
    public void NpcsSkipReplacedNpcsButCountTheirSizes()
    {
        List<OtherObject> otherModObjects = [PlaceNpc(0, Vector3.Zero), PlaceNpc(1, Vector3.Zero)];
        var replaced = Replacements.Of(otherModObjects.Count, [new Replacement(new OtherId(0), new TargetId(0), Distance: 0f, SizeRatio: 1f)]);
        var npcs = TestScenes.Create([], otherModObjects, Shapes, bodies: NewBodies()).NpcsThatCanSpawn(replaced);
        List<int> slots = [];

        npcs.Overlapping(TestTargets.Space, Upright(new Box(new Vector3(-5), new Vector3(5))), slots);

        Assert.Equal(new OtherId(1), npcs.NpcOf(TestTargets.Space, Assert.Single(slots)).Id);
        Assert.Equal(2, npcs.SizesIn(TestTargets.Space).Evaluated);
    }

    [Fact]
    public void VisibleObjectsOfAnyPluginHoldReplacedOtherModObjectsAndSupportOnlyObjectsButNoInvisibleObjects()
    {
        List<OtherObject> otherModObjects = [TestShapes.Placed(Mod, 0, Crate.Base, Vector3.Zero), TestShapes.Placed(Mod, 1, MissingBase, Vector3.Zero)];
        List<OtherObject> supportOnlyObjects = [TestShapes.Placed(Mod, 2, Crate.Base, Vector3.Zero), TestShapes.Placed(Mod, 3, MissingBase, Vector3.Zero)];
        var scene = TestScenes.Create([], otherModObjects, Shapes, supportOnlyObjects);
        var replaced = Replacements.Of(otherModObjects.Count, [new Replacement(new OtherId(0), new TargetId(0), Distance: 0f, SizeRatio: 1f)]);
        var area = new Box(new Vector3(-5), new Vector3(5));
        List<OtherId> objectsOfAnyPlugin = [];
        List<OtherId> active = [];

        scene.VisibleObjectsOfAnyPlugin().Overlapping(TestTargets.Space, area, new ObjectQueryScratch(), objectsOfAnyPlugin);
        scene.ObjectsThatCanCauseRemovals(replaced, NpcHandling.Ignore).Overlapping(TestTargets.Space, Upright(area), new ObjectQueryScratch(), active);

        Assert.Equal(new[] { new OtherId(0), new OtherId(2) }, objectsOfAnyPlugin);
        Assert.Empty(active);
    }

    [Fact]
    public void ConcurrentFirstUseOfEverySpaceGivesTheSequentialAnswers()
    {
        var random = new Random(17);
        var spaces = Enumerable.Range(0, SpaceCount).Select(i => TestTargets.SpaceKey(0x300 + (uint)i)).ToArray();
        var targets = new List<TargetObject>();
        var otherModObjects = new List<OtherObject>();
        var supportOnlyObjects = new List<OtherObject>();
        foreach (var space in spaces)
        {
            for (var i = 0; i < TargetsPerSpace; i++)
            {
                targets.Add(TestTargets.Create(targets.Count, TestTargets.At(RandomPoint(random)), RandomTargetBase(targets.Count, random), space));
            }
            for (var i = 0; i < OtherModObjectsPerSpace; i++) otherModObjects.Add(PlaceOther(otherModObjects.Count, space, random));
        }
        foreach (var space in spaces)
        {
            for (var i = 0; i < SupportOnlyObjectsPerSpace; i++)
            {
                supportOnlyObjects.Add(PlaceOther(otherModObjects.Count + supportOnlyObjects.Count, space, random));
            }
        }

        var sequential = QueryEveryTarget(targets, otherModObjects, supportOnlyObjects, SequentialThreads);
        var parallel = QueryEveryTarget(targets, otherModObjects, supportOnlyObjects, ParallelThreads);

        Assert.Contains(sequential, answer => answer.OtherModObjects.Length > 0);
        Assert.Contains(sequential, answer => answer.Covering != null);
        Assert.Contains(sequential, answer => answer.ObjectsOfAnyPlugin.Length > 0);
        Assert.Contains(sequential, answer => answer.Neighbours.Length > 0);
        Assert.Equal(sequential, parallel);
    }

    private sealed record Answer(string OtherModObjects, int Candidates, int? Covering, string ObjectsOfAnyPlugin, string Neighbours);

    /// <summary>A fresh scene per run, so every space is indexed by whichever worker asks first.</summary>
    private static Answer[] QueryEveryTarget(
        List<TargetObject> targets, List<OtherObject> otherModObjects, List<OtherObject> supportOnlyObjects, int threads)
    {
        var execution = new Execution(threads);
        var scene = TestScenes.Create(targets, otherModObjects, Shapes, supportOnlyObjects, threads: threads);
        var active = scene.ObjectsThatCanCauseRemovals(Replacements.None(otherModObjects.Count), NpcHandling.Ignore);
        var objectsOfAnyPlugin = scene.VisibleObjectsOfAnyPlugin();
        var visibleTargets = scene.VisibleTargets;
        return ParallelMap.Run(
            execution,
            targets.Count,
            () => (Scratch: new ObjectQueryScratch(), Found: new List<OtherId>(), Neighbours: new List<VisibleTargetNear>()),
            (i, buffers) =>
            {
                var target = targets[i];
                var position = target.Transform.Position;
                var area = new Box(position, position).Grown(QueryRadius);
                var candidates = active.Overlapping(target.SpaceKey, Upright(area), buffers.Scratch, buffers.Found);
                var otherModObjectIds = string.Join(' ', buffers.Found.Select(id => id.Index));
                var covering = active.FirstCovering(target.SpaceKey, position, buffers.Scratch)?.Index;
                objectsOfAnyPlugin.Overlapping(target.SpaceKey, area, buffers.Scratch, buffers.Found);
                var objectsOfAnyPluginIds = string.Join(' ', buffers.Found.Select(id => id.Index));
                visibleTargets.Around(target.SpaceKey, position, QueryRadius, buffers.Neighbours);
                var neighbourIds = string.Join(' ', buffers.Neighbours.Select(neighbour => neighbour.Target.Index));
                return new Answer(otherModObjectIds, candidates, covering, objectsOfAnyPluginIds, neighbourIds);
            },
            ParallelMap.AutomaticRangeSize);
    }

    private static OtherObject PlaceOther(int index, RecordKey space, Random random) =>
        TestShapes.Placed(Mod, index, random.Next(InvisibleEvery) == 0 ? MissingBase : RandomBase(random), RandomPoint(random), TestMeshes.RandomAngle(random))
            with { SpaceKey = space };

    private static BaseKey RandomBase(Random random) => random.Next(2) == 0 ? Crate.Base : Slab.Base;

    /// <summary>Every <see cref="InvisibleEvery"/>th target is an invisible object.</summary>
    private static BaseKey RandomTargetBase(int index, Random random)
    {
        var baseKey = RandomBase(random);
        return index % InvisibleEvery == 0 ? MissingBase : baseKey;
    }

    private static OrientedBox Upright(Box box) => OrientedBox.FromLocal(box, TestTargets.At(Vector3.Zero));

    private static Vector3 RandomPoint(Random random) =>
        new((float)random.NextDouble() * SceneExtent, (float)random.NextDouble() * SceneExtent, 0);

    private static OtherObject PlaceNpc(int index, Vector3 position) => TestNpcs.Place(Mod, index, Npc, position, default);

    private static NpcBodyCache NewBodies() =>
        TestNpcBodies.Create(Records.ToImmutableLinkCache(), Shapes);

    /// <summary>Non-playable race without a skin, so the NPC is sized by its Object Bounds.</summary>
    private static SkyrimMod CreateRecords()
    {
        var mod = new SkyrimMod(Mod, SkyrimRelease.SkyrimSE);
        mod.Races.Add(TestNpcs.CreateRace(Race, skin: null, playable: false, armorRace: null));
        var npc = TestNpcs.CreateNpc(Npc, Race, female: false, template: null);
        npc.ObjectBounds = new ObjectBounds { First = new P3Int16(-15, -15, 0), Second = new P3Int16(15, 15, 60) };
        mod.Npcs.Add(npc);
        return mod;
    }
}
