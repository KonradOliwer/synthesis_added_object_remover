namespace AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;

/// <summary>The part of the changed-terrain rule that records its user-data failures.</summary>
public static class ChangedTerrainParts
{
    /// <summary>The owner name the log and reports know these failures by.</summary>
    private const string Owner = "RemoveObjectsOnChangedTerrain";

    /// <summary>Checking the terrain around one target object; the subject is the object.</summary>
    public static readonly PartKey TerrainCheck = new(Owner, nameof(TerrainCheck));
}
