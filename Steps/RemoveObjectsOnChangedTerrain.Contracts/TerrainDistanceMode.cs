namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;

/// <summary>How far around an object the terrain is checked.</summary>
public enum TerrainDistanceMode
{
    /// <summary>A fixed distance in game units.</summary>
    GameUnits,

    /// <summary>A multiple of the object's own size on each axis.</summary>
    ObjectSize,
}
