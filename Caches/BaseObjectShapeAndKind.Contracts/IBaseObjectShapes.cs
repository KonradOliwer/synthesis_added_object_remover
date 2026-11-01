namespace AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;

/// <summary>The shape, size and kind of each base object, measured once per base.</summary>
public interface IBaseObjectShapes
{
    /// <summary>A null or unknown base yields <see cref="BaseShape.Missing"/>.</summary>
    BaseShape Of(BaseKey? baseKey);

    /// <summary>
    /// Whether a placed object with this base can be seen in game. A primitive box reference
    /// (trigger/activator volume) only counts as visible when its base has a visible mesh; otherwise
    /// it is a trigger box, unless the base's record type or marker flag names another invisible kind.
    /// </summary>
    ObjectVisibility VisibilityOf(BaseKey? baseKey, bool isPrimitive, bool hasMapMarker);

    /// <summary>
    /// Whether something on the collision layer stops characters or is kept out of by the navmesh; <paramref name="collisionLayer"/> is the
    /// layer number a reference stores (XTRI), numbered as the collision layer records and the mesh files number them.
    /// </summary>
    bool LayerBlocksActors(uint collisionLayer);

    /// <summary>Every base measured so far, in no particular order. Statistics are counted from it.</summary>
    IReadOnlyList<MeasuredBase> Computed();
}
