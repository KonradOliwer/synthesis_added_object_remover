namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;

/// <summary>The part of the changed-terrain step that records its unexpected errors.</summary>
public static class ChangedTerrainParts
{
    private const string Owner = "RemoveObjectsOnChangedTerrain";

    /// <summary>Checking the terrain around one target object; the subject is the object.</summary>
    public static readonly PartKey TerrainCheck = new(Owner, nameof(TerrainCheck));
}
