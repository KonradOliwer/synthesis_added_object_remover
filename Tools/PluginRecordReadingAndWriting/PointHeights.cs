using Noggog;

namespace AddedObjectRemover;

/// <summary>Moves Mutagen points up or down.</summary>
internal static class PointHeights
{
    /// <summary>The point at the same X and Y, at height <paramref name="z"/>.</summary>
    public static P3Float WithZ(this P3Float point, float z) => new(point.X, point.Y, z);
}
