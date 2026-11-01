using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// The one answer to which placed objects actors walking a navmesh would run into, and the box each fills, for a placed record
/// going back into a navmesh that stays changed (P5), an object standing in a navmesh going back (N5) and an object at a water line
/// (W): a visible object, an invisible one whose collision stops characters, and a primitive whose collision layer blocks actors,
/// measured by its own primitive box. Thread-safe.
/// </summary>
/// <param name="primitives">What the winning version of each primitive of the target spaces says about the reference itself, by key.</param>
internal sealed class ObjectsThatBlockActors(IBaseObjectShapes shapes, IReadOnlyDictionary<RecordKey, ReferenceFacts> primitives)
{
    /// <returns>Null when actors would not run into it.</returns>
    public OrientedBox? SolidBoxOf(TargetObject placed) =>
        SolidBoxOf(placed.Base, placed.IsPrimitive, placed.HasMapMarker, placed.CollisionLayer, placed.IsCullingVolume, placed.PrimitiveBounds, placed.Transform);

    /// <returns>Null when actors would not run into it.</returns>
    public OrientedBox? SolidBoxOf(OtherObject placed)
    {
        var reference = placed.IsPrimitive ? primitives.GetValueOrDefault(placed.Key) : default;
        return SolidBoxOf(placed.Base, placed.IsPrimitive, placed.HasMapMarker, reference.CollisionLayer, reference.IsCullingVolume, reference.PrimitiveBounds, placed.Transform);
    }

    /// <remarks>The primitive bounds are full sizes, centred on the reference.</remarks>
    private OrientedBox? SolidBoxOf(
        BaseKey? model, bool isPrimitive, bool hasMapMarker, uint? collisionLayer, bool isCullingVolume, Vector3? primitiveBounds, PlacedTransform transform)
    {
        if (!BlocksActors(model, isPrimitive, hasMapMarker, collisionLayer, isCullingVolume)) return null;
        var local = isPrimitive && primitiveBounds is { } size ? Box.FromCorners(-size / 2f, size / 2f) : shapes.Of(model).Box;
        return OrientedBox.FromLocal(local, transform);
    }

    /// <summary>
    /// A primitive whose base has no mesh, marker bases included, blocks when the collision layer the reference stores blocks actors.
    /// One that stores none has its kind's default layer: activators, acoustic spaces and culling volumes let actors through, every
    /// other kind (collision markers included) blocks. Any other object blocks when it is visible or its collision stops characters.
    /// </summary>
    private bool BlocksActors(BaseKey? model, bool isPrimitive, bool hasMapMarker, uint? collisionLayer, bool isCullingVolume)
    {
        var shape = shapes.Of(model);
        if (isPrimitive && shape is { Resolved: true, MeshPath: null })
        {
            return collisionLayer is { } layer ? shapes.LayerBlocksActors(layer) : !isCullingVolume && !shape.PrimitiveWithoutLayerIsWalkThrough;
        }
        return shapes.VisibilityOf(model, isPrimitive, hasMapMarker).IsVisible || shape.CollisionBlocksMovement == true;
    }
}
