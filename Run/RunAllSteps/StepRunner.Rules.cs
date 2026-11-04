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
using AddedObjectRemover.Steps.RemoveObjectsInClearedAreas;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind;
using AddedObjectRemover.Steps.RemoveTooCloseObjects;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;
using AddedObjectRemover.Steps.WriteThePatch;

namespace AddedObjectRemover.Run.RunAllSteps;

public static partial class StepRunner
{
    /// <summary>The run's target objects among the objects that can cause removals.</summary>
    private static TooCloseObjects.TooCloseWorld CreateTooCloseWorld(IRunCaches runCaches, CollectedObjects collected, SelectedObjects selected) =>
        new(collected.Targets, runCaches.VisibleTargets, selected, runCaches.PlacedNpcs, runCaches.Shapes, runCaches.Triangles);

    /// <summary>The fittable and the unfittable placement rule of each collision group.</summary>
    private static ImmutableArray<FitGroup> CreateFitGroups(
        RunScope scope, IRunCaches runCaches, ModTrustTable trust, TooCloseObjects.TooCloseWorld tooCloseWorld, ImmutableArray<CollisionGroup> groups) =>
    [
        .. groups.Select(group => new FitGroup(
            group.Fit,
            Fittable: TooCloseObjects.CreatePlacementRule(new(tooCloseWorld, group.TooClose.Settings)),
            Unfittable: ChangedTerrainObjects.CreatePlacementRule(
                new(trust.AcceptedWorld, scope.Environment.Plugins, scope.Plugin, runCaches.Shapes, group.ChangedTerrain.Settings)))),
    ];

    /// <summary>The objects others rest on: target objects and other mods' objects alike.</summary>
    private static IRestingRule CreateFitRestingRule(
        RunScope scope, IRunCaches runCaches, CollectedObjects collected, OtherModsObjects.SupportJudging support, HeldUpTargets heldUp)
    {
        var drops = OtherModsObjects.CreateDropTest(new(support, scope.Settings.AlsoRemove));
        var targetObjects = RestingObjects.CreateRestingRule(new(collected.Targets, scope.Settings.AlsoRemove, drops, scope.Settings.Execution, runCaches));
        return OtherModsObjects.CreateRestingRule(new(support, targetObjects, heldUp));
    }

    /// <summary>What every part of the step for other mods' objects and every rule it provides read.</summary>
    private static OtherModsObjects.SupportJudging CreateSupportJudging(RunScope scope, IRunCaches runCaches, ModTrustTable trust, CollectedObjects collected) =>
        new(
            collected.Targets,
            runCaches,
            trust,
            scope.Environment.Plugins,
            scope.Settings.OtherModsObjects,
            RestingObjects.CreateSupportContacts(runCaches),
            scope.Settings.Execution);

    private static IAlsoRemoveFollowUp CreateAlsoRemoveFollowUp(RunScope scope, IRunCaches runCaches, CollectedObjects collected, IRemovalDecisions decided) =>
        RestingObjects.CreateFollowUp(
            new(collected.Targets, decided, scope.Settings.AlsoRemove, scope.Settings.Execution, scope.Environment.TimeSinceStart, runCaches));

    /// <summary>The visible kinds the settings always keep, read also while the visible step is switched off.</summary>
    private static IKindsAlwaysKept CreateVisibleKindsAlwaysKept(RunScope scope, IRunCaches runCaches) =>
        VisibleLeftBehindObjects.CreateKindsAlwaysKept(new(runCaches, scope.Settings.NeverRemovedVisibleKinds));

    /// <summary>The invisible kinds the settings always keep, read also while the invisible step is switched off.</summary>
    private static IKindsAlwaysKept CreateInvisibleKindsAlwaysKept(RunScope scope, IRunCaches runCaches) =>
        InvisibleLeftBehindObjects.CreateKindsAlwaysKept(new(runCaches.Shapes, scope.Settings.NeverRemovedInvisibleKinds));

