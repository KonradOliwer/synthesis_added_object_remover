using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// The one answer to which placed objects actors walking a navmesh would run into, and the box each fills, for a placed record
/// going back into a navmesh that stays changed and an object standing in a navmesh going back: a
/// visible object, an invisible one whose collision stops characters, and a primitive whose collision layer blocks actors,
/// measured by its own primitive box. Thread-safe.
/// </summary>
/// <param name="primitives">What the winning version of each primitive of the target spaces says about the reference itself, by key.</param>
internal sealed class ObjectsThatBlockActors(IBaseObjectShapes shapes, IReadOnlyDictionary<RecordKey, ReferenceFacts> primitives)
{
    /// <returns>Null when actors would not run into it.</returns>
    public OrientedBox? SolidBoxOf(TargetObject placed) => SolidBoxOf(PlacedShape.Of(placed));

    /// <returns>Null when actors would not run into it.</returns>
    public OrientedBox? SolidBoxOf(OtherObject placed)
    {
        var reference = placed.IsPrimitive ? primitives.GetValueOrDefault(placed.Key) : default;
        return SolidBoxOf(new PlacedShape(
            new EulerPlacement(placed.Position, placed.Rotation, placed.Scale), placed.Base, placed.IsPrimitive, placed.HasMapMarker,
            reference.CollisionLayer, reference.IsCullingVolume, reference.PrimitiveBounds));
    }

    /// <remarks>The primitive bounds are full sizes, centred on the reference.</remarks>
    private OrientedBox? SolidBoxOf(PlacedShape placed)
    {
        if (!BlocksActors(placed)) return null;
        var local = placed.IsPrimitive && placed.PrimitiveBounds is { } size ? Box.FromCorners(-size / 2f, size / 2f) : shapes.Of(placed.Base).Box;
        return OrientedBox.FromLocal(local, placed.Placement.Transform);
    }

    /// <summary>
    /// A primitive whose base has no mesh, marker bases included, blocks when the collision layer the reference stores blocks actors.
    /// One that stores none has its kind's default layer: activators, acoustic spaces and culling volumes let actors through, every
    /// other kind (collision markers included) blocks. Any other object blocks when it is visible or its collision stops characters.
    /// </summary>
    private bool BlocksActors(PlacedShape placed)
    {
        var shape = shapes.Of(placed.Base);
        if (placed.IsPrimitive && shape is { Resolved: true, MeshPath: null })
        {
            return placed.CollisionLayer is { } layer ? shapes.LayerBlocksActors(layer) : !placed.IsCullingVolume && !shape.PrimitiveWithoutLayerIsWalkThrough;
        }
        return shapes.VisibilityOf(placed.Base, placed.IsPrimitive, placed.HasMapMarker).IsVisible || shape.CollisionBlocksMovement == true;
    }
}
