using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects;

/// <summary>
/// ObjectShape removal zone: a visible target object is too close when an other-mod object that can cause removals
/// has a solid mesh that intersects the target's enlarged mesh or lies inside it (<see cref="ShapeZoneContact"/>).
/// Broad phase: the other-mod objects' world AABBs against the zone's box; box filter: oriented boxes.
/// Targets without mesh triangles use the BoundingBox zone, and other-mod objects without mesh
/// triangles are tested by their bounds centre. Other mods' placed NPCs follow the
/// <see cref="NpcTooCloseRule"/>. Candidates are tested in index order, so the result
/// does not depend on thread scheduling.
/// </summary>
internal sealed class ShapeZoneSearch
{
    /// <summary>Reusable buffers and counters of one worker thread.</summary>
    private sealed class Scratch
    {
        public TouchScratch Touch { get; } = new();
        public ObjectQueryScratch Query { get; } = new();
        public List<OtherId> BoxPassed { get; } = [];
        public NpcScratch Npcs { get; } = new();
        public long CandidatePairs { get; set; }
        public long BoxFilterPasses { get; set; }
        public long NarrowTests { get; set; }
        public long Hits { get; set; }
        public long CentrePointFallbacks { get; set; }
        public long BoxZoneTargets { get; set; }

        public TooCloseWork Harvest() => new(
            new ShapeZoneWork(CandidatePairs, BoxFilterPasses, NarrowTests, Hits, CentrePointFallbacks, BoxZoneTargets, Touch.TrianglePairsTested),
            Npcs.Harvest());
    }

    private readonly IReadOnlyList<TargetObject> _targets;
    private readonly IObjectsThatCanCauseRemovals _otherModObjects;
    private readonly IBaseObjectShapes _shapes;
    private readonly ITriangleMeshes _meshCache;
    private readonly float _multiplier;
    private readonly NpcTooCloseRule _npcRule;

    private ShapeZoneSearch(
        IReadOnlyList<TargetObject> targets,
        IObjectsThatCanCauseRemovals otherModObjects,
        int largeOtherObjects,
        IBaseObjectShapes shapes,
        ITriangleMeshes meshCache,
        float multiplier,
        NpcTooCloseRule npcRule)
    {
        _targets = targets;
        _otherModObjects = otherModObjects;
        LargeOtherObjects = largeOtherObjects;
        _shapes = shapes;
        _meshCache = meshCache;
        _multiplier = multiplier;
        _npcRule = npcRule;
    }

    public int LargeOtherObjects { get; }

    /// <summary>Indexes the other-mod objects of every space holding a visible target by their world AABB up front.</summary>
    public static ShapeZoneSearch Create(
        IReadOnlyList<TargetObject> targets,
        IVisibleTargetObjects visibleTargets,
        IObjectsThatCanCauseRemovals otherModObjects,
        IBaseObjectShapes shapes,
        ITriangleMeshes meshCache,
        float multiplier,
        NpcTooCloseRule npcRule)
    {
        var largeOtherObjects = visibleTargets.Spaces.Sum(otherModObjects.LargeObjectCount);
        return new ShapeZoneSearch(targets, otherModObjects, largeOtherObjects, shapes, meshCache, multiplier, npcRule);
    }

    public TooCloseSearchResult FindTooCloseTargets(WorkOrder order, Execution execution)
    {
        return TooCloseSearch.FindForVisibleTargets(
            _targets, _otherModObjects, _shapes, order, execution, () => new Scratch(), FindFirstTooCloseOther, scratch => scratch.Harvest());
    }

    private OtherId? FindFirstTooCloseOther(int targetIndex, Scratch scratch) =>
        _npcRule.ThenFirstStuckNpc(FindFirstOtherModObjectInZone(targetIndex, scratch), targetIndex, scratch.Npcs);

    /// <summary>The first other-mod object reaching the target's zone, or null.</summary>
    private OtherId? FindFirstOtherModObjectInZone(int targetIndex, Scratch scratch)
    {
        var target = _targets[targetIndex];
        if (_shapes.Of(target.Base).MeshPath is not { } meshPath) return FindFirstInBoxZone(target, scratch);

        var zone = ShapeZone.Create(_shapes.Of(target.Base).Box, target.Transform, _multiplier);
        CollectOtherModObjectsOverlappingZoneBox(target, zone, scratch);
        if (scratch.BoxPassed.Count == 0) return null;

        using var bubble = _meshCache.Acquire(meshPath);
        if (bubble.Value is not { } bubbleTree) return FindFirstInBoxZone(target, scratch);

        foreach (var otherModObject in scratch.BoxPassed)
        {
            if (!Reaches(bubbleTree, zone, otherModObject, scratch)) continue;
            scratch.Hits++;
            return otherModObject;
        }
        return null;
    }

    private OtherId? FindFirstInBoxZone(TargetObject target, Scratch scratch)
    {
        scratch.BoxZoneTargets++;
        var match = TooCloseSearch.FindFirstCentreInBoxZone(target, _otherModObjects,_shapes, _multiplier, scratch.Query);
        if (match != null) scratch.Hits++;
        return match;
    }

    /// <summary>Fills <see cref="Scratch.BoxPassed"/> with the other-mod objects that can cause removals and whose oriented box overlaps the zone's box, in id order.</summary>
    private void CollectOtherModObjectsOverlappingZoneBox(TargetObject target, ShapeZone zone, Scratch scratch)
    {
        scratch.CandidatePairs += _otherModObjects.Overlapping(target.SpaceKey, zone.Box, scratch.Query, scratch.BoxPassed);
        scratch.BoxFilterPasses += scratch.BoxPassed.Count;
    }

    /// <summary>Mesh against mesh; an other-mod object without mesh triangles by its bounds centre.</summary>
    private bool Reaches(MeshTriangleTree bubbleTree, ShapeZone zone, OtherId otherModObject, Scratch scratch)
    {
        var other = _otherModObjects.Get(otherModObject);
        if (_shapes.Of(other.Base).MeshPath is not { } otherMeshPath) return IsCentreInZone(zone, otherModObject, scratch);

        using var otherMesh = _meshCache.Acquire(otherMeshPath);
        if (otherMesh.Value is not { } otherTree) return IsCentreInZone(zone, otherModObject, scratch);

        scratch.NarrowTests++;
        return ShapeZoneContact.Reaches(bubbleTree, zone, otherTree, other.Transform, scratch.Touch);
    }

    private bool IsCentreInZone(ShapeZone zone, OtherId otherModObject, Scratch scratch)
    {
        scratch.CentrePointFallbacks++;
        return zone.Box.Contains(_otherModObjects.CentreOf(otherModObject));
    }
}
