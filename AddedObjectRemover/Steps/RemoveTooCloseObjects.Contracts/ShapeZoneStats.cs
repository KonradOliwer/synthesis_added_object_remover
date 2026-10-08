namespace AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

/// <summary>Work counts of the ObjectShape too-close search.</summary>
/// <param name="CandidatePairs">Other objects found by the AABB grid query of a target's zone.</param>
/// <param name="BoxFilterPasses">Candidate pairs whose oriented boxes overlap.</param>
/// <param name="NarrowTests">Pairs tested mesh against mesh.</param>
/// <param name="Hits">Pairs where the other object reached the zone.</param>
/// <param name="CentrePointFallbacks">Pairs decided by the other object's bounds centre because it has no mesh triangles.</param>
/// <param name="BoxZoneTargets">Targets without mesh triangles, tested with the BoundingBox zone instead.</param>
public readonly record struct ShapeZoneWork(
    long CandidatePairs,
    long BoxFilterPasses,
    long NarrowTests,
    long Hits,
    long CentrePointFallbacks,
    long BoxZoneTargets,
    long TrianglePairsTested) : IWork<ShapeZoneWork>
{
    public static ShapeZoneWork Zero => default;

    public static ShapeZoneWork operator +(ShapeZoneWork a, ShapeZoneWork b) => new(
        a.CandidatePairs + b.CandidatePairs,
        a.BoxFilterPasses + b.BoxFilterPasses,
        a.NarrowTests + b.NarrowTests,
        a.Hits + b.Hits,
        a.CentrePointFallbacks + b.CentrePointFallbacks,
        a.BoxZoneTargets + b.BoxZoneTargets,
        a.TrianglePairsTested + b.TrianglePairsTested);
}
