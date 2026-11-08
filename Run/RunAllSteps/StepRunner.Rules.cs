using System.Collections.Immutable;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Run.RunAllSteps.RunCaches;
using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.FitClashingObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;
using AddedObjectRemover.Rules.ObjectsLeftBehind;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Rules.ObjectsThatCanCauseRemovals;
using AddedObjectRemover.Rules.ObjectsThatCanCauseRemovals.Contracts;
using AddedObjectRemover.Rules.RemovalRules.Contracts;
using AddedObjectRemover.Rules.TerrainChangedByOtherMods;
using AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;
using AddedObjectRemover.Rules.TooCloseToOtherModsObjects;
using AddedObjectRemover.Rules.TooCloseToOtherModsObjects.Contracts;
using AddedObjectRemover.Steps.JudgeChangedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;
using AddedObjectRemover.Steps.WriteThePatch;

namespace AddedObjectRemover.Run.RunAllSteps;

public static partial class StepRunner
{
    /// <summary>The run's target objects among the objects that can cause removals.</summary>
    private static TooCloseWorld CreateTooCloseWorld(IRunCaches runCaches, CollectedObjects collected, SelectedObjects selected) =>
        new(collected.Targets, runCaches.VisibleTargets, selected, runCaches.PlacedNpcs, runCaches.Shapes, runCaches.Triangles);

    /// <summary>The fittable and the unfittable placement rule of each collision group.</summary>
    private static ImmutableArray<FitGroup> CreateFitGroups(
        RunScope scope, IRunCaches runCaches, ModTrustTable trust, TooCloseWorld tooCloseWorld, ImmutableArray<CollisionGroup> groups) =>
    [
        .. groups.Select(group => new FitGroup(
            group.Fit,
            Fittable: TooCloseRules.CreatePlacementRule(tooCloseWorld, group.TooClose.Settings),
            Unfittable: ChangedTerrainRules.CreatePlacementRule(
                trust.AcceptedWorld, scope.Environment.Plugins, CreateTerrainView(scope), runCaches.Shapes, group.ChangedTerrain.Settings))),
    ];

    /// <summary>The changed-terrain checks over the run's target objects in the world the cleaned mod's objects are judged in.</summary>
    private static IChangedTerrainChecks CreateChangedTerrainChecks(RunScope scope, IRunCaches runCaches, ModTrustTable trust, CollectedObjects collected) =>
        ChangedTerrainRules.CreateChecks(collected.Targets, trust.AcceptedWorld, scope.Environment.Plugins, CreateTerrainView(scope), runCaches.Shapes);

    /// <summary>The terrain and water of the load order the changed-terrain rule judges, with the parts earlier runs gave back.</summary>
    private static ITerrainView CreateTerrainView(RunScope scope) => ChangedTerrainRules.ViewOfLoadOrder(scope.Plugin, scope.RestoredBefore);

    /// <summary>The objects others rest on: target objects and other mods' objects alike.</summary>
    private static IRestingRule CreateFitRestingRule(
        RunScope scope, IRunCaches runCaches, CollectedObjects collected, SupportJudging support, HeldUpTargets heldUp)
    {
        var drops = RestingObjectsRules.CreateDropTest(support, scope.Settings.AlsoRemove);
        var targetObjects = RestingObjectsRules.CreateTargetObjectsRestingRule(collected.Targets, scope.Settings.AlsoRemove, drops, scope.Settings.Execution, runCaches);
        return RestingObjectsRules.CreateAnyObjectRestingRule(support, targetObjects, heldUp);
    }

    /// <summary>What every part of the other-mods support rule and every rule it provides read.</summary>
    private static SupportJudging CreateSupportJudging(RunScope scope, IRunCaches runCaches, ModTrustTable trust, CollectedObjects collected) =>
        new(
            collected.Targets,
            runCaches,
            trust,
            scope.Environment.Plugins,
            scope.Settings.OtherModsObjects,
            RestingObjectsRules.CreateSupportContacts(runCaches),
            scope.Settings.Execution);

