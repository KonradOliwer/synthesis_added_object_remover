namespace AddedObjectRemover;

/// <summary>Counters of the ObjectShape too-close search; one instance per worker thread, then summed.</summary>
internal sealed class ShapeZoneStats
{
    /// <summary>Other objects found by the AABB grid query of a target's zone.</summary>
    public long CandidatePairs;

    /// <summary>Candidate pairs whose oriented boxes overlap.</summary>
    public long BoxFilterPasses;

    /// <summary>Pairs tested mesh against mesh.</summary>
    public long NarrowTests;

    /// <summary>Pairs where the other object reached the zone.</summary>
    public long Hits;

    /// <summary>Pairs decided by the other object's bounds centre because it has no mesh triangles.</summary>
    public long CentrePointFallbacks;

    /// <summary>Targets without mesh triangles, tested with the BoundingBox zone instead.</summary>
    public long BoxZoneTargets;

    public long TrianglePairsTested;

    public void Add(ShapeZoneStats other)
    {
        CandidatePairs += other.CandidatePairs;
        BoxFilterPasses += other.BoxFilterPasses;
        NarrowTests += other.NarrowTests;
        Hits += other.Hits;
        CentrePointFallbacks += other.CentrePointFallbacks;
        BoxZoneTargets += other.BoxZoneTargets;
        TrianglePairsTested += other.TrianglePairsTested;
    }
}
