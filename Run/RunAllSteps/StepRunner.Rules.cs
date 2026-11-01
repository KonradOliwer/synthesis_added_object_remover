using System.Collections.Immutable;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Run.RunAllSteps.RunCaches;
using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FitClashingObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;
using AddedObjectRemover.Steps.JudgeChangedObjects.Contracts;
using AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport;
using AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;
using AddedObjectRemover.Steps.WriteThePatch;

namespace AddedObjectRemover.Run.RunAllSteps;

public static partial class StepRunner
{
    /// <summary>The too-close rule of each collision group and its changed-terrain rule, which the fit step tries placements with.</summary>
    /// <remarks>
    /// Only a too-close clash is fitted. An object on changed terrain is removed as it stands, since shrinking or turning it does not
    /// change the ground under it; a shrunk or turned placement must not stand on changed terrain either.
    /// </remarks>
    private static ImmutableArray<FitGroup> CreateFitGroups(
        RunScope scope, IRunCaches runCaches, ModTrustTable trust, CollectedObjects collected, SelectedObjects selected, ImmutableArray<CollisionGroup> groups) =>
    [
        .. groups.Select(group => new FitGroup(
            group.Fit,
            Fittable: TooCloseObjects.CreatePlacementRule(
                collected.Targets, runCaches.VisibleTargets, selected, runCaches.PlacedNpcs, runCaches.Shapes, runCaches.Triangles, group.TooClose.Settings),
            Unfittable: ChangedTerrainObjects.CreatePlacementRule(
                trust.AcceptedWorld, scope.Environment.Plugins, scope.Plugin, runCaches.Shapes, group.ChangedTerrain.Settings))),
    ];

    /// <summary>The objects others rest on, which the fit step leaves to the removal rounds: target objects and other mods' objects alike.</summary>
    private static IRestingRule CreateFitRestingRule(RunScope scope, IRunCaches runCaches, ModTrustTable trust, CollectedObjects collected, HeldUpTargets heldUp)
    {
        var targetObjectsResting = RestingObjects.CreateRestingRule(
            collected.Targets,
            scope.Settings.AlsoRemove,
            runCaches.Shapes,
            runCaches.Triangles,
            runCaches.SurfaceSamples,
            runCaches.Terrain,
            runCaches.VisibleObjectsOfAnyPlugin,
            CreateTargetObjectsDropTest(scope, runCaches, trust, collected),
            scope.Settings.Execution);
        return OtherModsObjects.CreateRestingRule(
            targetObjectsResting,
            collected.Targets,
            runCaches.VisibleTargets,
            trust,
            scope.Environment.Plugins,
            scope.Settings.OtherModsObjects,
            runCaches.Shapes,
            runCaches.Triangles,
            runCaches.SurfaceSamples,
            runCaches.Terrain,
            runCaches.VisibleObjectsOfAnyPlugin,
            heldUp,
            scope.Settings.Execution);
    }

    private static ITargetObjectsDropTest CreateTargetObjectsDropTest(RunScope scope, IRunCaches runCaches, ModTrustTable trust, CollectedObjects collected) =>
        OtherModsObjects.CreateDropTest(
            collected.Targets,
            runCaches.VisibleTargets,
            trust,
            scope.Environment.Plugins,
            scope.Settings.AlsoRemove,
            runCaches.Shapes,
            runCaches.Triangles,
            runCaches.SurfaceSamples,
            runCaches.Terrain,
            runCaches.VisibleObjectsOfAnyPlugin,
            scope.Settings.Execution);

    /// <param name="decided">The decisions the follow-ups start from, whose rounds measure the target objects.</param>
    private static IAlsoRemoveFollowUp CreateAlsoRemoveFollowUp(RunScope scope, IRunCaches runCaches, CollectedObjects collected, IRemovalDecisions decided) =>
        RestingObjects.CreateFollowUp(
            decided.TargetsAsTheRoundsMeasureThem(collected.Targets),
            scope.Settings.AlsoRemove,
            scope.Settings.Execution,
            scope.Environment.TimeSinceStart,
            runCaches.Shapes,
            runCaches.Triangles,
            runCaches.SurfaceSamples,
            runCaches.Terrain,
            runCaches.VisibleObjectsOfAnyPlugin);

    private static IAlsoRemoveRoundsAfter CreateAlsoRemoveRoundsAfter(RunScope scope, IRunCaches runCaches) =>
        RestingObjects.CreateAlsoRemoveRoundsAfter(
            scope.Settings.AlsoRemove, runCaches.Shapes, runCaches.Triangles, runCaches.SurfaceSamples, runCaches.Terrain, runCaches.VisibleObjectsOfAnyPlugin, scope.Settings.Execution);

    /// <summary>The run's target objects measured among the causes of the old-place world; its caches are built when the first earlier version is checked.</summary>
    private static IOldPlaceTooCloseRule CreateOldPlaceTooCloseRule(RunScope scope, IRunCaches runCaches, CollectedObjects collected) =>
        new OldPlaceTooCloseRule(
            runCaches,
            collected.Targets,
            new ObjectCachesOfWorlds(scope.LoadOrderCaches, RemovalCauseSelector.MeasurementRules(scope.UnexpectedErrors), scope.Settings.Execution),
            scope.Settings.ChangedObjectCollision.TooClose);

