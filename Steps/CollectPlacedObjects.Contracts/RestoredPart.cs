namespace AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

/// <summary>
/// One part of a record that a restore where the cleaned mod was removed gave back the value of the plugin just below the cleaned
/// mod. It holds no value: it holds only while the patch's version of the part still has the value of <paramref name="Source"/>.
/// </summary>
/// <param name="CleanedMod">The cleaned mod of the run that gave the part back.</param>
/// <param name="Record">The placed record, the cell record or the LAND record.</param>
/// <param name="Source">The plugin whose value the part took.</param>
public sealed record RestoredPart(PluginName CleanedMod, RecordKey Record, RestoredPartName Part, PluginName Source);

/// <summary>Which part of its record a restored part is.</summary>
public abstract record RestoredPartName
{
    /// <param name="Part">One single part of a placed record.</param>
    public sealed record Placed(PlacedParts Part) : RestoredPartName;

    public sealed record Cell(CellPartName Part) : RestoredPartName;

    /// <summary>The heights of a LAND record at the grid points the restore gave back, which count only there.</summary>
    public sealed record LandHeights(PointSet Points) : RestoredPartName;

    /// <param name="Quarter">By <see cref="TerrainQuadrants.IndexOf"/>.</param>
    public sealed record LandTextureQuarter(int Quarter) : RestoredPartName;
}
