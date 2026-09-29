using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>Reusable buffers and counters of one worker thread for <see cref="NpcStuckSearch"/>.</summary>
internal sealed class NpcScratch
{
    public List<int> Triangles { get; } = [];
    public List<Box> Cores { get; } = [];
    public List<int> Slots { get; } = [];
    public List<int> Candidates { get; } = [];
    public long PairsTested { get; set; }
    public long CoreTests { get; set; }
    public int Conflicts { get; set; }
}

/// <param name="Sizes">Per placed NPC of the searched spaces, how its size was found.</param>
/// <param name="CoreTests">Body boxes tested against an object, combined boxes of several possible bodies included.</param>
/// <param name="Conflicts">Target objects found with an NPC stuck in them.</param>
/// <param name="PointFallbacks">NPCs sized as a point, with why, ordered by FormKey; for the detailed log only.</param>
internal sealed record NpcStuckSummary(NpcSizeCounts Sizes, long PairsTested, long CoreTests, int Conflicts, IReadOnlyList<PointNpc> PointFallbacks);

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
    private readonly IReadOnlyDictionary<FormKey, OtherObjectIndex> _indexes;
    private readonly Dictionary<FormKey, PlacedNpcIndex> _npcIndexes;
    private readonly Replacements _replacements;
    private readonly BaseObjectShapeProvider _shapes;
    private readonly TriangleTreeCache _meshCache;
    private readonly LazyCache<FormKey, MeshTriangleTree> _targetBoxTrees = new();
    private readonly object _statsLock = new();
    private long _pairsTested;
    private long _coreTests;
    private int _conflicts;

    private NpcStuckSearch(
        IReadOnlyList<TargetObject> targets,
        IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes,
        Dictionary<FormKey, PlacedNpcIndex> npcIndexes,
        Replacements replacements,
        BaseObjectShapeProvider shapes,
        TriangleTreeCache meshCache)
    {
        _targets = targets;
        _indexes = indexes;
        _npcIndexes = npcIndexes;
        _replacements = replacements;
        _shapes = shapes;
        _meshCache = meshCache;
    }

    /// <summary>Sizes the placed NPCs of every space holding a visible target.</summary>
    /// <param name="visibility">Parallel to <paramref name="targets"/>.</param>
    public static NpcStuckSearch Create(
        IReadOnlyList<TargetObject> targets,
        IReadOnlyList<ObjectVisibility> visibility,
        IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes,
        Replacements replacements,
        NpcBodyCache bodies,
        BaseObjectShapeProvider shapes,
        TriangleTreeCache meshCache,
        ParallelOptions parallelOptions)
    {
        var npcIndexes = new Dictionary<FormKey, PlacedNpcIndex>();
        for (var i = 0; i < targets.Count; i++)
        {
            var spaceKey = targets[i].SpaceKey;
            if (!visibility[i].IsVisible || npcIndexes.ContainsKey(spaceKey)) continue;
            npcIndexes[spaceKey] = PlacedNpcIndex.Build(indexes[spaceKey], bodies, parallelOptions);
        }
        return new NpcStuckSearch(targets, indexes, npcIndexes, replacements, shapes, meshCache);
    }

    public NpcStuckSummary GetSummary()
    {
        lock (_statsLock)
        {
            return new NpcStuckSummary(
                _npcIndexes.Values.Aggregate(default(NpcSizeCounts), (sum, index) => sum.Add(index.Counts)),
                _pairsTested,
                _coreTests,
                _conflicts,
                _npcIndexes.Values
                    .SelectMany(index => index.PointFallbacks)
                    .OrderBy(fallback => fallback.Npc.FormKey.ToString(), StringComparer.Ordinal)
                    .ToList());
        }
    }

    /// <summary>Adds a worker thread's counters once it is done; its scratch must not be used afterwards.</summary>
    public void AddStats(NpcScratch scratch)
    {
        lock (_statsLock)
        {
            _pairsTested += scratch.PairsTested;
            _coreTests += scratch.CoreTests;
            _conflicts += scratch.Conflicts;
        }
    }

    /// <summary>Index of the first other-mod NPC stuck in the visible target, or -1.</summary>
    public int FindFirstStuckNpc(int targetIndex, NpcScratch scratch)
    {
        var target = _targets[targetIndex];
        var npcs = _npcIndexes[target.SpaceKey];
        if (target.Base is not { } baseRef || npcs.Count == 0) return -1;

        var localBox = _shapes.GetLocalBox(baseRef);
        CollectCandidates(npcs, _indexes[target.SpaceKey], OrientedBox.FromLocal(localBox, target.Transform), scratch);
        if (scratch.Candidates.Count == 0) return -1;

        if (_shapes.GetMeshPath(baseRef) is not { } meshPath) return FindFirstStuck(GetBoxTree(baseRef, localBox), target.Transform, npcs, scratch);
        using var mesh = _meshCache.Acquire(meshPath);
        return FindFirstStuck(mesh.Tree ?? GetBoxTree(baseRef, localBox), target.Transform, npcs, scratch);
    }

    /// <summary>Fills <see cref="NpcScratch.Candidates"/> with the not replaced NPC slots whose body box overlaps the target's real box.</summary>
    private void CollectCandidates(PlacedNpcIndex npcs, OtherObjectIndex others, OrientedBox realBox, NpcScratch scratch)
    {
        npcs.Collect(realBox.WorldAabb(0f), scratch.Slots);
        scratch.Candidates.Clear();
        foreach (var slot in scratch.Slots)
        {
            if (_replacements.IsReplaced(others[npcs.OtherIndexOf(slot)].Id) || !realBox.Intersects(npcs.WorldBoxOf(slot), 0f)) continue;
            scratch.Candidates.Add(slot);
        }
    }

    private static int FindFirstStuck(MeshTriangleTree objectTree, PlacedTransform objectTransform, PlacedNpcIndex npcs, NpcScratch scratch)
    {
        foreach (var slot in scratch.Candidates)
        {
            scratch.PairsTested++;
            if (!NpcStuckTest.IsAnyBodyStuck(objectTree, objectTransform, npcs.BodiesOf(slot), npcs.TransformOf(slot), scratch)) continue;
            scratch.Conflicts++;
            return npcs.OtherIndexOf(slot);
        }
        return -1;
    }

    private MeshTriangleTree GetBoxTree(BaseRef baseRef, Box localBox) =>
        _targetBoxTrees.GetOrCreate(baseRef.FormKey, () => BoxMesh.CreateTree(localBox));
}
