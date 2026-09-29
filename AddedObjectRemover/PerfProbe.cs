namespace AddedObjectRemover;

internal sealed class PerfProbe(TriangleStore triangles, NpcBodyCache bodies, SkinnedBodyMeasurer bodyMeasurer) : IPerfProbe
{
    public TriangleTreeStats Triangles() => triangles.GetStats();

    public NpcBodyPerf Bodies() => new(bodies.GetStats(), bodyMeasurer.GetMeasurements().Count);
}
