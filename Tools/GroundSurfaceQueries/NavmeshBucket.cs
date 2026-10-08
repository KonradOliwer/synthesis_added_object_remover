namespace AddedObjectRemover;

/// <param name="Grid">The exterior cell; null for the space's navmeshes outside the exterior grid.</param>
public readonly record struct NavmeshBucket(RecordKey SpaceKey, (int X, int Y)? Grid);
