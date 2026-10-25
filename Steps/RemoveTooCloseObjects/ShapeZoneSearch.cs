using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects;

/// <summary>
/// ObjectShape removal zone: a visible target object is too close when an other-mod object that can cause removals
/// has a solid mesh that reaches the target's zone mesh or lies inside it (<see cref="ShapeZoneContact"/>).
/// The zones that are on are tested in order and the first hit is the target's one finding.
/// Broad phase: the other-mod objects' world AABBs against the zone's box; box filter: oriented boxes.
/// Targets without mesh triangles use the BoundingBox zone, and other-mod objects without mesh
/// triangles are tested by their bounds centre. Other mods' placed NPCs follow the
/// <see cref="NpcTooCloseRule"/>. Candidates are tested in index order, so the result
/// does not depend on thread scheduling.
/// </summary>
internal sealed class ShapeZoneSearch : ITooCloseSearch
{
    /// <summary>
    /// One pair count of one worker thread. A pair the current target's earlier zone already counted is not counted again;
    /// within one zone every pair is met once, so a target tested with one zone looks nothing up.
    /// </summary>
    private sealed class PairCount
    {
        private readonly List<OtherId> _countedForTarget = [];
        private int _countedByEarlierZones;

        public long Total { get; private set; }

        public void StartTarget()
        {
            _countedForTarget.Clear();
            _countedByEarlierZones = 0;
        }

        public void StartZone() => _countedByEarlierZones = _countedForTarget.Count;

        public void Count(OtherId other)
        {
            if (_countedForTarget.IndexOf(other, 0, _countedByEarlierZones) >= 0) return;
            _countedForTarget.Add(other);
            Total++;
        }
    }

    /// <summary>
    /// Reusable buffers and counters of one worker thread, or of one target's tries, whose counters are not read. A target
    /// tested with both zones counts once, and so does each of its pairs. <see cref="Meshes"/> must be released after each target.
    /// </summary>
    private sealed class Scratch(ITriangleMeshes meshes)
    {
        private readonly PairCount _boxFilterPasses = new();
        private readonly PairCount _narrowTests = new();
        private readonly PairCount _centrePointFallbacks = new();
        private long _candidatesOfTarget;
        private bool _boxZoneUsedByTarget;
        private long _candidatePairs;
        private long _boxZoneTargets;

        public HeldMeshes Meshes { get; } = new(meshes);
        public TouchScratch Touch { get; } = new();
        public ObjectQueryScratch Query { get; } = new();
        public List<OtherId> BoxPassed { get; } = [];
        public NpcScratch Npcs { get; } = new();
        public long Hits { get; set; }

        public void StartTarget()
        {
            _boxFilterPasses.StartTarget();
            _narrowTests.StartTarget();
            _centrePointFallbacks.StartTarget();
            _candidatesOfTarget = 0;
            _boxZoneUsedByTarget = false;
        }

        public void StartZone()
        {
            _boxFilterPasses.StartZone();
            _narrowTests.StartZone();
            _centrePointFallbacks.StartZone();
        }

        /// <summary>Counts the target's candidates of one zone's grid query beyond those of its other zone's query.</summary>
        public void CountCandidates(int candidates)
        {
            if (candidates <= _candidatesOfTarget) return;
            _candidatePairs += candidates - _candidatesOfTarget;
            _candidatesOfTarget = candidates;
        }

        public void CountBoxPassed()
        {
            foreach (var other in BoxPassed) _boxFilterPasses.Count(other);
        }

        public void CountMeshTest(OtherId other) => _narrowTests.Count(other);

        public void CountCentreFallback(OtherId other) => _centrePointFallbacks.Count(other);

        public void CountBoxZoneTarget()
        {
            if (_boxZoneUsedByTarget) return;
            _boxZoneUsedByTarget = true;
            _boxZoneTargets++;
        }

        public TooCloseWork Harvest() => new(
            new ShapeZoneWork(
                _candidatePairs, _boxFilterPasses.Total, _narrowTests.Total, Hits, _centrePointFallbacks.Total, _boxZoneTargets, Touch.TrianglePairsTested),
            Npcs.Harvest());
    }

