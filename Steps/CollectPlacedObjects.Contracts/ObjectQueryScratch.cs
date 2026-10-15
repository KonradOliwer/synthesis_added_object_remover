namespace AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

/// <summary>Reusable buffers of one worker thread for questions about the placed objects.</summary>
public sealed class ObjectQueryScratch
{
    public SpatialQueryScratch Spatial { get; } = new();
    public List<int> Triangles { get; } = [];
    public List<int> Matches { get; } = [];
    public List<OtherId> Others { get; } = [];
}
