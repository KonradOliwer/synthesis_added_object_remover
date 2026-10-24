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
    public int ConflictsWithoutBlockingCollision { get; set; }

    public NpcWork Harvest() => new(PairsTested, CoreTests, Conflicts, ConflictsWithoutBlockingCollision);
}

/// <summary>
/// The NPC settings that count NPCs only when stuck: a visible target object is too close to another mod's
/// placed NPC only when one of the NPC's possible bodies is stuck in the target at the target's
/// real size, not enlarged (<see cref="NpcStuckTest"/>), and, when only objects with blocking collision
/// count, the target's mesh has collision that stops characters. A target without mesh triangles uses its
/// real box, as a closed box mesh. Candidates are tested in index order and each target writes
/// only its own slot, so results do not depend on thread scheduling.
/// </summary>
internal sealed class NpcStuckSearch
{
    private readonly INpcsThatCanSpawn _npcs;
    private readonly IReadOnlyList<RecordKey> _spaces;
    private readonly IBaseObjectShapes _shapes;
    private readonly ITriangleMeshes _meshCache;
    private readonly bool _onlyWithBlockingCollision;
    private readonly ComputedOncePerKey<RecordKey, MeshTriangleTree> _targetBoxTrees = new(Publication.BuiltOnce, EqualityComparer<RecordKey>.Default);

    private NpcStuckSearch(
        INpcsThatCanSpawn npcs,
        IReadOnlyList<RecordKey> spaces,
        IBaseObjectShapes shapes,
        ITriangleMeshes meshCache,
        bool onlyWithBlockingCollision)
    {
        _npcs = npcs;
        _spaces = spaces;
        _shapes = shapes;
        _meshCache = meshCache;
        _onlyWithBlockingCollision = onlyWithBlockingCollision;
    }

    /// <summary>Sizes the placed NPCs of every space holding a visible target up front.</summary>
    /// <param name="onlyWithBlockingCollision">Only targets whose mesh collision stops characters can have an NPC stuck in them.</param>
    public static NpcStuckSearch Create(
        IVisibleTargetObjects visibleTargets,
        INpcsThatCanSpawn npcs,
        IBaseObjectShapes shapes,
        ITriangleMeshes meshCache,
        bool onlyWithBlockingCollision)
    {
        foreach (var space in visibleTargets.Spaces) npcs.MeasureBodiesIn(space);
        return new NpcStuckSearch(npcs, visibleTargets.Spaces, shapes, meshCache, onlyWithBlockingCollision);
    }

    /// <param name="work">The search's work counts, harvested from its scratches.</param>
    public NpcStuckSummary GetSummary(NpcWork work) =>
        new(
            _spaces.Aggregate(default(NpcSizeCounts), (sum, space) => sum.Add(_npcs.SizesIn(space))),
            work.PairsTested,
            work.CoreTests,
            work.Conflicts,
            work.ConflictsWithoutBlockingCollision,
            _spaces
                .SelectMany(_npcs.PointFallbacksIn)
                .OrderBy(fallback => fallback.Npc.Key, RecordKeyTextOrder.Comparer)
                .ToList());

    /// <summary>The first other-mod NPC stuck in the visible target, or null.</summary>
    public OtherId? FindFirstStuckNpc(TargetObject target, NpcScratch scratch)
    {
        if (target.Base is not { } baseKey) return null;

        var shape = _shapes.Of(baseKey);
        var stuck = FindFirstStuckInShape(target, baseKey, shape, scratch);
        if (stuck == null || !IgnoresStuckNpcsIn(shape)) return stuck;
        scratch.ConflictsWithoutBlockingCollision++;
        return null;
    }

    /// <summary>A target whose mesh collision was not read is a solid box to every check, so only a read collision without blocking layers is ignored.</summary>
    private bool IgnoresStuckNpcsIn(BaseShape shape) => _onlyWithBlockingCollision && shape.CollisionBlocksMovement == false;

    private OtherId? FindFirstStuckInShape(TargetObject target, BaseKey baseKey, BaseShape shape, NpcScratch scratch)
    {
        _npcs.Overlapping(target.SpaceKey, OrientedBox.FromLocal(shape.Box, target.Transform), scratch.Candidates);
        if (scratch.Candidates.Count == 0) return null;

        if (shape.MeshPath is not { } meshPath) return FindFirstStuck(GetBoxTree(baseKey, shape.Box), target, scratch);
        using var mesh = _meshCache.Acquire(meshPath);
        return FindFirstStuck(mesh.Value ?? GetBoxTree(baseKey, shape.Box), target, scratch);
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