    private readonly IReadOnlyList<TargetObject> _targets;
    private readonly IObjectsThatCanCauseRemovals _otherModObjects;
    private readonly IBaseObjectShapes _shapes;
    private readonly ITriangleMeshes _meshCache;
    private readonly ImmutableArray<ZoneGrowth> _zones;
    private readonly NpcTooCloseRule _npcRule;

    private ShapeZoneSearch(
        IReadOnlyList<TargetObject> targets,
        IObjectsThatCanCauseRemovals otherModObjects,
        int largeOtherObjects,
        IBaseObjectShapes shapes,
        ITriangleMeshes meshCache,
        ImmutableArray<ZoneGrowth> zones,
        NpcTooCloseRule npcRule)
    {
        _targets = targets;
        _otherModObjects = otherModObjects;
        LargeOtherObjects = largeOtherObjects;
        _shapes = shapes;
        _meshCache = meshCache;
        _zones = zones;
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
        ImmutableArray<ZoneGrowth> zones,
        NpcTooCloseRule npcRule)
    {
        var largeOtherObjects = visibleTargets.Spaces.Sum(otherModObjects.LargeObjectCount);
        return new ShapeZoneSearch(targets, otherModObjects, largeOtherObjects, shapes, meshCache, zones, npcRule);
    }

    public TooCloseSearchResult FindTooCloseTargets(WorkOrder order, Execution execution)
    {
        return TooCloseSearch.FindForVisibleTargets(
            _targets, _otherModObjects, _shapes, order, execution, CreateScratch, FindFirstTooCloseOtherHoldingMeshesForTheTarget, scratch => scratch.Harvest());
    }

    public ITargetTries StartTries() => new TriesOfOneTarget(this);

    private Scratch CreateScratch() => new(_meshCache);

    private ZoneHit? FindFirstTooCloseOtherHoldingMeshesForTheTarget(TargetObject target, Scratch scratch)
    {
        try
        {
            return FindFirstTooCloseOther(target, scratch);
        }
        finally
        {
            scratch.Meshes.Release();
        }
    }

    private ZoneHit? FindFirstTooCloseOther(TargetObject target, Scratch scratch)
    {
        scratch.StartTarget();
        return _npcRule.ThenFirstStuckNpc(FindFirstOtherModObjectHit(target, scratch), target, scratch.Npcs);
    }

    /// <remarks>
    /// A target the size zone missed runs the whole search again with the distance zone. One broad phase over a box holding
    /// both zones would save only the second grid query, while the zones' own box filters and mesh tests still differ.
    /// </remarks>
    private ZoneHit? FindFirstOtherModObjectHit(TargetObject target, Scratch scratch) =>
        ZoneGrowth.FirstHit(_zones, growth => FindFirstOtherModObjectInZone(target, growth, scratch));

    /// <summary>Whether the object a check hit reaches the target's zone it hit in, by the box filter and the test of the whole search.</summary>
    /// <remarks>The target's mesh is read only once the box filter passes, as in the whole search.</remarks>
    private bool ReachesAgain(TargetObject target, ZoneHit hit, Scratch scratch)
    {
        var shape = _shapes.Of(target.Base);
        if (shape.MeshPath is not { } meshPath) return false;

        var zone = ZoneOf(hit.Cause).CreateShapeZone(shape.Box, target.Transform);
        return _otherModObjects.Overlaps(hit.Other, zone.Box)
            && scratch.Meshes.Get(meshPath) is { } bubbleTree
            && Reaches(bubbleTree, zone, hit.Other, scratch);
    }

    private ZoneGrowth ZoneOf(TooCloseCause cause) => _zones.Single(zone => zone.Cause.Equals(cause));

