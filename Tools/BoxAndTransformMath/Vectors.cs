using System.Numerics;

namespace AddedObjectRemover;

public static class Vectors
{
    public static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    /// <summary>Whether <paramref name="position"/> lies farther than <paramref name="radius"/> from <paramref name="point"/>; a NaN distance is not farther.</summary>
    public static bool IsFartherThan(Vector3 position, Vector3 point, float radius) => Vector3.Distance(position, point) > radius;

    /// <summary>Every component within ±<paramref name="limit"/>; a NaN component is not.</summary>
    public static bool IsWithinLimit(Vector3 v, float limit) =>
        MathF.Abs(v.X) <= limit && MathF.Abs(v.Y) <= limit && MathF.Abs(v.Z) <= limit;

    /// <summary>
    /// Per axis, the shortest angle between two rotations given as angles in radians: two angles a full turn apart give 0.
    /// </summary>
    public static Vector3 ShortestAngles(Vector3 one, Vector3 other) =>
        new(ShortestAngle(one.X, other.X), ShortestAngle(one.Y, other.Y), ShortestAngle(one.Z, other.Z));

    private static float ShortestAngle(float one, float other) => MathF.Abs(MathF.IEEERemainder(one - other, MathF.Tau));
}