    private static IPlacementRule CreateOldPlaceChangedTerrainRule(RunScope scope, IRunCaches runCaches, ModTrustTable trust) =>
        ChangedTerrainObjects.CreatePlacementRule(
            trust.AcceptedWorldWithoutCleanedMod, scope.Environment.Plugins, scope.Plugin, runCaches.Shapes, scope.Settings.ChangedObjectCollision.ChangedTerrain);

    /// <remarks>
    /// The object caches of the world the restore's guards judge in are built only when the step asks, so a run with no placed record
    /// to guard builds none; the step gives them only the objects its guards can reach. Its rules share those caches.
    /// </remarks>
    private static WhereRemovedRestorer.Input CreateRestoreInput(
        RunScope scope,
        IRunCaches runCaches,
        ModTrustTable trust,
        IVisibleTargetObjects visibleTargets,
        CollectedObjects collected,
        OtherModsObjectsResult otherModsObjects,
        IRemovalDecisions final)
    {
        var worldCaches = new ObjectCachesOfWorlds(
            scope.LoadOrderCaches, RemovalCauseSelector.MeasurementRules(scope.UnexpectedErrors, RestoreWhereRemovedParts.ObjectThatStays), scope.Settings.Execution);
        return new WhereRemovedRestorer.Input(
            collected,
            final,
            otherModsObjects,
            visibleTargets,
            worldCaches,
            new TooCloseToWhatStaysRule(runCaches, worldCaches),
            CreateHoldersOfObjectsThatStay(scope, runCaches, trust, worldCaches),
            PatchWriter.CreateNavmeshCheck(scope.Plugin, scope.Environment.PatchWriting),
            scope.Settings.OtherModsObjects,
            runCaches.Shapes,
            runCaches.Navmeshes,
            scope.Plugin,
            scope.Environment.Plugins,
            trust.CleanedMod,
            scope.Settings.Execution,
            scope.Settings.RestoreWhereRemoved);
    }

    private static IHoldersOfObjectsThatStay CreateHoldersOfObjectsThatStay(RunScope scope, IRunCaches runCaches, ModTrustTable trust, IObjectCachesOfWorlds worldCaches) =>
        OtherModsObjects.CreateHoldersOfObjectsThatStay(
            worldCaches,
            trust,
            scope.Environment.Plugins,
            scope.Settings.OtherModsObjects,
            runCaches.Shapes,
            runCaches.Triangles,
            runCaches.SurfaceSamples,
            runCaches.Terrain,
            scope.Settings.Execution);

    /// <summary>
    /// The too-close rule that checks <paramref name="targets"/> among the objects of another world than the run's, every one of them
    /// a cause except <paramref name="leftOut"/>; the old-place check and the restore's guards both judge with it.
    /// </summary>
    private static IPlacementRule CreateOtherWorldTooCloseRule(
        IRunCaches runCaches,
        IReadOnlyList<TargetObject> targets,
        IVisibleTargetObjects visibleTargets,
        CollectedObjects world,
        IObjectCaches worldCaches,
        IEnumerable<OtherId> leftOut,
        TooCloseSettings settings) =>
        TooCloseObjects.CreatePlacementRule(
            targets,
            visibleTargets,
            RemovalCauseSelector.SelectForOldPlace(worldCaches.OtherModObjects, world.OtherModObjects.Length, leftOut, settings.Npcs),
            worldCaches.PlacedNpcs,
            runCaches.Shapes,
            runCaches.Triangles,
            settings);

    /// <param name="targets">The run's target objects, measured where the cleaned mod put them.</param>
    private sealed class OldPlaceTooCloseRule(
        IRunCaches runCaches, IReadOnlyList<TargetObject> targets, IObjectCachesOfWorlds worldCaches, TooCloseSettings settings) : IOldPlaceTooCloseRule
    {
        public IPlacementRule Create(CollectedObjects oldPlaceWorld, OtherId leftOut) =>
            CreateOtherWorldTooCloseRule(runCaches, targets, runCaches.VisibleTargets, oldPlaceWorld, worldCaches.Of(oldPlaceWorld), [leftOut], settings);
    }

    /// <summary>The candidates of a world of what stays, its target objects, measured among its other objects.</summary>
    private sealed class TooCloseToWhatStaysRule(IRunCaches runCaches, IObjectCachesOfWorlds worldCaches) : ITooCloseToWhatStaysRule
    {
        public IPlacementRule Create(CollectedObjects world, IEnumerable<OtherId> leftOut, TooCloseSettings settings)
        {
            var caches = worldCaches.Of(world);
            return CreateOtherWorldTooCloseRule(runCaches, world.Targets, caches.VisibleTargets, world, caches, leftOut, settings);
        }
    }
}
