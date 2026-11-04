using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// The LAND grid points the changed-terrain step checks around the targets, each with the settings of its own collision group: in
/// every area the step grows around the target, where the step checks that area, in the worldspace whose land the target's space
/// uses. A later run or Batch step reads the patch's heights as the shown ones, so ground kept there gives that step no change. The
/// quarters' textures and the ground under the water lines the step reads in those areas are named too, so they can be kept the same
/// way; textures whose version below the cleaned mod's is one the step accepts are not, since a later run accepts them back.
/// Thread-safe.
/// </summary>
/// <param name="findLandWorldspace">A space → the worldspace whose land it uses; null for a space without land.</param>
/// <param name="settingsOf">The settings the step judges each target with, by its key.</param>
/// <param name="cloneSettings">The settings the step judges the cleaned mod's own objects with, and so a clone.</param>
/// <param name="winningHeights">A cell's winning heights; null when it has none.</param>
/// <param name="isAccepted">Whether the step accepts the values of a plugin's version whatever the others hold.</param>
internal sealed class TerrainStepAreas(
    IBaseObjectShapes shapes,
    Func<RecordKey, RecordKey?> findLandWorldspace,
    IReadOnlyDictionary<RecordKey, ChangedTerrainSettings> settingsOf,
    ChangedTerrainSettings cloneSettings,
    Func<ExteriorCell, float[]?> winningHeights,
    Func<PluginName, bool> isAccepted)
{
    /// <summary>
    /// The points in the areas the step checks around each of the objects that is a target or a clone, where its settings count height
    /// changes; none for any other.
    /// </summary>
    /// <remarks>
    /// An area the step checks only where it reaches the ground is kept when it reaches the ground as it is now, as the plan ends it or
    /// as the world without the cleaned mod has it: a later step admits it by the ground it accepts, which may be that earlier ground.
    /// </remarks>
    public IEnumerable<LandGridPoint> Under(IEnumerable<TargetObject> placed, TerrainUndoPlan plan) => PointsAround(placed, plan, settings => settings.CountHeightChanges);

    /// <summary>
    /// The terrain quarters holding a grid point of an area the step checks around each object that is a target or a clone, where its
    /// settings count terrain type changes, and whose LAND record's version below the cleaned mod's the step does not accept; none for
    /// any other object. A quarter is listed once, in the order first met.
    /// </summary>
    public IEnumerable<(ExteriorCell Cell, int Quarter)> QuartersAround(IEnumerable<TargetObject> placed, TerrainUndoPlan plan) =>
        PointsAround(placed, plan, settings => settings.CountTerrainTypeChanges)
            .Where(point => plan.EarlierPluginOf(point.Cell) is not { } earlier || !isAccepted(earlier))
            .SelectMany(point => TerrainQuadrants.Holding(point.Point).Select(quarter => (point.Cell, quarter)))
            .Distinct();

    /// <summary>
    /// The areas the step checks around each object that is a target or a clone, where its settings count water changes, each with
    /// the ground of its points as the plan ends it and what keeps a water level for it: the object, or a clone's original; none for
    /// any other object.
    /// </summary>
    public IEnumerable<WaterCheckArea> WaterAreasAround(IEnumerable<TargetObject> placed, TerrainUndoPlan plan) =>
        placed.SelectMany(each => WaterAreasAround(each, plan));

    private IEnumerable<WaterCheckArea> WaterAreasAround(TargetObject placed, TerrainUndoPlan plan)
    {
        var (keeper, reason) = placed.CloneOf is { } original ? (original, WaterKeptReason.AroundCopy) : (placed.Key, WaterKeptReason.AroundObjectThatStays);
        return CheckedAreasAround(placed, plan, settings => settings.CountsWaterChanges)
            .Select(area => new WaterCheckArea(
                placed.SpaceKey,
                [.. GroundOf(area.Points, area.Settings.CountHeightChanges ? plan.CurrentHeightsOf : plan.PlannedHeightsOf)],
                area.Settings.MinWaterCoverChangePercent,
                keeper,
                reason));
    }

    /// <summary>
    /// Each point's height from <paramref name="heightsOf"/>, or as the game shows it where that holds none; a point without a height is
    /// left out. Where the step counts height changes, <see cref="Under"/> keeps the area's ground as the game shows it.
    /// </summary>
    private IEnumerable<GroundPoint> GroundOf(IEnumerable<LandGridPoint> points, Func<ExteriorCell, float[]?> heightsOf)
    {
        foreach (var point in points)
        {
            if ((heightsOf(point.Cell) ?? winningHeights(point.Cell))?[point.Point] is { } height) yield return new GroundPoint(point.Cell.X, point.Cell.Y, height);
        }
    }

    private IEnumerable<LandGridPoint> PointsAround(IEnumerable<TargetObject> placed, TerrainUndoPlan plan, Func<ChangedTerrainSettings, bool> count) =>
        placed.SelectMany(each => CheckedAreasAround(each, plan, count)).SelectMany(area => area.Points);

    /// <summary>The points of each area the step checks around the object with settings that <paramref name="count"/>; none when it does not check the object.</summary>
    private IEnumerable<CheckedArea> CheckedAreasAround(TargetObject placed, TerrainUndoPlan plan, Func<ChangedTerrainSettings, bool> count)
    {
        if (SettingsOf(placed) is not { } settings || !settings.Runs || !settings.Checks(shapes.VisibilityOf(placed).IsVisible) || !count(settings)) return [];
        if (findLandWorldspace(placed.SpaceKey) is not { } land) return [];
        return settings.AreasAround(shapes.Of(placed.Base).Box, placed.Transform)
            .Select(area => new CheckedArea(settings, InCheckedArea(land, area, settings, plan)))
            .Where(area => area.Points.Count > 0);
    }

    /// <summary>A clone carries its original's key, whose settings are those of another group.</summary>
    private ChangedTerrainSettings? SettingsOf(TargetObject placed) => placed.CloneOf is not null ? cloneSettings : settingsOf.GetValueOrDefault(placed.Key);

    private static List<LandGridPoint> InCheckedArea(RecordKey land, Box area, ChangedTerrainSettings settings, TerrainUndoPlan plan)
    {
        List<LandGridPoint> points = [.. GroundUnderObjects.PointsUnder(land, area)];
        var planned = LandGridPoint.HeightsAt(points, plan.PlannedHeightsOf);
        return settings.ChecksArea(area, LandGridPoint.HeightsAt(points, plan.CurrentHeightsOf), planned)
            || settings.ChecksArea(area, LandGridPoint.HeightsAt(points, plan.HeightsWithoutCleanedModOf), planned)
            ? points
            : [];
    }

    private sealed record CheckedArea(ChangedTerrainSettings Settings, List<LandGridPoint> Points);
}
