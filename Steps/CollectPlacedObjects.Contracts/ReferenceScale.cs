namespace AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

public static class ReferenceScale
{
    /// <summary>A placed reference's scale; missing, non-finite or non-positive values mean 1.</summary>
    public static float Normalize(float? scale) =>
        scale is { } s && float.IsFinite(s) && s > 0 ? s : 1f;
}
