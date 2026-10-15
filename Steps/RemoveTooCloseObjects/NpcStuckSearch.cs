using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects;

/// <summary>Reusable buffers and counters of one worker thread for <see cref="NpcStuckSearch"/>.</summary>
internal sealed class NpcScratch
{
    public List<int> Triangles { get; } = [];
    public List<Box> Cores { get; } = [];
    public List<int> Candidates { get; } = [];
    public long PairsTested { get; set; }
    public long CoreTests { get; set; }
    public int Conflicts { get; set; }

    public NpcWork Harvest() => new(PairsTested, CoreTests, Conflicts);
}

/// <summary>
/// The OnlyWhenStuckInObject NPC setting: a visible target object is too close to another mod's
/// placed NPC only when one of the NPC's possible bodies is stuck in the target at the target's
/// real size, not enlarged (<see cref="NpcStuckTest"/>). A target without mesh triangles uses its
/// real box, as a closed box mesh. Candidates are tested in index order and each target writes
/// only its own slot, so results do not depend on thread scheduling.
/// </summary>
internal sealed class NpcStuckSearch
{
    private readonly IReadOnlyList<TargetObject> _targets;
    private readonly INpcsThatCanSpawn _npcs;
    private readonly IReadOnlyList<RecordKey> _spaces;
    private readonly IBaseObjectShapes _shapes;
    private readonly ITriangleMeshes _meshCache;
    private readonly ComputedOncePerKey<RecordKey, MeshTriangleTree> _targetBoxTrees = new(Publication.BuiltOnce, EqualityComparer<RecordKey>.Default);

    private NpcStuckSearch(
        IReadOnlyList<TargetObject> targets, INpcsThatCanSpawn npcs, IReadOnlyList<RecordKey> spaces, IBaseObjectShapes shapes, ITriangleMeshes meshCache)
    {
        _targets = targets;
        _npcs = npcs;
        _spaces = spaces;
        _shapes = shapes;
        _meshCache = meshCache;
    }

    /// <summary>Sizes the placed NPCs of every space holding a visible target up front.</summary>
    public static NpcStuckSearch Create(
        IReadOnlyList<TargetObject> targets,
        IVisibleTargetObjects visibleTargets,
        INpcsThatCanSpawn npcs,
        IBaseObjectShapes shapes,
        ITriangleMeshes meshCache)
    {
        foreach (var space in visibleTargets.Spaces) npcs.MeasureBodiesIn(space);
        return new NpcStuckSearch(targets, npcs, visibleTargets.Spaces, shapes, meshCache);
    }

    /// <param name="work">The search's work counts, harvested from its scratches.</param>
    public NpcStuckSummary GetSummary(NpcWork work) =>
        new(
            _spaces.Aggregate(default(NpcSizeCounts), (sum, space) => sum.Add(_npcs.SizesIn(space))),
            work.PairsTested,
            work.CoreTests,
            work.Conflicts,
            _spaces
                .SelectMany(_npcs.PointFallbacksIn)
                .OrderBy(fallback => fallback.Npc.Key, RecordKeyTextOrder.Comparer)
                .ToList());

    /// <summary>The first other-mod NPC stuck in the visible target, or null.</summary>
    public OtherId? FindFirstStuckNpc(int targetIndex, NpcScratch scratch)
    {
        var target = _targets[targetIndex];
        if (target.Base is not { } baseKey) return null;

        var localBox = _shapes.Of(baseKey).Box;
        _npcs.Overlapping(target.SpaceKey, OrientedBox.FromLocal(localBox, target.Transform), scratch.Candidates);
        if (scratch.Candidates.Count == 0) return null;

        if (_shapes.Of(baseKey).MeshPath is not { } meshPath) return FindFirstStuck(GetBoxTree(baseKey, localBox), target, scratch);
        using var mesh = _meshCache.Acquire(meshPath);
        return FindFirstStuck(mesh.Value ?? GetBoxTree(baseKey, localBox), target, scratch);
    }

    /// <summary>The first NPC, in slot order among the candidates, whose body is stuck in the object.</summary>
    private OtherId? FindFirstStuck(MeshTriangleTree objectTree, TargetObject target, NpcScratch scratch)
    {
        var space = target.SpaceKey;
        foreach (var slot in scratch.Candidates)
        {
            scratch.PairsTested++;
            if (!NpcStuckTest.IsAnyBodyStuck(objectTree, target.Transform, _npcs.BodiesOf(space, slot), _npcs.TransformOf(space, slot), scratch)) continue;
            scratch.Conflicts++;
            return _npcs.NpcOf(space, slot).Id;
        }
        return null;
    }

    private MeshTriangleTree GetBoxTree(BaseKey baseKey, Box localBox) =>
        _targetBoxTrees.Get(baseKey.Record, () => BoxMesh.CreateTree(localBox));
}
