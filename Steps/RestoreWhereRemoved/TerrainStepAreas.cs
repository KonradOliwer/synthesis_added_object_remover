using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// The LAND grid points the changed-terrain step checks around the targets, each with the settings of its own collision group: in
/// every area the step grows around the target, where the step checks that area, in the worldspace whose land the target's space
/// uses. A later run or Batch step reads the patch's heights as the shown ones, so ground kept there gives that step no change.
/// Thread-safe.
/// </summary>
/// <param name="findLandWorldspace">A space → the worldspace whose land it uses; null for a space without land.</param>
/// <param name="settingsOf">The settings the step judges each target with, by its key.</param>
internal sealed class TerrainStepAreas(
    IBaseObjectShapes shapes, Func<RecordKey, RecordKey?> findLandWorldspace, IReadOnlyDictionary<RecordKey, ChangedTerrainSettings> settingsOf)
{
    /// <summary>The points in the areas the step checks around each of the objects that is a target; none for any other.</summary>
    /// <remarks>An area the step checks only where it reaches the ground is kept when it reaches the ground as it is now or as the plan ends it.</remarks>
    public IEnumerable<LandGridPoint> Under(IEnumerable<TargetObject> placed, TerrainUndoPlan plan) => placed.SelectMany(each => Under(each, plan));

    private IEnumerable<LandGridPoint> Under(TargetObject placed, TerrainUndoPlan plan)
    {
        if (!settingsOf.TryGetValue(placed.Key, out var settings) || !settings.Runs || !settings.Checks(shapes.VisibilityOf(placed).IsVisible)) return [];
        if (findLandWorldspace(placed.SpaceKey) is not { } land) return [];
        return settings.AreasAround(shapes.Of(placed.Base).Box, placed.Transform).SelectMany(area => InCheckedArea(land, area, settings, plan));
    }

    private static IEnumerable<LandGridPoint> InCheckedArea(RecordKey land, Box area, ChangedTerrainSettings settings, TerrainUndoPlan plan)
    {
        List<LandGridPoint> points = [.. GroundUnderObjects.PointsUnder(land, area)];
        return settings.ChecksArea(area, LandGridPoint.HeightsAt(points, plan.CurrentHeightsOf), LandGridPoint.HeightsAt(points, plan.PlannedHeightsOf)) ? points : [];
    }
}
