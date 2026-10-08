namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

/// <summary>Where a mesh's origin sits inside the bounds of its triangles; the wording the log prints for each place.</summary>
public static class MeshOriginPlaces
{
    public const string NearBottom = "origin near bottom";
    public const string NearCentre = "origin near centre";
    public const string Other = "other";
}
