using System.Collections.Immutable;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Run.RunAllSteps.RunCaches;
using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
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
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;
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
                new(collected.Targets, runCaches.VisibleTargets, selected, runCaches.PlacedNpcs, runCaches.Shapes, runCaches.Triangles, group.TooClose.Settings)),
            Unfittable: ChangedTerrainObjects.CreatePlacementRule(
                new(trust.AcceptedWorld, scope.Environment.Plugins, scope.Plugin, runCaches.Shapes, group.ChangedTerrain.Settings)))),
    ];

    /// <summary>The objects others rest on, which the fit step leaves to the removal rounds: target objects and other mods' objects alike.</summary>
    private static IRestingRule CreateFitRestingRule(RunScope scope, IRunCaches runCaches, ModTrustTable trust, CollectedObjects collected, HeldUpTargets heldUp) =>
        OtherModsObjects.CreateRestingRule(
            new(CreateSupportJudging(scope, runCaches, trust, collected), CreateTargetObjectsRestingRule(scope, runCaches, trust, collected), heldUp));

    private static IRestingRule CreateTargetObjectsRestingRule(RunScope scope, IRunCaches runCaches, ModTrustTable trust, CollectedObjects collected) =>
        RestingObjects.CreateRestingRule(
            new(collected.Targets, scope.Settings.AlsoRemove, CreateTargetObjectsDropTest(scope, runCaches, trust, collected), scope.Settings.Execution, runCaches));

    private static ITargetObjectsDropTest CreateTargetObjectsDropTest(RunScope scope, IRunCaches runCaches, ModTrustTable trust, CollectedObjects collected) =>
        OtherModsObjects.CreateDropTest(new(CreateSupportJudging(scope, runCaches, trust, collected), scope.Settings.AlsoRemove));

    private static IHeldUpTargetsSearch CreateHeldUpTargetsSearch(RunScope scope, IRunCaches runCaches, ModTrustTable trust, CollectedObjects collected) =>
        OtherModsObjects.CreateHeldUpTargetsSearch(CreateSupportJudging(scope, runCaches, trust, collected));

    /// <summary>What every part of the step for other mods' objects reads, which measures support by the also-remove step's rule.</summary>
    private static OtherModsObjects.SupportJudging CreateSupportJudging(RunScope scope, IRunCaches runCaches, ModTrustTable trust, CollectedObjects collected) =>
        new(
            collected.Targets,
            runCaches,
            trust,
            scope.Environment.Plugins,
            scope.Settings.OtherModsObjects,
            runCaches.Shapes,
            runCaches.Triangles,
            RestingObjects.CreateSupportContacts(runCaches),
            runCaches.Terrain,
            scope.Settings.Execution);

    /// <param name="decided">The decisions the follow-ups start from, whose rounds measure the target objects.</param>
    private static IAlsoRemoveFollowUp CreateAlsoRemoveFollowUp(RunScope scope, IRunCaches runCaches, CollectedObjects collected, IRemovalDecisions decided) =>
        RestingObjects.CreateFollowUp(
            new(decided.TargetsAsTheRoundsMeasureThem(collected.Targets), scope.Settings.AlsoRemove, scope.Settings.Execution, scope.Environment.TimeSinceStart, runCaches));

    private static IAlsoRemoveRoundsAfter CreateAlsoRemoveRoundsAfter(RunScope scope, IRunCaches runCaches) =>
        RestingObjects.CreateAlsoRemoveRoundsAfter(new(scope.Settings.AlsoRemove, scope.Settings.Execution, runCaches));

    /// <summary>The run's target objects measured among the causes of the old-place world; its caches are built when the first earlier version is checked.</summary>
    private static IOldPlaceTooCloseRule CreateOldPlaceTooCloseRule(RunScope scope, IRunCaches runCaches, CollectedObjects collected)
    {
        var worldCaches = new ObjectCachesOfWorlds(scope.LoadOrderCaches, RemovalCauseSelector.MeasurementRules(scope.UnexpectedErrors), scope.Settings.Execution);
        return TooCloseObjects.CreateOldPlaceTooCloseRule(
            new(collected.Targets, runCaches, worldCaches, RemovalCauseSelector.CreateCausesOfWorlds(worldCaches), scope.Settings.ChangedObjectCollision.TooClose));
    }

    private static IPlacementRule CreateOldPlaceChangedTerrainRule(RunScope scope, IRunCaches runCaches, ModTrustTable trust) =>
        ChangedTerrainObjects.CreatePlacementRule(
            new(trust.AcceptedWorldWithoutCleanedMod, scope.Environment.Plugins, scope.Plugin, runCaches.Shapes, scope.Settings.ChangedObjectCollision.ChangedTerrain));

    /// <remarks>
    /// The object caches of the world the restore's guards judge in are built only when the step asks, so a run with no placed record
    /// to guard builds none; the step gives them only the objects its guards can reach. Its rules share those caches, so they are
    /// built here with the measurement failures recorded under the restore step's part.
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
            TooCloseObjects.CreateTooCloseToWhatStaysRule(new(runCaches, worldCaches, RemovalCauseSelector.CreateCausesOfWorlds(worldCaches))),
            OtherModsObjects.CreateHoldersOfObjectsThatStay(new(CreateSupportJudging(scope, runCaches, trust, collected), worldCaches)),
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
}
