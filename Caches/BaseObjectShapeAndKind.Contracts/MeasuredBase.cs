namespace AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;

/// <summary>Where a base object's bounding box came from.</summary>
public enum BoundsSource
{
    /// <summary>The base record was not found.</summary>
    Unresolved,

    /// <summary>The base is a marker kind, which is measured as a point without reading anything else.</summary>
    Marker,

    MeshFile,
    ObjectBounds,

    /// <summary>The base has an effect-only mesh (fog, light rays) and no mesh bounds are used.</summary>
    EffectOnlyMesh,

    /// <summary>Neither a mesh nor Object Bounds gave a box.</summary>
    None,
}

/// <param name="MeshGaveNoBounds">The base has a model, but its mesh gave no bounds.</param>
public readonly record struct MeasuredBase(BaseShape Shape, BoundsSource Source, bool MeshGaveNoBounds);
