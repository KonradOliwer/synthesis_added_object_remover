using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>The guards between the kinds, with what the loop reads, and the time of building the after-removal world.</summary>
/// <param name="Placed">None when no placed record's area is cleared.</param>
/// <param name="Terrain">None when the terrain is not restored.</param>
internal sealed record RestoreGuardsBuilt(PlacedGuards? Placed, TerrainGuards? Terrain, RestoreLoopInputs LoopInputs, TimeSpan WorldTime);

internal static class RestoreGuardsBuilder
{
    public static RestoreGuardsBuilt Build(WhereRemovedRestorer.StepInputs inputs, WhereRemovedRestorer.Plans plans)
    {
        var input = inputs.Input;
        var (collected, settings, shapes) = (input.Earlier.Collected, inputs.Settings, input.Reads.Caches.Shapes);
        var reach = new GuardReach(shapes, settings.TooClose, input.Guards.HoldersOfObjectsThatStay.TouchGap);
        var (world, worldTime) = Timed.Run(
            input.TimeSinceStart, () => AfterRemovalWorld.Of(collected, inputs.Final, inputs.OtherModsObjects, input.Reads.CleanedMod, plans.Cleared, reach));
        var blocking = new ObjectsThatBlockActors(shapes, collected.WinningPrimitives);
        var landHeights = plans.LandHeights;
        var terrainGuards = settings.Terrain
            ? new TerrainGuards(() => ObjectsOfTheRun.OtherPluginsObjectsThatStay(world, collected, plans.Cleared), plans.Ground, plans.SpacesOnLand, cell => landHeights(cell).Current, settings.HeightChangeTolerance, input.Execution)
            : null;
        return new RestoreGuardsBuilt(
            plans.Cleared.Count == 0 ? null : BuildPlaced(inputs, plans, world, reach, blocking),
            terrainGuards,
            new RestoreLoopInputs(
                world,
                new StandingObjects(world, blocking, plans.FinalPresence),
                plans.TargetsThatMayStay,
                plans.Ground,
                new TerrainStepAreas(shapes, inputs.FindLandWorldspace, TerrainSettingsByTarget(collected, input.RemovalSteps)),
                inputs.FindLandWorldspace,
                (plan, cell) => landHeights(cell) with { Current = plan.PlannedHeightsOf(cell) ?? landHeights(cell).Current },
                settings.HeightChangeTolerance,
                input.Reads.Caches.Navmeshes,
                NavmeshInputs.EveryNavmeshWithChangedTriangles(collected),
                NavmeshInputs.ChangedNavmeshTriangles(collected),
                plans.FinalPresence,
                plans.Doors),
            worldTime);
    }

    private static PlacedGuards BuildPlaced(
        WhereRemovedRestorer.StepInputs inputs, WhereRemovedRestorer.Plans plans, AfterRemovalWorld world, GuardReach reach, ObjectsThatBlockActors blocking)
    {
        var (input, guards, runCaches) = (inputs.Input, inputs.Input.Guards, inputs.Input.Reads.Caches);
        var caches = guards.WorldCaches.Of(world.World);
        var tooCloseSettings = inputs.Settings.TooClose;
        return new PlacedGuards(
            world,
            new PlacedGuardInputs(
                caches,
                leftOut => guards.TooClose.Create(world.World, leftOut, tooCloseSettings),
                GroupTooCloseGuard.Of(world, plans.Cleared, plans.TargetsThatMayStay, input.RemovalSteps, reach, input.Reads.CleanedMod, guards.TooClose.Create, input.Execution),
                guards.HoldersOfObjectsThatStay.Over(world.World, world.Now),
                guards.HoldersOfObjectsThatStay.Over(world.GoingBackWorld, world.Holders),
                runCaches.Shapes,
                blocking,
                runCaches.Navmeshes,
                inputs.FindLandWorldspace,
                input.Reads.Plugin.ReadTerrainWinner),
            reach,
            plans.FinalPresence,
            new DoorLinkGuard(plans.Cleared, input.Earlier.Collected.ChangedNavmeshes, guards.NavmeshCheck.FindFaultIfWritten),
            input.Execution);
    }

    /// <summary>The changed-terrain settings each target is judged with, by its key.</summary>
    private static Dictionary<RecordKey, ChangedTerrainSettings> TerrainSettingsByTarget(CollectedObjects collected, WhereRemovedRestorer.RemovalStepJudging groups) =>
        groups.ChangedTerrain.SelectMany(group => group.Objects.Select(target => KeyValuePair.Create(collected.Targets[target.Index].Key, group.Settings))).ToDictionary();
}