    private static IAlsoRemoveFollowUp CreateAlsoRemoveFollowUp(RunScope scope, IRunCaches runCaches, CollectedObjects collected, IRemovalDecisions decided) =>
        RestingObjectsRules.CreateFollowUp(
            collected.Targets, decided, scope.Settings.AlsoRemove, scope.Settings.Execution, scope.Environment.TimeSinceStart, runCaches);

    /// <summary>The also-remove rounds seeded by earlier rounds, with the report-file details when report files are written.</summary>
    private static IRestingObjectsRounds CreateRestingObjectsRounds(RunScope scope, IRunCaches runCaches) =>
        RestingObjectsRules.CreateRounds(scope.Settings.AlsoRemove, scope.Settings.Execution, scope.Environment.TimeSinceStart, scope.ReportFolder.Outcome, runCaches);

    /// <summary>What becomes of the other mods' objects left without support, with the also-remove rounds after its own.</summary>
    private static ILeftWithoutSupportRounds CreateLeftWithoutSupportRounds(RunScope scope, IRunCaches runCaches, SupportJudging support) =>
        RestingObjectsRules.CreateLeftWithoutSupportRounds(support, scope.Settings.AlsoRemove, CreateAlsoRemoveRoundsAfter(scope, runCaches));

    /// <summary>The kinds the settings always keep, read also while the left-behind steps are switched off.</summary>
    private static AllKindsAlwaysKept CreateKindsAlwaysKept(RunScope scope, IRunCaches runCaches) =>
        LeftBehindRules.CreateKindsAlwaysKept(runCaches, scope.Settings.NeverRemovedVisibleKinds, scope.Settings.NeverRemovedInvisibleKinds);

    private static IAlsoRemoveRoundsAfter CreateAlsoRemoveRoundsAfter(RunScope scope, IRunCaches runCaches) =>
        RestingObjectsRules.CreateAlsoRemoveRoundsAfter(scope.Settings.AlsoRemove, scope.Settings.Execution, runCaches);

    private static ObjectMeasurementRules CreateMeasurementRules(RunScope scope) => RemovalCauseSelector.MeasurementRules(scope.UnexpectedErrors);

    /// <summary>The object caches of worlds other than the run's, each built on its first ask.</summary>
    private static ObjectCachesOfWorlds CreateWorldCaches(RunScope scope) => CreateWorldCaches(scope, CreateMeasurementRules(scope));

    private static ObjectCachesOfWorlds CreateWorldCaches(RunScope scope, ObjectMeasurementRules rules) => new(scope.LoadOrderCaches, rules, scope.Settings.Execution);

    /// <summary>The run's target objects measured among the causes of the old-place world; its caches are built when the first earlier version is checked.</summary>
    private static IOldPlaceTooCloseRule CreateOldPlaceTooCloseRule(RunScope scope, IRunCaches runCaches, CollectedObjects collected)
    {
        var worldCaches = CreateWorldCaches(scope);
        return TooCloseRules.CreateOldPlaceTooCloseRule(
            collected.Targets, runCaches, worldCaches, RemovalCauses.CreateCausesOfWorlds(worldCaches), scope.Settings.ChangedObjectCollision.TooClose);
    }

    private static IPlacementRule CreateOldPlaceChangedTerrainRule(RunScope scope, IRunCaches runCaches, ModTrustTable trust) =>
        ChangedTerrainRules.CreateOldPlacePlacementRule(
            scope.Plugin, trust.AcceptedWorldWithoutCleanedMod, scope.Environment.Plugins, runCaches.Shapes, scope.Settings.ChangedObjectCollision.ChangedTerrain);

