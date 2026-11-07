using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

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
        var (world, worldTime) = Timed.Run(
            input.TimeSinceStart, () => AfterRemovalWorld.Of(input.Earlier.Collected, inputs.Final, inputs.OtherModsObjects, input.Reads.CleanedMod, plans.Cleared, inputs.Reach));
        return new RestoreGuardsBuilt(
            plans.Cleared.Count == 0 ? null : BuildPlaced(inputs, plans, world),
            inputs.Settings.Terrain ? new TerrainGuards(plans.SpacesOnLand, inputs.Settings.HeightChangeTolerance, input.Execution) : null,
            BuildLoopInputs(inputs, plans, world),
            worldTime);
    }

    private static RestoreLoopInputs BuildLoopInputs(WhereRemovedRestorer.StepInputs inputs, WhereRemovedRestorer.Plans plans, AfterRemovalWorld world)
    {
        var (input, collected) = (inputs.Input, inputs.Input.Earlier.Collected);
        return new RestoreLoopInputs(
            world,
            new StandingObjects(world, inputs.Blocking),
            plans.TargetsThatMayStay,
            collected.CleanedModsObjectsOverriddenLater,
            plans.Ground,
            inputs.TerrainChecks,
            inputs.FindLandWorldspace,
            (plan, cell) => plan.GroundBeforeAndAfter(cell, plans.LandHeights(cell)),
            inputs.Settings.HeightChangeTolerance,
            input.Reads.Caches.Navmeshes,
            inputs.NavmeshesWithChangedTriangles,
            inputs.ChangedNavmeshTriangles,
            plans.FinalPresence,
            plans.Doors,
            new FollowUpOfGoingBack(input.Guards.FollowUp, inputs.Final, inputs.OtherModsObjects.TakenAway(), plans.Cleared, collected, inputs.LandUnder),
            inputs.LandAfter.Of,
            new ComputedOnce<StandingTooCloseChecks>(() => BuildStandingTooCloseChecks(inputs, plans, world)),
            BuildBuriedByGroundGoingUp(inputs, plans, world));
    }

    private static StandingTooCloseChecks BuildStandingTooCloseChecks(WhereRemovedRestorer.StepInputs inputs, WhereRemovedRestorer.Plans plans, AfterRemovalWorld world)
    {
        var input = inputs.Input;
        return StandingTooCloseChecks.Of(
            input.Earlier.Collected,
            inputs.OtherModsObjects,
            world,
            plans.Cleared,
            plans.TargetsThatMayStay,
            plans.Start.GoingBackWithClone(),
            input.RemovalSteps.TooClose,
            input.RemovalSteps.CleanedModsObjects.TooClose,
            plans.FinalPresence,
            input.Guards.TooClose,
            input.Execution);
    }

    private static ObjectsBuriedByGroundGoingUp BuildBuriedByGroundGoingUp(WhereRemovedRestorer.StepInputs inputs, WhereRemovedRestorer.Plans plans, AfterRemovalWorld world)
    {
        var input = inputs.Input;
        return new ObjectsBuriedByGroundGoingUp(
            ObjectsInPlace(world, plans.Cleared, input.Earlier.Collected, input.Reads.CleanedMod),
            inputs.PulledDownByOtherModsStep,
            input.RemovalSteps.Accepted,
            input.Reads.Caches.Shapes,
            input.Reads.Caches.Terrain,
            inputs.Settings.HeightChangeTolerance);
    }

    /// <summary>
    /// The objects that stand in the final world where they stand now, of other plugins and the cleaned mod's objects later plugins
    /// hold: neither the cleaned mod's own targets nor the placed records whose areas let them go back.
    /// </summary>
    private static IEnumerable<OtherObject> ObjectsInPlace(AfterRemovalWorld world, IReadOnlyList<RecordChangedByCleanedMod> cleared, CollectedObjects collected, PluginName cleanedMod)
    {
        var candidates = cleared.Select(change => change.Key).ToHashSet();
        var overriddenLater = collected.CleanedModsObjectsOverriddenLater.Select(overridden => overridden.Key).ToHashSet();
        return world.Causes.Concat(world.SupportOnly)
            .Where(other => (other.Key.Plugin != cleanedMod || overriddenLater.Contains(other.Key)) && !candidates.Contains(other.Key));
    }

    private static PlacedGuards BuildPlaced(WhereRemovedRestorer.StepInputs inputs, WhereRemovedRestorer.Plans plans, AfterRemovalWorld world)
    {
        var (blocking, changedTriangles) = (inputs.Blocking, inputs.ChangedNavmeshTriangles);
        var (input, guards, runCaches, collected) = (inputs.Input, inputs.Input.Guards, inputs.Input.Reads.Caches, inputs.Input.Earlier.Collected);
        var tooCloseSettings = inputs.Settings.TooClose;
        return new PlacedGuards(
            world,
            new PlacedGuardInputs(
                (checkedWorld, leftOut) => guards.TooClose.Create(checkedWorld, leftOut, tooCloseSettings),
                blocking,
                runCaches.Navmeshes,
                CloneGuards.Of(world, blocking, collected.ChangedNavmeshes, changedTriangles, input.Execution)),
            new DoorLinkGuard(plans.Cleared, collected.ChangedNavmeshes, guards.NavmeshCheck.FindFaultIfWritten),
            input.Execution);
    }
}
