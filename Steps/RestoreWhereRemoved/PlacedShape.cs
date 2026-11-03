using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// What of a placed object decides the box it fills and whether and how it blocks: every part the too-close rule and
/// <see cref="ObjectsThatBlockActors"/> read of it.
/// </summary>
/// <param name="PrimitiveBounds">The reference's primitive box, full sizes; null when it is not a primitive.</param>
internal readonly record struct PlacedShape(
    EulerPlacement Placement, BaseKey? Base, bool IsPrimitive, bool HasMapMarker, uint? CollisionLayer, bool IsCullingVolume, Vector3? PrimitiveBounds)
{
    public static PlacedShape Of(TargetObject placed) =>
        new(placed.Placement, placed.Base, placed.IsPrimitive, placed.HasMapMarker, placed.CollisionLayer, placed.IsCullingVolume, placed.PrimitiveBounds);

    /// <summary>
    /// Whether the two are the same shape where they stand: the placement and the primitive box each within the rounding of plugins
    /// that re-save a record, every other part exactly.
    /// </summary>
    public bool IsSameAs(PlacedShape other) =>
        !ResavedPlacedParts.PositionsDiffer(Placement.Position, other.Placement.Position)
        && !ResavedPlacedParts.RotationsDiffer(Placement.Rotation, other.Placement.Rotation)
        && !ResavedPlacedParts.ScalesDiffer(Placement.Scale, other.Placement.Scale)
        && !Vectors.DiffersBeyond(PrimitiveBounds, other.PrimitiveBounds, Vectors.AxisGaps, ResavedPlacedParts.Tolerances.Position)
        && this with { Placement = other.Placement, PrimitiveBounds = other.PrimitiveBounds } == other;
}
