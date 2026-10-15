using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static class SectorAreasText
{
    /// <summary>E.g. "East 120/400, NorthEast -, ..." as removed/total ground area; "-" for an empty direction.</summary>
    public static string Describe(SectorAreas areas) =>
        string.Join(", ", CompassDirections.All.Select(sector =>
            areas.State(sector) == SectorState.Empty
                ? $"{sector} -"
                : $"{sector} {TextFormat.Fixed(areas.Removed(sector), 0)}/{TextFormat.Fixed(areas.Total(sector), 0)}"));
}
