using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static class SectorAreasText
{
    /// <summary>
    /// E.g. "East 120/400, NorthEast -, ..." as removed/total ground area; "-" for an empty direction. A direction with restored
    /// boxes names their area apart: "East 100 (+20 restored)/400".
    /// </summary>
    public static string Describe(SectorAreas areas) =>
        string.Join(", ", CompassDirections.All.Select(sector =>
        {
            if (areas.State(sector) == SectorState.Empty) return $"{sector} -";
            var restored = areas.Restored(sector) > 0 ? $" (+{TextFormat.Fixed(areas.Restored(sector), 0)} restored)" : string.Empty;
            return $"{sector} {TextFormat.Fixed(areas.Removed(sector), 0)}{restored}/{TextFormat.Fixed(areas.Total(sector), 0)}";
        }));
}
