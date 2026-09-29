namespace AddedObjectRemover;

/// <summary>
/// ObjectShape removal zone: a visible target object is too close when an active rival's
/// solid mesh intersects the target's enlarged mesh or lies inside it (<see cref="ShapeZoneContact"/>).
/// Broad phase: rivals' world AABBs against the zone's box; box filter: oriented boxes.
/// Targets without mesh triangles use the BoundingBox zone, and rivals without mesh
/// triangles are tested by their bounds centre. Other mods' placed NPCs follow the
/// <see cref="NpcClashRule"/>. Candidates are tested in index order, so the result
/// does not depend on thread scheduling.
/// </summary>
internal sealed class ShapeZoneSearch
{
    /// <summary>Reusable buffers and counters of one worker thread.</summary>
    private sealed class Scratch
    {
        public TouchScratch Touch { get; } = new();
        public SpatialQueryScratch Query { get; } = new();
        public List<OtherId> Candidates { get; } = [];
        public List<OtherId> BoxPassed { get; } = [];
        public NpcScratch Npcs { get; } = new();
        public long CandidatePairs { get; set; }
        public long BoxFilterPasses { get; set; }
        public long NarrowTests { get; set; }
        public long Hits { get; set; }
        public long CentrePointFallbacks { get; set; }
        public long BoxZoneTargets { get; set; }

        public ClashWork Harvest() => new(
            new ShapeZoneWork(CandidatePairs, BoxFilterPasses, NarrowTests, Hits, CentrePointFallbacks, BoxZoneTargets, Touch.TrianglePairsTested),
            Npcs.Harvest());
    }

    private readonly IReadOnlyList<TargetObject> _targets;
    private readonly IActiveRivals _rivals;
    private readonly ShapeCatalog _shapes;
    private readonly TriangleStore _meshCache;
    private readonly float _multiplier;
    private readonly NpcClashRule _npcRule;

    private ShapeZoneSearch(
        IReadOnlyList<TargetObject> targets,
        IActiveRivals rivals,
        int largeRivals,
        ShapeCatalog shapes,
        TriangleStore meshCache,
        float multiplier,
        NpcClashRule npcRule)
    {
        _targets = targets;
        _rivals = rivals;
        LargeOtherObjects = largeRivals;
        _shapes = shapes;
        _meshCache = meshCache;
        _multiplier = multiplier;
        _npcRule = npcRule;
    }

    public int LargeOtherObjects { get; }

    /// <summary>Indexes the rivals of every space holding a visible target by their world AABB up front.</summary>
    /// <param name="looks">Parallel to <paramref name="targets"/>.</param>
    public static ShapeZoneSearch Create(
        IReadOnlyList<TargetObject> targets,
        TargetLooks looks,
        IActiveRivals rivals,
        ShapeCatalog shapes,
        TriangleStore meshCache,
        float multiplier,
        NpcClashRule npcRule)
    {
        var largeRivals = Enumerable.Range(0, targets.Count)
            .Where(i => looks.ByTarget[i].IsVisible)
            .Select(i => targets[i].SpaceKey)
            .Distinct()
            .Sum(rivals.LargeObjectCount);
        return new ShapeZoneSearch(targets, rivals, largeRivals, shapes, meshCache, multiplier, npcRule);
    }

    /// <param name="looks">Parallel to the targets.</param>
    public ClashSearchResult FindTooCloseTargets(TargetLooks looks, WorkOrder order, Execution execution)
    {
        var (matches, work) = ParallelMap.Run(
            execution,
            order,
            _targets.Count,
            () => new Scratch(),
            (targetIndex, scratch) => looks.ByTarget[targetIndex].IsVisible ? FindFirstTooCloseOther(targetIndex, scratch) : null,
            scratch => scratch.Harvest());
        return new ClashSearchResult(TooCloseSearch.ToHits(_rivals, matches), work);
    }

    private OtherId? FindFirstTooCloseOther(int targetIndex, Scratch scratch) =>
        _npcRule.ThenFirstStuckNpc(FindFirstRivalInZone(targetIndex, scratch), targetIndex, scratch.Npcs);

    /// <summary>The first rival reaching the target's zone, or null.</summary>
    private OtherId? FindFirstRivalInZone(int targetIndex, Scratch scratch)
    {
        var target = _targets[targetIndex];
        if (_shapes.GetMeshPath(target.Base) is not { } meshPath) return FindFirstInBoxZone(target, scratch);

        var zone = ShapeZone.Create(_shapes.GetLocalBox(target.Base), target.Transform, _multiplier);
        CollectRivalsOverlappingZoneBox(target, zone, scratch);
        if (scratch.BoxPassed.Count == 0) return null;

        using var bubble = _meshCache.Acquire(meshPath);
        if (bubble.Tree is not { } bubbleTree) return FindFirstInBoxZone(target, scratch);

        foreach (var rival in scratch.BoxPassed)
        {
            if (!Reaches(bubbleTree, zone, rival, scratch)) continue;
            scratch.Hits++;
            return rival;
        }
        return null;
    }

    private OtherId? FindFirstInBoxZone(TargetObject target, Scratch scratch)
    {
        scratch.BoxZoneTargets++;
        var match = TooCloseSearch.FindFirstCentreInBoxZone(target, _rivals, _shapes, _multiplier, scratch.Query, scratch.Candidates);
        if (match != null) scratch.Hits++;
        return match;
    }

    /// <summary>Fills <see cref="Scratch.BoxPassed"/> with the active rivals whose oriented box overlaps the zone's box, in id order.</summary>
    private void CollectRivalsOverlappingZoneBox(TargetObject target, ShapeZone zone, Scratch scratch)
    {
        scratch.CandidatePairs += _rivals.Overlapping(target.SpaceKey, zone.Box.WorldAabb(0f), scratch.Query, scratch.Candidates);
        scratch.BoxPassed.Clear();
        foreach (var rival in scratch.Candidates)
        {
            var other = _rivals.Get(rival);
            if (!zone.Box.Intersects(OrientedBox.FromLocal(_shapes.GetLocalBox(other.Base), other.Transform), 0f)) continue;
            scratch.BoxPassed.Add(rival);
        }
        scratch.BoxFilterPasses += scratch.BoxPassed.Count;
    }

    /// <summary>Mesh against mesh; a rival without mesh triangles by its bounds centre.</summary>
    private bool Reaches(MeshTriangleTree bubbleTree, ShapeZone zone, OtherId rival, Scratch scratch)
    {
        var other = _rivals.Get(rival);
        if (_shapes.GetMeshPath(other.Base) is not { } otherMeshPath) return IsCentreInZone(zone, rival, scratch);

        using var otherMesh = _meshCache.Acquire(otherMeshPath);
        if (otherMesh.Tree is not { } otherTree) return IsCentreInZone(zone, rival, scratch);

        scratch.NarrowTests++;
        return ShapeZoneContact.Reaches(bubbleTree, zone, otherTree, other.Transform, scratch.Touch);
    }

    private bool IsCentreInZone(ShapeZone zone, OtherId rival, Scratch scratch)
    {
        scratch.CentrePointFallbacks++;
        return zone.Box.Contains(_rivals.CentreOf(rival));
    }
}
