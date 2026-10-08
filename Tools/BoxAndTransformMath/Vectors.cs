using System.Numerics;

namespace AddedObjectRemover;

public static class Vectors
{
    public static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    /// <summary>Every component within ±<paramref name="limit"/>; a NaN component is not.</summary>
    public static bool IsWithinLimit(Vector3 v, float limit) =>
        MathF.Abs(v.X) <= limit && MathF.Abs(v.Y) <= limit && MathF.Abs(v.Z) <= limit;
}
