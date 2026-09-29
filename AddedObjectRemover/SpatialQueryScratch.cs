namespace AddedObjectRemover;

/// <summary>Reusable buffers of one worker thread for box-index queries and containment tests.</summary>
internal sealed class SpatialQueryScratch
{
    public List<int> Triangles { get; } = [];
    public List<int> Slots { get; } = [];
    public List<int> Candidates { get; } = [];
    public List<OtherId> Others { get; } = [];
}
