using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// The LAND grid points the changed-terrain step checks around the targets, each with the settings of its own collision group: in
/// every area the step grows around the target, where the step checks that area, in the worldspace whose land the target's space
/// uses. A later run or Batch step reads the patch's heights as the shown ones, so ground kept there gives that step no change. The
/// quarters' textures and the cells' water levels the step reads around them are named too, so they can be kept the same way.
/// Thread-safe.
/// </summary>
/// <param name="findLandWorldspace">A space → the worldspace whose land it uses; null for a space without land.</param>
/// <param name="settingsOf">The settings the step judges each target with, by its key.</param>
/// <param name="cloneSettings">The settings the step judges the cleaned mod's own objects with, and so a clone.</param>
internal sealed class TerrainStepAreas(
    IBaseObjectShapes shapes, Func<RecordKey, RecordKey?> findLandWorldspace, IReadOnlyDictionary<RecordKey, ChangedTerrainSettings> settingsOf, ChangedTerrainSettings cloneSettings)
{
    /// <summary>The points in the areas the step checks around each of the objects that is a target or a clone; none for any other.</summary>
    /// <remarks>
    /// An area the step checks only where it reaches the ground is kept when it reaches the ground as it is now, as the plan ends it or
    /// as the world without the cleaned mod has it: a later step admits it by the ground it accepts, which may be that earlier ground.
    /// </remarks>
    public IEnumerable<LandGridPoint> Under(IEnumerable<TargetObject> placed, TerrainUndoPlan plan) => placed.SelectMany(each => Under(each, plan));

    /// <summary>
    /// The terrain quarters holding a grid point of an area the step checks around each object that is a target or a clone, where its
    /// settings count terrain type changes; none for any other object. A quarter is listed once, in the order first met.
    /// </summary>
    /// <remarks>Every area the step grows counts, whether or not it reaches the ground: keeping a quarter's textures can only keep more.</remarks>
    public IEnumerable<(ExteriorCell Cell, int Quarter)> QuartersAround(IEnumerable<TargetObject> placed) =>
        placed.SelectMany(QuartersAround).Distinct();

    /// <summary>
    /// The areas the step checks around each object that is a target or a clone, where its settings count water changes, each with
    /// what keeps a water level for it: the object, or a clone's original; none for any other object.
    /// </summary>
    public IEnumerable<WaterCheckArea> WaterAreasAround(IEnumerable<TargetObject> placed) => placed.SelectMany(WaterAreasAround);

    private IEnumerable<LandGridPoint> Under(TargetObject placed, TerrainUndoPlan plan)
    {
        if (CheckedSettingsOf(placed) is not { } settings) return [];
        if (findLandWorldspace(placed.SpaceKey) is not { } land) return [];
        return AreasAround(placed, settings).SelectMany(area => InCheckedArea(land, area, settings, plan));
    }

    private IEnumerable<(ExteriorCell Cell, int Quarter)> QuartersAround(TargetObject placed)
    {
        if (CheckedSettingsOf(placed) is not { CountTerrainTypeChanges: true } settings) return [];
        if (findLandWorldspace(placed.SpaceKey) is not { } land) return [];
        return AreasAround(placed, settings)
            .SelectMany(area => GroundUnderObjects.PointsUnder(land, area))
            .SelectMany(point => TerrainQuadrants.Holding(point.Point).Select(quarter => (point.Cell, quarter)));
    }

    private IEnumerable<WaterCheckArea> WaterAreasAround(TargetObject placed)
    {
        if (CheckedSettingsOf(placed) is not { CountsWaterChanges: true } settings) return [];
        var (keeper, reason) = placed.CloneOf is { } original ? (original, WaterKeptReason.AroundCopy) : (placed.Key, WaterKeptReason.AroundObjectThatStays);
        return AreasAround(placed, settings).Select(area => new WaterCheckArea(placed.SpaceKey, area, keeper, reason));
    }

    /// <returns>The settings the step judges the object with when it checks the object; null when it does not.</returns>
    private ChangedTerrainSettings? CheckedSettingsOf(TargetObject placed) =>
        SettingsOf(placed) is { } settings && settings.Runs && settings.Checks(shapes.VisibilityOf(placed).IsVisible) ? settings : null;

    private IEnumerable<Box> AreasAround(TargetObject placed, ChangedTerrainSettings settings) => settings.AreasAround(shapes.Of(placed.Base).Box, placed.Transform);

    /// <summary>A clone carries its original's key, whose settings are those of another group.</summary>
    private ChangedTerrainSettings? SettingsOf(TargetObject placed) => placed.CloneOf is not null ? cloneSettings : settingsOf.GetValueOrDefault(placed.Key);

    private static IEnumerable<LandGridPoint> InCheckedArea(RecordKey land, Box area, ChangedTerrainSettings settings, TerrainUndoPlan plan)
    {
        List<LandGridPoint> points = [.. GroundUnderObjects.PointsUnder(land, area)];
        var planned = LandGridPoint.HeightsAt(points, plan.PlannedHeightsOf);
        return settings.ChecksArea(area, LandGridPoint.HeightsAt(points, plan.CurrentHeightsOf), planned)
            || settings.ChecksArea(area, LandGridPoint.HeightsAt(points, plan.HeightsWithoutCleanedModOf), planned)
            ? points
            : [];
    }
}