    private static IAlsoRemoveRoundsAfter CreateAlsoRemoveRoundsAfter(RunScope scope, IRunCaches runCaches) =>
        RestingObjects.CreateAlsoRemoveRoundsAfter(new(scope.Settings.AlsoRemove, scope.Settings.Execution, runCaches));

    private static ObjectMeasurementRules CreateMeasurementRules(RunScope scope) => RemovalCauseSelector.MeasurementRules(scope.UnexpectedErrors);

    /// <summary>The object caches of worlds other than the run's, each built on its first ask.</summary>
    private static ObjectCachesOfWorlds CreateWorldCaches(RunScope scope) => CreateWorldCaches(scope, CreateMeasurementRules(scope));

    private static ObjectCachesOfWorlds CreateWorldCaches(RunScope scope, ObjectMeasurementRules rules) => new(scope.LoadOrderCaches, rules, scope.Settings.Execution);

    /// <summary>What the too-close checks in other worlds read: the run's caches, the caches of those worlds and the causes selected there.</summary>
    private static TooCloseObjects.WorldRuleInput CreateWorldRuleInput(IRunCaches runCaches, IObjectCachesOfWorlds worldCaches) =>
        new(runCaches, worldCaches, RemovalCauseSelector.CreateCausesOfWorlds(worldCaches));

    /// <summary>The run's target objects measured among the causes of the old-place world; its caches are built when the first earlier version is checked.</summary>
    private static IOldPlaceTooCloseRule CreateOldPlaceTooCloseRule(RunScope scope, IRunCaches runCaches, CollectedObjects collected)
    {
        var worldCaches = CreateWorldCaches(scope);
        return TooCloseObjects.CreateOldPlaceTooCloseRule(new(collected.Targets, CreateWorldRuleInput(runCaches, worldCaches), scope.Settings.ChangedObjectCollision.TooClose));
    }

    private static IPlacementRule CreateOldPlaceChangedTerrainRule(RunScope scope, IRunCaches runCaches, ModTrustTable trust) =>
        ChangedTerrainObjects.CreatePlacementRule(
            new(trust.AcceptedWorldWithoutCleanedMod, scope.Environment.Plugins, scope.Plugin, runCaches.Shapes, scope.Settings.ChangedObjectCollision.ChangedTerrain));

    /// <remarks>The restore's rules share the world caches, which record measurement failures under the part the step names.</remarks>
    /// <param name="final">The decisions with every round and every move.</param>
    private static WhereRemovedRestorer.Input CreateRestoreInput(
        RunScope scope,
        IRunCaches runCaches,
        ModTrustTable trust,
        OtherModsObjects.SupportJudging support,
        CollectedObjects collected,
        ImmutableArray<CollisionGroup> groups,
        OtherModsObjectsResult otherModsObjects,
        IRemovalDecisions final)
    {
        var worldCaches = CreateWorldCaches(scope, RemovalCauseSelector.MeasurementRules(scope.UnexpectedErrors, WhereRemovedRestorer.MeasurementFailurePart()));
        return new WhereRemovedRestorer.Input(
            new(collected, otherModsObjects),
            final,
            new LazyClearedAreas(() => ObjectsInClearedAreas.CreateClearedAreas(new(collected.Targets, collected.Spaces, final, runCaches, scope.Settings.RestoreWhereRemoved.ClearedArea))),
            new(
                worldCaches,
                TooCloseObjects.CreateTooCloseToWhatStaysRule(CreateWorldRuleInput(runCaches, worldCaches)),
                OtherModsObjects.CreateHoldersOfObjectsThatStay(new(support, worldCaches)),
                PatchWriter.CreateNavmeshCheck(scope.Plugin, scope.Environment.PatchWriting)),
            new([.. groups.Select(group => group.ChangedTerrain)], [.. groups.Select(group => group.TooClose)], trust.AcceptedWorld),
            new(runCaches, scope.Plugin, scope.Environment.Plugins, trust.CleanedMod),
            scope.Settings.Execution,
            scope.Settings.RestoreWhereRemoved,
            scope.Environment.TimeSinceStart);
    }
}
