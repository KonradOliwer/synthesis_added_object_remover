using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;

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
        var reach = inputs.Reach;
        var (world, worldTime) = Timed.Run(
            input.TimeSinceStart, () => AfterRemovalWorld.Of(collected, inputs.Final, inputs.OtherModsObjects, input.Reads.CleanedMod, plans.Cleared, reach));
        var blocking = new ObjectsThatBlockActors(shapes, collected.WinningPrimitives);
        var changedTriangles = NavmeshInputs.ChangedNavmeshTriangles(collected);
        var landHeights = plans.LandHeights;
        Func<PluginName, bool> isAccepted = plugin => input.RemovalSteps.Accepted.TrustOf(plugin) == VersionTrust.Full;
        var terrainGuards = settings.Terrain
            ? new TerrainGuards(() => ObjectsOfTheRun.OtherPluginsObjectsThatStay(world, collected, plans.Cleared), plans.Ground, plans.SpacesOnLand, cell => landHeights(cell).Current, settings.HeightChangeTolerance, input.Execution)
            : null;
        return new RestoreGuardsBuilt(
            plans.Cleared.Count == 0 ? null : BuildPlaced(inputs, plans, world, blocking, changedTriangles),
            terrainGuards,
            new RestoreLoopInputs(
                world,
                new StandingObjects(world, blocking),
                plans.TargetsThatMayStay,
                plans.Ground,
                new TerrainStepAreas(
                    shapes,
                    inputs.FindLandWorldspace,
                    TerrainSettingsByTarget(collected, input.RemovalSteps),
                    input.RemovalSteps.CleanedModsObjects.ChangedTerrainSettings,
                    cell => landHeights(cell).Current,
                    isAccepted),
                inputs.FindLandWorldspace,
                (plan, cell) => landHeights(cell) with { Current = plan.PlannedHeightsOf(cell) ?? landHeights(cell).Current },
                settings.HeightChangeTolerance,
                input.Reads.Caches.Navmeshes,
                NavmeshInputs.EveryNavmeshWithChangedTriangles(collected),
                changedTriangles,
                plans.FinalPresence,
                plans.Doors,
                isAccepted),
            worldTime);
    }

    /// <param name="changedTriangles">The triangles of every navmesh the cleaned mod changed or added as they stand, in key order of their navmeshes.</param>
    private static PlacedGuards BuildPlaced(
        WhereRemovedRestorer.StepInputs inputs,
        WhereRemovedRestorer.Plans plans,
        AfterRemovalWorld world,
        ObjectsThatBlockActors blocking,
        ImmutableArray<NavmeshTriangleInCell> changedTriangles)
    {
        var (input, guards, runCaches, collected) = (inputs.Input, inputs.Input.Guards, inputs.Input.Reads.Caches, inputs.Input.Earlier.Collected);
        var caches = guards.WorldCaches.Of(world.World);
        var tooCloseSettings = inputs.Settings.TooClose;
        return new PlacedGuards(
            world,
            new PlacedGuardInputs(
                caches,
                leftOut => guards.TooClose.Create(world.World, leftOut, tooCloseSettings),
                StandingTooCloseChecks.Of(
                    collected,
                    inputs.OtherModsObjects,
                    world,
                    plans.Cleared,
                    plans.TargetsThatMayStay,
                    plans.Start.GoingBackWithClone(),
                    input.RemovalSteps,
                    plans.FinalPresence,
                    guards.TooClose,
                    input.Execution),
                guards.HoldersOfObjectsThatStay.Over(world.World, world.Now),
                guards.HoldersOfObjectsThatStay.Over(world.GoingBackWorld, world.Holders),
                runCaches.Shapes,
                blocking,
                runCaches.Navmeshes,
                inputs.FindLandWorldspace,
                input.Reads.Plugin.ReadTerrainWinner,
                CloneGuards.Of(world, blocking, collected.ChangedNavmeshes, changedTriangles, input.Execution)),
            new DoorLinkGuard(plans.Cleared, collected.ChangedNavmeshes, guards.NavmeshCheck.FindFaultIfWritten),
            input.Execution);
    }

    /// <summary>The changed-terrain settings each target is judged with, by its key.</summary>
    private static Dictionary<RecordKey, ChangedTerrainSettings> TerrainSettingsByTarget(CollectedObjects collected, WhereRemovedRestorer.RemovalStepJudging groups) =>
        groups.ChangedTerrain.SelectMany(group => group.Objects.Select(target => KeyValuePair.Create(collected.Targets[target.Index].Key, group.Settings))).ToDictionary();
}
