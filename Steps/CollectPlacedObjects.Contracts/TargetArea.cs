namespace AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

/// <summary>Where a target object's place name comes from.</summary>
public enum PlaceSource
{
    /// <summary>The Location of the object's exterior cell.</summary>
    OwnLocation,

    /// <summary>The name of the object's cell: an interior cell, or an exterior cell without a Location.</summary>
    CellName,

    /// <summary>The Location of the nearest cell that has one, as the object's cell has neither a Location nor a name.</summary>
    NearbyLocation,

    /// <summary>Nothing names it; only its exterior cell grid position is known.</summary>
    GridOnly,
}

/// <summary>
/// Where a target object is below its space, as the load-order winners name it (display name, else editor id); the
/// space's own name is <see cref="SpaceFact.Name"/>.
/// </summary>
/// <param name="Hold">The hold of the object's Location, or of the nearest cell's; null when no Location names it.</param>
/// <param name="Place">The most specific name; null only with <see cref="PlaceSource.GridOnly"/>, or for an interior cell without a name.</param>
/// <param name="Grid">The exterior cell, from the object's position when it is in a worldspace's persistent cell; null for an interior.</param>
public sealed record TargetArea(string? Hold, string? Place, PlaceSource PlaceSource, CellGridPoint? Grid);
