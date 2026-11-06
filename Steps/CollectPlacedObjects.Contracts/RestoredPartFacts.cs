namespace AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

/// <summary>
/// The restored parts a record's write gives back, one per part, from what goes back to the version just below the cleaned mod.
/// </summary>
public static class RestoredPartFacts
{
    /// <returns>In flag order.</returns>
    public static IEnumerable<RestoredPart> ListPlaced(RecordKey record, EarlierVersion earlier, PluginName cleanedMod) =>
        Enum.GetValues<PlacedParts>()
            .Where(part => part != PlacedParts.None && earlier.Undo.HasFlag(part))
            .Select(part => new RestoredPart(cleanedMod, record, new RestoredPartName.Placed(part), earlier.Plugin));

    /// <remarks>Every cell part is copied from the version just below the cleaned mod, the one whose water fields <see cref="CellChangedByCleanedMod.EarlierWater"/> holds, so its plugin is the source of each part.</remarks>
    /// <returns>In the order of <paramref name="undone"/>.</returns>
    public static IEnumerable<RestoredPart> ListCell(CellChangedByCleanedMod cell, IEnumerable<PartChange> undone, PluginName cleanedMod) =>
        undone.Select(part => new RestoredPart(cleanedMod, cell.Cell, new RestoredPartName.Cell(part.Name), cell.EarlierWater.Plugin));

    /// <param name="heights">The grid points whose height goes back; one part naming all of them.</param>
    /// <param name="textures">The quarters whose texture layers go back; one part each.</param>
    /// <returns>The heights first, then the quarters in ascending order.</returns>
    public static IEnumerable<RestoredPart> ListLand(LandChangedByCleanedMod land, PointSet heights, QuarterSet textures, PluginName cleanedMod) =>
        (heights.IsEmpty ? [] : new RestoredPartName[] { new RestoredPartName.LandHeights(heights) })
            .Concat(textures.Quarters.Select(quarter => new RestoredPartName.LandTextureQuarter(quarter)))
            .Select(part => new RestoredPart(
                cleanedMod,
                land.LandKey,
                part,
                land.EarlierPlugin ?? throw new InvalidOperationException($"The LAND record {land.LandKey} goes back without the plugin of its earlier version.")));
}
