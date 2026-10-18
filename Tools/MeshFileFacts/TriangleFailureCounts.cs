namespace AddedObjectRemover;

/// <summary>The meshes whose triangles could not be read in a run; each mesh counts once, by its first failure.</summary>
/// <param name="ByKind">How many meshes failed, by failure, in no particular order.</param>
/// <param name="AfterGoodRead">How many of those meshes had their triangles read fine earlier in the run.</param>
public sealed record TriangleFailureCounts(IReadOnlyList<KeyValuePair<MeshFailureCategory, int>> ByKind, int AfterGoodRead)
{
    public static TriangleFailureCounts None { get; } = new([], 0);
}
