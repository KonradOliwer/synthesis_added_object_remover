using System.Numerics;

namespace AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

/// <summary>
/// Plugins that re-save a record round its floats, so a difference this small in a part of a placed record is no change. The collect
/// step compares the versions of a record with these rules and the restore step compares where an object ends with them.
/// </summary>
public static class ResavedPlacedParts
{
    /// <summary>Per axis, in radians.</summary>
    private const float RotationTolerance = 0.0001f;

    private const float ScaleTolerance = 0.0001f;

    public static readonly PlacedPartTolerances Tolerances = new(Position: PlacedPartTolerances.ResavedPosition, Rotation: RotationTolerance, Scale: ScaleTolerance);

    /// <summary>A position and none always differ.</summary>
    public static bool PositionsDiffer(Vector3? one, Vector3? other) => Vectors.DiffersBeyond(one, other, Vectors.AxisGaps, Tolerances.Position);

    /// <summary>Per axis in radians; the same rotation a full turn apart is no change, and a rotation and none always differ.</summary>
    public static bool RotationsDiffer(Vector3? one, Vector3? other) => Vectors.DiffersBeyond(one, other, Vectors.ShortestAngles, Tolerances.Rotation);

    /// <summary>A missing scale is the same as 1 (<see cref="ReferenceScale.Normalize"/>).</summary>
    public static bool ScalesDiffer(float? one, float? other) => MathF.Abs(ReferenceScale.Normalize(one) - ReferenceScale.Normalize(other)) > Tolerances.Scale;
}
