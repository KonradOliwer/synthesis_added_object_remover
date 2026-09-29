using System.Collections.Concurrent;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>
/// ObjectShape removal zone: a visible target object is too close when a visible other-mod object's
/// solid mesh intersects the target's enlarged mesh or lies inside it (<see cref="ShapeZoneContact"/>).
/// Broad phase: other objects' world AABBs against the zone's box; box filter: oriented boxes.
/// Targets without mesh triangles use the BoundingBox zone, and other objects without mesh
/// triangles are tested by their bounds centre. Other mods' placed NPCs follow the
/// <see cref="NpcClashRule"/>. Each target writes only its own result slot and
/// candidates are tested in index order, so the result does not depend on thread scheduling.
/// </summary>
internal sealed class ShapeZoneSearch
{
    /// <summary>Reusable buffers and counters of one worker thread.</summary>
    private sealed class Scratch
    {
        public TouchScratch Touch { get; } = new();
        public List<int> Slots { get; } = [];
        public List<int> Candidates { get; } = [];
        public List<int> BoxPassed { get; } = [];
        public ShapeZoneStats Stats { get; } = new();
        public NpcScratch Npcs { get; } = new();
    }

    private readonly IReadOnlyList<TargetObject> _targets;
    private readonly IReadOnlyDictionary<FormKey, OtherObjectIndex> _indexes;
    private readonly Replacements _replacements;
    private readonly IReadOnlyList<OtherObjectBoxIndex> _visibleTargetSpaceBounds;
    private readonly BaseObjectShapeProvider _shapes;
    private readonly TriangleTreeCache _meshCache;
    private readonly float _multiplier;
    private readonly NpcClashRule _npcRule;
    private readonly object _statsLock = new();

    private ShapeZoneSearch(
        IReadOnlyList<TargetObject> targets,
        IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes,
        Replacements replacements,
        IReadOnlyList<OtherObjectBoxIndex> visibleTargetSpaceBounds,
        BaseObjectShapeProvider shapes,
        TriangleTreeCache meshCache,
        float multiplier,
        NpcClashRule npcRule)
    {
        _targets = targets;
        _indexes = indexes;
        _replacements = replacements;
        _visibleTargetSpaceBounds = visibleTargetSpaceBounds;
        _shapes = shapes;
        _meshCache = meshCache;
        _multiplier = multiplier;
        _npcRule = npcRule;
    }

    public ShapeZoneStats Stats { get; } = new();

    public int LargeOtherObjects => _visibleTargetSpaceBounds.Sum(bounds => bounds.LargeObjectCount);

    /// <summary>Indexes the other objects of every space holding a visible target by their world AABB up front.</summary>
    /// <param name="visibility">Parallel to <paramref name="targets"/>.</param>
    public static ShapeZoneSearch Create(
        IReadOnlyList<TargetObject> targets,
        IReadOnlyList<ObjectVisibility> visibility,
        IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes,
        Replacements replacements,
        BaseObjectShapeProvider shapes,
        TriangleTreeCache meshCache,
        float multiplier,
        NpcClashRule npcRule)
    {
        var visibleTargetSpaceBounds = Enumerable.Range(0, targets.Count)
            .Where(i => visibility[i].IsVisible)
            .Select(i => targets[i].SpaceKey)
            .Distinct()
            .Select(spaceKey => indexes[spaceKey].Bounds)
            .ToList();
        return new ShapeZoneSearch(targets, indexes, replacements, visibleTargetSpaceBounds, shapes, meshCache, multiplier, npcRule);
    }

    /// <param name="visibility">Parallel to the targets.</param>
    public List<TooCloseHit> FindTooCloseTargets(IReadOnlyList<ObjectVisibility> visibility, ParallelOptions parallelOptions)
    {
        var order = OrderVisibleTargetsBySpaceAndCell(visibility);
        var matches = Enumerable.Repeat(-1, _targets.Count).ToArray();
        Parallel.ForEach(
            Partitioner.Create(0, order.Length),
            parallelOptions,
            () => new Scratch(),
            (range, _, scratch) =>
            {
                for (var i = range.Item1; i < range.Item2; i++) matches[order[i]] = FindFirstTooCloseOther(order[i], scratch);
                return scratch;
            },
            AddStats);
        return TooCloseSearch.ToHits(_targets, _indexes, matches);
    }

