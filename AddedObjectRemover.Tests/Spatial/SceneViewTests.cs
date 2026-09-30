using System.Numerics;
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
    private const int RivalsPerSpace = 30;
    private const int BackdropPerSpace = 20;
    private const int InvisibleEvery = 4;
    private const float SceneExtent = 1500f;
    private const float QueryRadius = 150f;

    private static readonly ModKey Mod = ModKey.FromNameAndExtension("SceneViews.esp");
    private static readonly FormKey Race = new(Mod, 0x601);
    private static readonly FormKey Npc = new(Mod, 0x602);
    private static readonly BaseRef MissingBase = new(new FormKey(Mod, 0x7FF), typeof(IStaticGetter));

    private static readonly TestStatic Crate = new(
        new FormKey(Mod, 0x801), @"test\crate.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20), new Vector3(20))));

    /// <summary>More than a thousand units across: its box lies in many grid cells.</summary>
    private static readonly TestStatic Slab = new(
        new FormKey(Mod, 0x802), @"test\slab.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-500, -600, 0), new Vector3(500, 600, 10))));

    private static readonly SkyrimMod Records = CreateRecords();
    private static readonly ShapeCatalog Shapes = TestShapes.Create(Records, "SceneViewData", Crate, Slab);

    [Fact]
    public void VisibleTargetsComeInIdOrderWhereverTheGridFindsThemFirst()
    {
        // The grid meets the slab (id 1) in the cell west of the crate (id 0) before it meets the crate.
        List<TargetObject> targets =
        [
            TestTargets.Create(0, TestTargets.At(new Vector3(1000, 0, 0)), Crate.Ref, TestTargets.Space),
            TestTargets.Create(1, TestTargets.At(new Vector3(600, 0, 0)), Slab.Ref, TestTargets.Space),
        ];
        var visibleTargets = TestScenes.Create(targets, [], Shapes).VisibleTargets(TestSeededLedger.AllVisible(targets.Count));
        var point = new Vector3(600, 0, 5);
        List<VisibleNeighbour> neighbours = [];

        visibleTargets.Around(TestTargets.Space, point, radius: 450f, neighbours);

        Assert.Equal(new[] { new TargetId(0), new TargetId(1) }, neighbours.Select(neighbour => neighbour.Target));
        Assert.Equal(
            targets.Select(target => OrientedBox.FromLocal(Shapes.GetLocalBox(target.Base), target.Transform).Center),
            visibleTargets.BoxesNear(TestTargets.Space, point, radius: 450f, include: _ => true).Select(box => box.Center));
    }

    [Theory]
    [InlineData(nameof(NpcHandling.Ignore), false)]
    [InlineData(nameof(NpcHandling.OnlyWhenStuckInObject), false)]
    [InlineData(nameof(NpcHandling.CountLikeObjects), true)]
    public void ActiveRivalsHoldPlacedNpcsOnlyWhenTheyCountLikeObjects(string handling, bool npcIsActive)
    {
        List<OtherObject> rivals = [TestShapes.Placed(Mod, 0, Crate.Ref, Vector3.Zero), PlaceNpc(1, Vector3.Zero)];
        var active = TestScenes.Create([], rivals, Shapes, bodies: NewBodies())
            .ActiveRivals(Replacements.None(rivals.Count), Enum.Parse<NpcHandling>(handling));
        List<OtherId> found = [];

        active.Overlapping(TestTargets.Space, new Box(new Vector3(-5), new Vector3(5)), new SpatialQueryScratch(), found);

        Assert.Equal(npcIsActive ? [new OtherId(0), new OtherId(1)] : new[] { new OtherId(0) }, found);
    }

    [Fact]
    public void NpcsSkipReplacedNpcsButCountTheirSizes()
    {
        List<OtherObject> rivals = [PlaceNpc(0, Vector3.Zero), PlaceNpc(1, Vector3.Zero)];
        var replaced = Replacements.Of(rivals.Count, [new Replacement(new OtherId(0), new TargetId(0), Distance: 0f, SizeRatio: 1f)]);
        var npcs = TestScenes.Create([], rivals, Shapes, bodies: NewBodies()).Npcs(replaced);
        List<int> slots = [];

        npcs.Overlapping(TestTargets.Space, new Box(new Vector3(-5), new Vector3(5)), slots);

        Assert.Equal(new OtherId(1), npcs.NpcOf(TestTargets.Space, Assert.Single(slots)).Id);
        Assert.Equal(2, npcs.SizesIn(TestTargets.Space).Evaluated);
    }

    [Fact]
    public void SolidsHoldReplacedRivalsAndBackdropButNoInvisibleObjects()
    {
        List<OtherObject> rivals = [TestShapes.Placed(Mod, 0, Crate.Ref, Vector3.Zero), TestShapes.Placed(Mod, 1, MissingBase, Vector3.Zero)];
        List<OtherObject> backdrop = [TestShapes.Placed(Mod, 2, Crate.Ref, Vector3.Zero), TestShapes.Placed(Mod, 3, MissingBase, Vector3.Zero)];
        var scene = TestScenes.Create([], rivals, Shapes, backdrop);
        var replaced = Replacements.Of(rivals.Count, [new Replacement(new OtherId(0), new TargetId(0), Distance: 0f, SizeRatio: 1f)]);
        var area = new Box(new Vector3(-5), new Vector3(5));
        List<OtherId> solids = [];
        List<OtherId> active = [];

        scene.Solids().Overlapping(TestTargets.Space, area, new SpatialQueryScratch(), solids);
        scene.ActiveRivals(replaced, NpcHandling.Ignore).Overlapping(TestTargets.Space, area, new SpatialQueryScratch(), active);

        Assert.Equal(new[] { new OtherId(0), new OtherId(2) }, solids);
        Assert.Empty(active);
    }

    [Fact]
    public void ConcurrentFirstUseOfEverySpaceGivesTheSequentialAnswers()
    {
        var random = new Random(17);
        var spaces = Enumerable.Range(0, SpaceCount).Select(i => new FormKey(TestTargets.TargetMod, 0x300 + (uint)i)).ToArray();
        var targets = new List<TargetObject>();
        var rivals = new List<OtherObject>();
        var backdrop = new List<OtherObject>();
        foreach (var space in spaces)
        {
            for (var i = 0; i < TargetsPerSpace; i++)
            {
                targets.Add(TestTargets.Create(targets.Count, TestTargets.At(RandomPoint(random)), RandomBase(random), space));
            }
            for (var i = 0; i < RivalsPerSpace; i++) rivals.Add(PlaceOther(rivals.Count, space, random));
        }
        foreach (var space in spaces)
        {
            for (var i = 0; i < BackdropPerSpace; i++) backdrop.Add(PlaceOther(rivals.Count + backdrop.Count, space, random));
        }
        var looks = new TargetLooks([.. targets.Select(target => target.Id.Index % InvisibleEvery == 0 ? ObjectVisibility.Invisible(InvisibleObjectKind.XMarkers) : ObjectVisibility.Visible)]);

        var sequential = QueryEveryTarget(targets, looks, rivals, backdrop, SequentialThreads);
        var parallel = QueryEveryTarget(targets, looks, rivals, backdrop, ParallelThreads);

        Assert.Contains(sequential, answer => answer.Rivals.Length > 0);
        Assert.Contains(sequential, answer => answer.Covering != null);
        Assert.Contains(sequential, answer => answer.Solids.Length > 0);
        Assert.Contains(sequential, answer => answer.Neighbours.Length > 0);
        Assert.Equal(sequential, parallel);
    }

    private sealed record Answer(string Rivals, int Candidates, int? Covering, string Solids, string Neighbours);

    /// <summary>A fresh scene per run, so every space is indexed by whichever worker asks first.</summary>
    private static Answer[] QueryEveryTarget(
        List<TargetObject> targets, TargetLooks looks, List<OtherObject> rivals, List<OtherObject> backdrop, int threads)
    {
        var execution = new Execution(threads);
        var scene = TestScenes.Create(targets, rivals, Shapes, backdrop, threads: threads);
        var active = scene.ActiveRivals(Replacements.None(rivals.Count), NpcHandling.Ignore);
        var solids = scene.Solids();
        var visibleTargets = scene.VisibleTargets(looks);
        return ParallelMap.Run(
            execution,
            targets.Count,
            () => (Scratch: new SpatialQueryScratch(), Found: new List<OtherId>(), Neighbours: new List<VisibleNeighbour>()),
            (i, buffers) =>
            {
                var target = targets[i];
                var position = target.Transform.Position;
                var area = new Box(position, position).Grown(QueryRadius);
                var candidates = active.Overlapping(target.SpaceKey, area, buffers.Scratch, buffers.Found);
                var rivalIds = string.Join(' ', buffers.Found.Select(id => id.Index));
                var covering = active.FirstCovering(target.SpaceKey, position, buffers.Scratch)?.Index;
                solids.Overlapping(target.SpaceKey, area, buffers.Scratch, buffers.Found);
                var solidIds = string.Join(' ', buffers.Found.Select(id => id.Index));
                visibleTargets.Around(target.SpaceKey, position, QueryRadius, buffers.Neighbours);
                var neighbourIds = string.Join(' ', buffers.Neighbours.Select(neighbour => neighbour.Target.Index));
                return new Answer(rivalIds, candidates, covering, solidIds, neighbourIds);
            });
    }

    private static OtherObject PlaceOther(int index, FormKey space, Random random) =>
        TestShapes.Placed(Mod, index, random.Next(InvisibleEvery) == 0 ? MissingBase : RandomBase(random), RandomPoint(random), TestMeshes.RandomAngle(random))
            with { SpaceKey = space };

    private static BaseRef RandomBase(Random random) => random.Next(2) == 0 ? Crate.Ref : Slab.Ref;

    private static Vector3 RandomPoint(Random random) =>
        new((float)random.NextDouble() * SceneExtent, (float)random.NextDouble() * SceneExtent, 0);

    private static OtherObject PlaceNpc(int index, Vector3 position) => TestNpcs.Place(Mod, index, Npc, position, default);

    private static NpcBodyCache NewBodies() =>
        new(new NpcBodyResolver(Records.ToImmutableLinkCache(), Shapes, new SkinnedBodyMeasurer(Shapes.ReadGeometry)));

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