    /// <summary>The first other-mod object reaching the target's zone, or null.</summary>
    private OtherId? FindFirstOtherModObjectInZone(TargetObject target, ZoneGrowth growth, Scratch scratch)
    {
        scratch.StartZone();
        if (_shapes.Of(target.Base).MeshPath is not { } meshPath) return FindFirstInBoxZone(target, growth, scratch);

        var zone = growth.CreateShapeZone(_shapes.Of(target.Base).Box, target.Transform);
        CollectOtherModObjectsOverlappingZoneBox(target, zone, scratch);
        if (scratch.BoxPassed.Count == 0) return null;

        if (scratch.Meshes.Get(meshPath) is not { } bubbleTree) return FindFirstInBoxZone(target, growth, scratch);

        foreach (var otherModObject in scratch.BoxPassed)
        {
            if (!Reaches(bubbleTree, zone, otherModObject, scratch)) continue;
            scratch.Hits++;
            return otherModObject;
        }
        return null;
    }

    private OtherId? FindFirstInBoxZone(TargetObject target, ZoneGrowth growth, Scratch scratch)
    {
        scratch.CountBoxZoneTarget();
        var match = TooCloseSearch.FindFirstCentreInBoxZone(target, _otherModObjects, _shapes, growth, scratch.Query);
        if (match != null) scratch.Hits++;
        return match;
    }

    /// <summary>Fills <see cref="Scratch.BoxPassed"/> with the other-mod objects that can cause removals and whose oriented box overlaps the zone's box, in id order.</summary>
    private void CollectOtherModObjectsOverlappingZoneBox(TargetObject target, ShapeZone zone, Scratch scratch)
    {
        scratch.CountCandidates(_otherModObjects.Overlapping(target.SpaceKey, zone.Box, scratch.Query, scratch.BoxPassed));
        scratch.CountBoxPassed();
    }

    /// <summary>Mesh against mesh; an other-mod object without mesh triangles by its bounds centre.</summary>
    private bool Reaches(MeshTriangleTree bubbleTree, ShapeZone zone, OtherId otherModObject, Scratch scratch)
    {
        var other = _otherModObjects.Get(otherModObject);
        if (_shapes.Of(other.Base).MeshPath is not { } otherMeshPath) return IsCentreInZone(zone, otherModObject, scratch);

        if (scratch.Meshes.Get(otherMeshPath) is not { } otherTree) return IsCentreInZone(zone, otherModObject, scratch);

        scratch.CountMeshTest(otherModObject);
        return ShapeZoneContact.Reaches(bubbleTree, zone, otherTree, other.Transform, scratch.Touch);
    }

    private bool IsCentreInZone(ShapeZone zone, OtherId otherModObject, Scratch scratch)
    {
        scratch.CountCentreFallback(otherModObject);
        return zone.Box.Contains(_otherModObjects.CentreOf(otherModObject));
    }

    /// <summary>
    /// The tries of one target, with one scratch whose meshes stay held until the last try. A try first tests the other-mod object
    /// the last object hit found, in the zone it was found in: a try only asks whether the target is too close, and when that
    /// object reaches the zone, the whole search would find a hit too. A stuck NPC is never tested first, since the NPC rule
    /// looks only when no object hits.
    /// </summary>
    private sealed class TriesOfOneTarget(ShapeZoneSearch search) : ITargetTries
    {
        private readonly Scratch _scratch = search.CreateScratch();
        private ZoneHit? _lastObjectHit;

        public TargetCheck Check(TargetObject placed) => TooCloseSearch.CheckTarget(placed, search._shapes, _scratch, FindTooCloseOther);

        public void Dispose() => _scratch.Meshes.Release();

        /// <remarks>
        /// Only a mesh test that throws, which is a bug (an unreadable mesh gives no tree), makes a try's failure differ from the
        /// whole search's: when the remembered object reaches, a lower object whose test would throw is not tested, so the try
        /// is a clash without a failure; when the remembered object's test throws, the try fails where the whole search would
        /// hit a lower object first. Either way the try does not fit, and the result does not depend on the thread count.
        /// </remarks>
        private ZoneHit? FindTooCloseOther(TargetObject target, Scratch scratch)
        {
            scratch.StartTarget();
            if (_lastObjectHit is { } last && search.ReachesAgain(target, last, scratch)) return last;

            var objectHit = search.FindFirstOtherModObjectHit(target, scratch);
            _lastObjectHit = objectHit ?? _lastObjectHit;
            return search._npcRule.ThenFirstStuckNpc(objectHit, target, scratch.Npcs);
        }
    }
}