    /// <summary>The neighbours of each left-behind step, measured on <paramref name="final"/>; a step that is switched off measures none.</summary>
    private static ImmutableArray<ILeftBehindNeighbours> CreateLeftBehindNeighbours(RunScope scope, IRunCaches runCaches, CollectedObjects collected, IRemovalDecisions final) =>
        LeftBehindRules.CreateNeighbours(collected.Targets, final, runCaches, scope.Settings.VisibleLeftBehind, scope.Settings.InvisibleLeftBehind);

    /// <summary>The too-close and changed-terrain settings the removal steps judge the cleaned mod's own objects with.</summary>
    private static CleanedModsObjectRules CreateCleanedModsObjectRules(RunScope scope) => new(scope.Settings.Collision.TooClose, scope.Settings.Collision.ChangedTerrain);

    /// <summary>The changed-terrain rule in the world the cleaned mod's objects are judged in, over any view of the terrain.</summary>
    private static IChangedTerrainJudging CreateChangedTerrainJudging(RunScope scope, IRunCaches runCaches, ModTrustTable trust) =>
        ChangedTerrainRules.CreateJudging(trust.AcceptedWorld, scope.Environment.Plugins, CreateTerrainView(scope), runCaches.Shapes);

    /// <param name="final">The decisions with every round and every move.</param>
    private static WhereRemovedRestorer.Input CreateRestoreInput(
        RunScope scope,
        IRunCaches runCaches,
        ModTrustTable trust,
        SupportJudging support,
        CollectedObjects collected,
        ImmutableArray<CollisionGroup> groups,
        OtherModsObjectsResult otherModsObjects,
        IRemovalDecisions final) =>
        new(
            new(collected, otherModsObjects),
            final,
            LeftBehindRules.CreateClearedAreasOfTheRestore(collected, runCaches, final, scope.Settings.RestoreWhereRemoved.ClearedArea),
            CreateRestoreGuardRules(scope, runCaches, support),
            CreateRemovalStepJudging(scope, runCaches, trust, collected, groups, final),
            new(runCaches, scope.Plugin, scope.Environment.Plugins, trust.CleanedMod),
            scope.Settings.Execution,
            scope.Settings.RestoreWhereRemoved,
            scope.Environment.TimeSinceStart);

    /// <remarks>The too-close rule's world caches record measurement failures under the part the restore names.</remarks>
    private static WhereRemovedRestorer.GuardRules CreateRestoreGuardRules(RunScope scope, IRunCaches runCaches, SupportJudging support)
    {
        var worldCaches = CreateWorldCaches(scope, RemovalCauseSelector.MeasurementRules(scope.UnexpectedErrors, WhereRemovedRestorer.MeasurementFailurePart()));
        return new(
            TooCloseRules.CreateTooCloseToWhatStaysRule(runCaches, worldCaches, RemovalCauses.CreateCausesOfWorlds(worldCaches)),
            RestingObjectsRules.CreateFollowUpOfObjectsLeaving(support, scope.Settings.AlsoRemove, CreateAlsoRemoveRoundsAfter(scope, runCaches)),
            support.Settings.TouchGap,
            PatchWriter.CreateNavmeshCheck(scope.Plugin, scope.Environment.PatchWriting));
    }

    /// <summary>What the removal steps judge each target with, as the restore judges the cleaned mod's objects that stand.</summary>
    /// <param name="final">The decisions with every round and every move.</param>
    private static WhereRemovedRestorer.RemovalStepJudging CreateRemovalStepJudging(
        RunScope scope, IRunCaches runCaches, ModTrustTable trust, CollectedObjects collected, ImmutableArray<CollisionGroup> groups, IRemovalDecisions final) =>
        new(
            ObjectsJudgedWith<ChangedTerrainSettings>.SettingsByObject(groups.Select(group => group.ChangedTerrain)),
            ObjectsJudgedWith<TooCloseSettings>.SettingsByObject(groups.Select(group => group.TooClose)),
            trust.AcceptedWorld,
            CreateLeftBehindNeighbours(scope, runCaches, collected, final),
            CreateCleanedModsObjectRules(scope),
            CreateChangedTerrainJudging(scope, runCaches, trust));
}
