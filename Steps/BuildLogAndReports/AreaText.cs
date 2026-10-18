using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>The three levels of the area tree, from the top.</summary>
internal enum AreaLevel
{
    Worldspace,
    Hold,
    Place,
}

/// <summary>The wording of where a target object is: its worldspace (or interior), hold and place.</summary>
internal static class AreaText
{
    private const string UnnamedHold = "unnamed area";
    public const string NearbyPrefix = "near ";
    private const string HoldSuffix = " hold";

    /// <summary>The space's display name, else its editor id, else its key.</summary>
    public static string Worldspace(SpaceFact space) => space.Name ?? RecordLabels.Of(space.Key, space.EditorId);

    public static string Hold(TargetArea area) => area.Hold ?? UnnamedHold;

    /// <remarks>An interior cell without any name is named like its space.</remarks>
    public static string Place(TargetArea area, SpaceFact space) => area switch
    {
        { PlaceSource: PlaceSource.NearbyLocation, Place: { } nearby } => NearbyPrefix + nearby,
        { Place: { } place } => place,
        { Grid: { } grid } => $"cell {Grid(grid)}",
        _ => Worldspace(space),
    };

    public static string Grid(CellGridPoint grid) => $"{grid.X},{grid.Y}";

    /// <summary>E.g. "place Loreius Farm, hold The Pale, grid 7,6"; an interior has no grid.</summary>
    public static string Detail(TargetArea area, SpaceFact space) =>
        $"place {Place(area, space)}, hold {Hold(area)}" + (area.Grid is { } grid ? $", grid {Grid(grid)}" : string.Empty);

    /// <summary>E.g. "Whiterun hold"; a name that already ends in the word "hold" (e.g. "Falkreath Hold") and an unnamed area stay as they are.</summary>
    public static string HoldLabel(TargetArea area) => area.Hold switch
    {
        null => UnnamedHold,
        { } hold when hold.EndsWith(HoldSuffix, StringComparison.OrdinalIgnoreCase) => hold,
        { } hold => hold + HoldSuffix,
    };
}