    /// <summary>Neighbouring targets are tested close together in time, so their meshes are still cached.</summary>
    private int[] OrderVisibleTargetsBySpaceAndCell(IReadOnlyList<ObjectVisibility> visibility) =>
        Enumerable.Range(0, _targets.Count)
            .Where(i => visibility[i].IsVisible)
            .GroupBy(i => _targets[i].SpaceKey)
            .SelectMany(space => space
                .OrderBy(i => ExteriorGrid.CellIndex(_targets[i].Transform.Position.X))
                .ThenBy(i => ExteriorGrid.CellIndex(_targets[i].Transform.Position.Y)))
            .ToArray();

    private void AddStats(Scratch scratch)
    {
        scratch.Stats.TrianglePairsTested = scratch.Touch.TrianglePairsTested;
        lock (_statsLock) Stats.Add(scratch.Stats);
        _npcRule.AddStats(scratch.Npcs);
    }

    private int FindFirstTooCloseOther(int targetIndex, Scratch scratch) =>
        _npcRule.ThenFirstStuckNpc(FindFirstObjectInZone(targetIndex, scratch), targetIndex, scratch.Npcs);

    /// <summary>Index of the first other object reaching the target's zone, or -1.</summary>
    private int FindFirstObjectInZone(int targetIndex, Scratch scratch)
    {
        var target = _targets[targetIndex];
        var others = _indexes[target.SpaceKey];
        if (_shapes.GetMeshPath(target.Base) is not { } meshPath) return FindFirstInBoxZone(target, others, scratch);

        var zone = ShapeZone.Create(_shapes.GetLocalBox(target.Base), target.Transform, _multiplier);
        CollectOthersOverlappingZoneBox(others, zone, scratch);
        if (scratch.BoxPassed.Count == 0) return -1;

        using var bubble = _meshCache.Acquire(meshPath);
        if (bubble.Tree is not { } bubbleTree) return FindFirstInBoxZone(target, others, scratch);

        foreach (var otherIndex in scratch.BoxPassed)
        {
            if (!Reaches(bubbleTree, zone, others, otherIndex, scratch)) continue;
            scratch.Stats.Hits++;
            return otherIndex;
        }
        return -1;
    }

    private int FindFirstInBoxZone(TargetObject target, OtherObjectIndex others, Scratch scratch)
    {
        scratch.Stats.BoxZoneTargets++;
        var match = TooCloseSearch.FindFirstCentreInBoxZone(target, others, _replacements, _shapes, _multiplier, _npcRule, scratch.Slots, scratch.Candidates);
        if (match >= 0) scratch.Stats.Hits++;
        return match;
    }

    /// <summary>Fills <see cref="Scratch.BoxPassed"/> with the visible, not replaced others tested like objects whose oriented box overlaps the zone's box, in index order.</summary>
    private void CollectOthersOverlappingZoneBox(OtherObjectIndex others, ShapeZone zone, Scratch scratch)
    {
        others.Bounds.CollectCandidates(zone.Box.WorldAabb(0f), scratch.Slots, scratch.Candidates);
        scratch.Stats.CandidatePairs += scratch.Candidates.Count;
        scratch.BoxPassed.Clear();
        foreach (var otherIndex in scratch.Candidates)
        {
            var other = others[otherIndex];
            if (_replacements.IsReplaced(other.Id) || !_npcRule.TestsLikeObject(other) || !others.IsVisible(otherIndex)) continue;
            if (!zone.Box.Intersects(OrientedBox.FromLocal(_shapes.GetLocalBox(other.Base), other.Transform), 0f)) continue;
            scratch.BoxPassed.Add(otherIndex);
        }
        scratch.Stats.BoxFilterPasses += scratch.BoxPassed.Count;
    }

    /// <summary>Mesh against mesh; an other object without mesh triangles by its bounds centre.</summary>
    private bool Reaches(MeshTriangleTree bubbleTree, ShapeZone zone, OtherObjectIndex others, int otherIndex, Scratch scratch)
    {
        var other = others[otherIndex];
        if (_shapes.GetMeshPath(other.Base) is not { } otherMeshPath) return IsCentreInZone(zone, others, otherIndex, scratch);

        using var otherMesh = _meshCache.Acquire(otherMeshPath);
        if (otherMesh.Tree is not { } otherTree) return IsCentreInZone(zone, others, otherIndex, scratch);

        scratch.Stats.NarrowTests++;
        return ShapeZoneContact.Reaches(bubbleTree, zone, otherTree, other.Transform, scratch.Touch);
    }

    private static bool IsCentreInZone(ShapeZone zone, OtherObjectIndex others, int otherIndex, Scratch scratch)
    {
        scratch.Stats.CentrePointFallbacks++;
        return others.TryGetVisibleCenter(otherIndex, out var centre) && zone.Box.Contains(centre);
    }
}
