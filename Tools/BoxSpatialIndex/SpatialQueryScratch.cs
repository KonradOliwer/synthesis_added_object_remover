namespace AddedObjectRemover;

/// <summary>Reusable buffers of one worker thread for the queries of <see cref="ItemsInSpace{T}"/>.</summary>
public sealed class SpatialQueryScratch
{
    public List<int> Slots { get; } = [];
    public List<int> Candidates { get; } = [];
}
