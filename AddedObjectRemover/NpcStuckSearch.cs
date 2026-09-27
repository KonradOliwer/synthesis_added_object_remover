using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>Reusable buffers of one worker thread for <see cref="NpcStuckSearch"/>.</summary>
internal sealed class NpcScratch
{
    public TouchScratch Touch { get; } = new();
    public List<int> Slots { get; } = [];
    public List<int> Candidates { get; } = [];
}

/// <summary>An other-mod NPC of unknown size standing inside a target object's real box.</summary>
internal readonly record struct UnsizedNpcAtTarget(int TargetIndex, UnsizedNpc Npc);

/// <param name="Sizes">Per placed NPC of the searched spaces, how its size was found.</param>
/// <param name="Conflicts">Target objects found with an NPC stuck in them.</param>
/// <param name="Unsized">NPCs skipped because their size is unknown, ordered by FormKey.</param>
internal sealed record NpcStuckSummary(NpcSizeCounts Sizes, long PairsTested, int Conflicts, IReadOnlyList<UnsizedNpc> Unsized);

/// <summary>
/// The OnlyWhenStuckInObject NPC setting: a visible target object is too close to another mod's
/// placed NPC only when the NPC's body is stuck in the target at the target's real size, not
/// enlarged (<see cref="NpcStuckTest"/>). A target without mesh triangles uses its real box, as a
/// closed box mesh. Candidates are tested in index order and each target writes only its own
/// slot, so results do not depend on thread scheduling.
/// </summary>
internal sealed class NpcStuckSearch
{
    private readonly IReadOnlyList<TargetObject> _targets;
    private readonly IReadOnlyDictionary<FormKey, OtherObjectIndex> _indexes;
    private readonly Dictionary<FormKey, PlacedNpcIndex> _npcIndexes;
    private readonly BaseObjectShapeProvider _shapes;
    private readonly TriangleTreeCache _meshCache;
    private readonly LazyCache<FormKey, MeshTriangleTree> _targetBoxTrees = new();
    private readonly List<UnsizedNpc>?[] _unsizedAtTarget;
    private long _pairsTested;
    private int _conflicts;

    private NpcStuckSearch(
        IReadOnlyList<TargetObject> targets,
        IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes,
        Dictionary<FormKey, PlacedNpcIndex> npcIndexes,
        BaseObjectShapeProvider shapes,
        TriangleTreeCache meshCache)
    {
        _targets = targets;
        _indexes = indexes;
        _npcIndexes = npcIndexes;
        _shapes = shapes;
        _meshCache = meshCache;
        _unsizedAtTarget = new List<UnsizedNpc>?[targets.Count];
    }

    /// <summary>Sizes the placed NPCs of every space holding a visible target.</summary>
    /// <param name="visibility">Parallel to <paramref name="targets"/>.</param>
    public static NpcStuckSearch Create(
        IReadOnlyList<TargetObject> targets,
        IReadOnlyList<ObjectVisibility> visibility,
        IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes,
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
        return new NpcStuckSearch(targets, indexes, npcIndexes, shapes, meshCache);
    }

    public NpcStuckSummary GetSummary() => new(
        _npcIndexes.Values.Aggregate(default(NpcSizeCounts), (sum, index) => sum.Add(index.Counts)),
        Interlocked.Read(ref _pairsTested),
        Volatile.Read(ref _conflicts),
        _npcIndexes.Values
            .SelectMany(index => index.Unsized)
            .OrderBy(unsized => unsized.Npc.FormKey.ToString(), StringComparer.Ordinal)
            .ToList());

    /// <summary>In target order; call after the search.</summary>
    public List<UnsizedNpcAtTarget> CollectUnsizedAtTargets() =>
        Enumerable.Range(0, _unsizedAtTarget.Length)
            .SelectMany(index => (_unsizedAtTarget[index] ?? []).Select(npc => new UnsizedNpcAtTarget(index, npc)))
            .ToList();

    /// <summary>Index of the first other-mod NPC stuck in the visible target, or -1.</summary>
    public int FindFirstStuckNpc(int targetIndex, NpcScratch scratch)
    {
        var target = _targets[targetIndex];
        if (target.Base is not { } baseRef) return -1;

        var npcs = _npcIndexes[target.SpaceKey];
        var localBox = _shapes.GetLocalBox(baseRef);
        var realBox = OrientedBox.FromLocal(localBox, target.Transform);
        RecordUnsizedInside(targetIndex, npcs, realBox, scratch);
        CollectCandidates(npcs, _indexes[target.SpaceKey], realBox, scratch);
        if (scratch.Candidates.Count == 0) return -1;

        if (_shapes.GetMeshPath(baseRef) is not { } meshPath) return FindFirstStuck(GetBoxTree(baseRef, localBox), target.Transform, npcs, scratch);
        using var mesh = _meshCache.Acquire(meshPath);
        return FindFirstStuck(mesh.Tree ?? GetBoxTree(baseRef, localBox), target.Transform, npcs, scratch);
    }

    private void RecordUnsizedInside(int targetIndex, PlacedNpcIndex npcs, OrientedBox realBox, NpcScratch scratch)
    {
        var inside = npcs.CollectUnsizedInside(realBox, scratch.Slots);
        if (inside.Count > 0) _unsizedAtTarget[targetIndex] = inside;
    }

    /// <summary>Fills <see cref="NpcScratch.Candidates"/> with the not replaced sized NPC slots whose body box overlaps the target's real box.</summary>
    private static void CollectCandidates(PlacedNpcIndex npcs, OtherObjectIndex others, OrientedBox realBox, NpcScratch scratch)
    {
        npcs.CollectSized(realBox.WorldAabb(0f), scratch.Slots);
        scratch.Candidates.Clear();
        foreach (var slot in scratch.Slots)
        {
            if (others.IsReplaced(npcs.OtherIndexOf(slot)) || !realBox.Intersects(npcs.WorldBoxOf(slot), 0f)) continue;
            scratch.Candidates.Add(slot);
        }
    }

    private int FindFirstStuck(MeshTriangleTree objectTree, PlacedTransform objectTransform, PlacedNpcIndex npcs, NpcScratch scratch)
    {
        foreach (var slot in scratch.Candidates)
        {
            Interlocked.Increment(ref _pairsTested);
            if (!IsBodyStuck(objectTree, objectTransform, npcs.BodyOf(slot), npcs.TransformOf(slot), scratch.Touch)) continue;
            Interlocked.Increment(ref _conflicts);
            return npcs.OtherIndexOf(slot);
        }
        return -1;
    }

    /// <summary>Each body mesh in turn; the body's box only when none of its meshes has usable triangles.</summary>
    private bool IsBodyStuck(MeshTriangleTree objectTree, PlacedTransform objectTransform, NpcBody body, PlacedTransform bodyTransform, TouchScratch scratch)
    {
        var usedMesh = false;
        foreach (var meshPath in body.MeshPaths)
        {
            using var mesh = _meshCache.Acquire(meshPath);
            if (mesh.Tree is not { } bodyTree) continue;
            usedMesh = true;
            if (NpcStuckTest.IsStuck(objectTree, objectTransform, bodyTree, bodyTransform, scratch)) return true;
        }
        return !usedMesh
            && body.BoxTree is { } boxTree
            && NpcStuckTest.IsStuck(objectTree, objectTransform, boxTree, bodyTransform, scratch);
    }

    private MeshTriangleTree GetBoxTree(BaseRef baseRef, Box localBox) =>
        _targetBoxTrees.GetOrCreate(baseRef.FormKey, () => BoxMesh.CreateTree(localBox));
}
