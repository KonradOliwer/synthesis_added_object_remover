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

    public NpcWork Harvest() => new(PairsTested, CoreTests, Conflicts);
}

/// <summary>Work counts of <see cref="NpcStuckSearch"/>.</summary>
/// <param name="CoreTests">Body boxes tested against an object, combined boxes of several possible bodies included.</param>
/// <param name="Conflicts">Target objects found with an NPC stuck in them.</param>
internal readonly record struct NpcWork(long PairsTested, long CoreTests, int Conflicts) : IWork<NpcWork>
{
    public static NpcWork Zero => default;

    public static NpcWork operator +(NpcWork a, NpcWork b) => new(a.PairsTested + b.PairsTested, a.CoreTests + b.CoreTests, a.Conflicts + b.Conflicts);
}

/// <param name="Sizes">Per placed NPC of the spaces holding a visible target, how its size was found.</param>
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
    private readonly INpcs _npcs;
    private readonly IReadOnlyList<FormKey> _spaces;
    private readonly ShapeCatalog _shapes;
    private readonly TriangleStore _meshCache;
    private readonly LazyCache<FormKey, MeshTriangleTree> _targetBoxTrees = new();

    private NpcStuckSearch(
        IReadOnlyList<TargetObject> targets, INpcs npcs, IReadOnlyList<FormKey> spaces, ShapeCatalog shapes, TriangleStore meshCache)
    {
        _targets = targets;
        _npcs = npcs;
        _spaces = spaces;
        _shapes = shapes;
        _meshCache = meshCache;
    }

    /// <summary>Sizes the placed NPCs of every space holding a visible target up front.</summary>
    /// <param name="visibility">Parallel to <paramref name="targets"/>.</param>
    public static NpcStuckSearch Create(
        IReadOnlyList<TargetObject> targets,
        IReadOnlyList<ObjectVisibility> visibility,
        INpcs npcs,
        ShapeCatalog shapes,
        TriangleStore meshCache)
    {
        var spaces = Enumerable.Range(0, targets.Count)
            .Where(i => visibility[i].IsVisible)
            .Select(i => targets[i].SpaceKey)
            .Distinct()
            .ToList();
        foreach (var space in spaces) npcs.SizesIn(space);
        return new NpcStuckSearch(targets, npcs, spaces, shapes, meshCache);
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
                .OrderBy(fallback => fallback.Npc.FormKey.ToString(), StringComparer.Ordinal)
                .ToList());

    /// <summary>The first other-mod NPC stuck in the visible target, or null.</summary>
    public OtherId? FindFirstStuckNpc(int targetIndex, NpcScratch scratch)
    {
        var target = _targets[targetIndex];
        if (target.Base is not { } baseRef) return null;

        var localBox = _shapes.GetLocalBox(baseRef);
        CollectCandidates(target.SpaceKey, OrientedBox.FromLocal(localBox, target.Transform), scratch);
        if (scratch.Candidates.Count == 0) return null;

        if (_shapes.GetMeshPath(baseRef) is not { } meshPath) return FindFirstStuck(GetBoxTree(baseRef, localBox), target, scratch);
        using var mesh = _meshCache.Acquire(meshPath);
        return FindFirstStuck(mesh.Tree ?? GetBoxTree(baseRef, localBox), target, scratch);
    }

    /// <summary>Fills <see cref="NpcScratch.Candidates"/> with the NPC slots whose body box overlaps the target's real box.</summary>
    private void CollectCandidates(FormKey space, OrientedBox realBox, NpcScratch scratch)
    {
        _npcs.Overlapping(space, realBox.WorldAabb(0f), scratch.Slots);
        scratch.Candidates.Clear();
        foreach (var slot in scratch.Slots)
        {
            if (realBox.Intersects(_npcs.WorldBoxOf(space, slot), 0f)) scratch.Candidates.Add(slot);
        }
    }

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

    private MeshTriangleTree GetBoxTree(BaseRef baseRef, Box localBox) =>
        _targetBoxTrees.GetOrCreate(baseRef.FormKey, () => BoxMesh.CreateTree(localBox));
}
