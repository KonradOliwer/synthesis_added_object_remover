using System.Collections.Immutable;
using System.Diagnostics;
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

    /// <summary>The object properties, each as the part a restore gives back: position, rotation, scale, base and presence.</summary>
    public static readonly ImmutableArray<PlacedParts> ObjectProperties = [PlacedParts.Position, PlacedParts.Rotation, PlacedParts.Scale, PlacedParts.BaseAndData, PlacedParts.Presence];

    /// <summary>Whether two versions differ in one of the <see cref="ObjectProperties"/>; base and presence are exact.</summary>
    public static bool ObjectPropertyDiffers(PlacedParts property, PlacedVersionFact one, PlacedVersionFact other) =>
        property switch
        {
            PlacedParts.Position => PositionsDiffer(one.Placement.Position, other.Placement.Position),
            PlacedParts.Rotation => RotationsDiffer(one.Placement.EulerRotation, other.Placement.EulerRotation),
            PlacedParts.Scale => ScalesDiffer(one.Scale, other.Scale),
            PlacedParts.BaseAndData => one.Base != other.Base,
            PlacedParts.Presence => one.Placement.Presence != other.Placement.Presence,
            _ => throw new UnreachableException($"{property} is no object property."),
        };

    /// <summary>Whether two versions differ in any of the <see cref="ObjectProperties"/>: a version that differs in none only copies the other.</summary>
    public static bool ObjectPropertiesDiffer(PlacedVersionFact one, PlacedVersionFact other) => ObjectProperties.Any(property => ObjectPropertyDiffers(property, one, other));
}
