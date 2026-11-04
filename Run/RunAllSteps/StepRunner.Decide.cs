using System.Collections.Immutable;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.FitClashingObjects;
using AddedObjectRemover.Steps.FitClashingObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;
using AddedObjectRemover.Steps.JudgeChangedObjects;
using AddedObjectRemover.Steps.JudgeChangedObjects.Contracts;
using AddedObjectRemover.Steps.MoveKeptMarkers;
using AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;
using AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport;
using AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsInClearedAreas;
using AddedObjectRemover.Steps.RemoveObjectsInClearedAreas.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Run.RunAllSteps;

public static partial class StepRunner
{
    /// <summary>
    /// Runs the deciding steps in order: select the objects that can cause removals, find the objects to keep, fit clashing
    /// objects, the removal rounds (changed terrain, too close, also remove, visible objects left behind, objects in cleared areas,
    /// other mods' objects left without support, invisible objects left behind), move the kept markers, and last restore what the
    /// cleaned mod changed where it was removed.
    /// </summary>
    /// <remarks>
    /// The runner holds no error boundary: each step catches only user-data failures inside the step, and any other error ends
    /// the run. A step adds its rounds to the run's decisions outside that boundary, so any error there ends the run. Each deciding
    /// step's call is one timed section, so the printed times include adding the rounds.
    /// </remarks>
    private static StepResults Decide(RunScope scope, IRunCaches runCaches, ModTrustTable trust, CollectedObjects collected)
    {
        var support = CreateSupportJudging(scope, runCaches, trust, collected);
        var selected = RunSelection(scope, runCaches, collected);
        var tooCloseWorld = CreateTooCloseWorld(runCaches, collected, selected);
        var kept = RunKeepCheck(scope, runCaches, collected, support);
        var judged = RunChangedObjectJudging(scope, runCaches, trust, collected);
        var groups = scope.Settings.CollisionGroupsOf(collected.Targets);
        var fitted = RunFit(scope, runCaches, trust, collected, tooCloseWorld, support, groups, kept, judged);
        var changedTerrain = RunChangedTerrain(scope, runCaches, trust, collected, groups, fitted);
        var tooClose = RunTooClose(scope, collected, tooCloseWorld, groups, changedTerrain);
        var alsoRemove = RunAlsoRemove(scope, runCaches, collected, tooClose);
        var visibleLeftBehind = RunVisibleLeftBehind(scope, runCaches, collected, alsoRemove);
        var clearedAreas = RunObjectsInClearedAreas(scope, runCaches, collected, visibleLeftBehind);
        var otherMods = RunOtherModsObjects(scope, runCaches, collected, support, fitted.Decisions, clearedAreas);
        var leftBehind = RunInvisibleLeftBehind(scope, runCaches, collected, selected.ObjectsThatCanCauseRemovals, otherMods);
        var markerMoves = RunMarkerMoves(scope, runCaches, collected, otherMods.Result, leftBehind);
        var whereRemoved = RunRestoreWhereRemoved(scope, runCaches, trust, support, collected, groups, otherMods.Result, markerMoves.Decisions);
        return new StepResults(
            Collected: collected,
            ObjectsToKeep: kept.Kept.ObjectsToKeep,
            RemovalRisks: kept.Kept.Risks,
            ChangedObjects: judged.Result,
            OldPlaceWorld: judged.OldPlaceWorld,
            ChangedTerrain: changedTerrain.Result,
            RestingObjects: alsoRemove.Result,
            VisibleLeftBehind: visibleLeftBehind.Result,
            ObjectsInClearedAreas: clearedAreas.Result,
            InvisibleLeftBehind: leftBehind.Result,
            MarkerMoves: markerMoves.Result,
            OtherModsObjects: otherMods.Result,
            Final: markerMoves.Decisions,
            RestoreWhereRemoved: whereRemoved);
    }

    private static SelectedObjects RunSelection(RunScope scope, IRunCaches runCaches, CollectedObjects collected) =>
        scope.Environment.Log.Timed(
            () => RemovalCauseSelector.Select(new(collected, runCaches.OtherModObjects, runCaches.Shapes, scope.Settings.Selection, scope.Settings.Execution)),
            (selected, elapsed) => scope.Report.BuildStepSections(selected.Replacements, collected, elapsed));

    /// <remarks>The step times its two parts through the run's clock, since they print one after the other.</remarks>
    private static ObjectsToKeepFound RunKeepCheck(RunScope scope, IRunCaches runCaches, CollectedObjects collected, OtherModsObjects.SupportJudging support)
    {
        var found = ObjectsToKeepFinder.Find(
            new(collected.Targets, collected.Links, runCaches.Bases, OtherModsObjects.CreateHeldUpTargetsSearch(support), scope.Environment.TimeSinceStart));
        Print(scope.Environment.Log, scope.Report.BuildStepSections(found));
        return found;
    }

    private static ChangedObjectsJudged RunChangedObjectJudging(RunScope scope, IRunCaches runCaches, ModTrustTable trust, CollectedObjects collected) =>
        scope.Environment.Log.Timed(
            () => ChangedObjectJudging.Judge(
                new(
                    collected,
                    trust.CleanedMod,
                    scope.Settings.Read,
                    CreateOldPlaceTooCloseRule(scope, runCaches, collected),
                    CreateOldPlaceChangedTerrainRule(scope, runCaches, trust),
                    scope.Settings.ChangedObjects,
                    scope.Settings.Execution)),
            (judged, elapsed) => scope.Report.BuildStepSections(judged.Result, collected, judged.OldPlaceWorld, elapsed));

    private static Decided<FitResult> RunFit(
        RunScope scope,
        IRunCaches runCaches,
        ModTrustTable trust,
        CollectedObjects collected,
        TooCloseObjects.TooCloseWorld tooCloseWorld,
        OtherModsObjects.SupportJudging support,
        ImmutableArray<CollisionGroup> groups,
        ObjectsToKeepFound kept,
        ChangedObjectsJudged judged) =>
        scope.Environment.Log.Timed(
            () =>
            {
                var started = RemovalDecisions.Start(kept.Kept.ObjectsToKeep, judged.Result.WhenRemoved);
                return ClashingObjectFitter.Fit(
                    new(
                        collected.Targets,
                        kept.Kept.ObjectsToKeep,
                        CreateFitGroups(scope, runCaches, trust, tooCloseWorld, groups),
                        CreateFitRestingRule(scope, runCaches, collected, support, kept.HeldUp),
                        runCaches.Shapes,
                        collected.Order,
                        scope.Settings.Execution,
                        started));
            },
            (fitted, elapsed) => scope.Report.BuildStepSections(fitted.Result, elapsed));

    private static Decided<ChangedTerrainResult> RunChangedTerrain(
        RunScope scope,
        IRunCaches runCaches,
        ModTrustTable trust,
        CollectedObjects collected,
        ImmutableArray<CollisionGroup> groups,
        Decided<FitResult> fitted) =>
        scope.Environment.Log.Timed(
            () => ChangedTerrainObjects.Find(
                new(
                    collected,
                    fitted.Decisions,
                    trust.AcceptedWorld,
                    scope.Environment.Plugins,
                    scope.Plugin,
                    runCaches.Shapes,
                    scope.Settings.Execution,
                    groups.Select(group => group.ChangedTerrain))),
            (phase, elapsed) => scope.Report.BuildStepSections(phase.Result, phase.Decisions, collected, elapsed));

    private static Decided<TooCloseResult> RunTooClose(
        RunScope scope,
        CollectedObjects collected,
        TooCloseObjects.TooCloseWorld tooCloseWorld,
        ImmutableArray<CollisionGroup> groups,
        Decided<ChangedTerrainResult> changedTerrain) =>
        scope.Environment.Log.Timed(
            () => TooCloseObjects.Find(
                new(tooCloseWorld, collected, collected.Order, scope.Settings.Execution, groups.Select(group => group.TooClose), changedTerrain.Decisions)),
            (phase, elapsed) => scope.Report.BuildStepSections(phase.Result, phase.Decisions, collected, elapsed));

    private static Decided<RestingObjectsResult> RunAlsoRemove(RunScope scope, IRunCaches runCaches, CollectedObjects collected, Decided<TooCloseResult> tooClose) =>
        scope.Environment.Log.Timed(
            () => RestingObjects.Find(
                new(
                    collected.Targets,
                    tooClose.Decisions,
                    scope.Settings.AlsoRemove,
                    scope.Settings.Execution,
                    scope.Environment.TimeSinceStart,
                    scope.ReportFolder.Outcome,
                    runCaches)),
            (phase, elapsed) => scope.Report.BuildStepSections(phase.Result, phase.Decisions, collected, elapsed));

    private static Decided<VisibleLeftBehindResult> RunVisibleLeftBehind(
        RunScope scope, IRunCaches runCaches, CollectedObjects collected, Decided<RestingObjectsResult> alsoRemove) =>
        scope.Environment.Log.Timed(
            () => VisibleLeftBehindObjects.Find(
                new(
                    collected.Targets,
                    alsoRemove.Decisions,
                    runCaches,
                    CreateVisibleKindsAlwaysKept(scope, runCaches),
                    CreateAlsoRemoveFollowUp(scope, runCaches, collected, alsoRemove.Decisions),
                    scope.Environment.TimeSinceStart,
                    collected.Order,
                    scope.Settings.Execution,
                    scope.Settings.VisibleLeftBehind)),
            (phase, elapsed) => scope.Report.BuildStepSections(phase.Result, phase.Decisions, collected, elapsed));

    private static Decided<ObjectsInClearedAreasResult> RunObjectsInClearedAreas(
        RunScope scope, IRunCaches runCaches, CollectedObjects collected, Decided<VisibleLeftBehindResult> visibleLeftBehind) =>
        scope.Environment.Log.Timed(
            () => ObjectsInClearedAreas.Find(
                new(
                    collected.Targets,
                    collected.Spaces,
                    visibleLeftBehind.Decisions,
                    runCaches,
                    [CreateVisibleKindsAlwaysKept(scope, runCaches), CreateInvisibleKindsAlwaysKept(scope, runCaches)],
                    VisibleLeftBehindObjects.CreateLeftBehindRoundJoin(),
                    CreateAlsoRemoveFollowUp(scope, runCaches, collected, visibleLeftBehind.Decisions),
                    scope.Environment.TimeSinceStart,
                    collected.Order,
                    scope.Settings.Execution,
                    scope.Settings.ObjectsInClearedAreas)),
            // The section prints the judging time only, so the whole step's time is not passed.
            (phase, _) => scope.Report.BuildStepSections(phase.Result, phase.Decisions, collected));

    private static Decided<OtherModsObjectsResult> RunOtherModsObjects(
        RunScope scope,
        IRunCaches runCaches,
        CollectedObjects collected,
        OtherModsObjects.SupportJudging support,
        IRemovalDecisions afterFits,
        Decided<ObjectsInClearedAreasResult> clearedAreas) =>
        scope.Environment.Log.Timed(
            () => OtherModsObjects.Settle(
                new(
                    support,
                    afterFits,
                    clearedAreas.Decisions,
                    scope.Settings.AlsoRemove,
                    CreateAlsoRemoveRoundsAfter(scope, runCaches))),
            (phase, elapsed) => scope.Report.BuildStepSections(phase.Result, phase.Decisions, collected, elapsed));

    private static Decided<InvisibleLeftBehindResult> RunInvisibleLeftBehind(
        RunScope scope,
        IRunCaches runCaches,
        CollectedObjects collected,
        IObjectsThatCanCauseRemovals objectsThatCanCauseRemovals,
        Decided<OtherModsObjectsResult> otherMods) =>
        scope.Environment.Log.Timed(
            () => InvisibleLeftBehindObjects.Find(
                new(
                    collected.Targets,
                    objectsThatCanCauseRemovals,
                    otherMods.Result,
                    otherMods.Decisions,
                    runCaches,
                    CreateInvisibleKindsAlwaysKept(scope, runCaches),
                    VisibleLeftBehindObjects.CreateSurroundingsJudging(),
                    VisibleLeftBehindObjects.CreateLeftBehindRoundJoin(),
                    collected.Order,
                    scope.Settings.Execution,
                    scope.Settings.InvisibleLeftBehind)),
            (phase, elapsed) => scope.Report.BuildStepSections(phase.Result, phase.Decisions, collected, elapsed));

    private static Decided<MarkerMoves> RunMarkerMoves(
        RunScope scope,
        IRunCaches runCaches,
        CollectedObjects collected,
        OtherModsObjectsResult otherModsObjects,
        Decided<InvisibleLeftBehindResult> leftBehind) =>
        scope.Environment.Log.Timed(
            () => MarkerMovePlanner.Plan(
                new(
                    collected.Targets,
                    leftBehind.Result,
                    leftBehind.Decisions,
                    otherModsObjects,
                    runCaches,
                    scope.Plugin,
                    scope.Settings.Execution,
                    scope.Settings.MarkerMoves)),
            (markerMoves, elapsed) => scope.Report.BuildStepSections(markerMoves.Result, collected, elapsed));

    /// <param name="final">The decisions with every round and every move.</param>
    private static RestoreWhereRemovedResult RunRestoreWhereRemoved(
        RunScope scope,
        IRunCaches runCaches,
        ModTrustTable trust,
        OtherModsObjects.SupportJudging support,
        CollectedObjects collected,
        ImmutableArray<CollisionGroup> groups,
        OtherModsObjectsResult otherModsObjects,
        IRemovalDecisions final) =>
        scope.Environment.Log.Timed(
            () => WhereRemovedRestorer.Plan(CreateRestoreInput(scope, runCaches, trust, support, collected, groups, otherModsObjects, final)),
            (result, elapsed) => scope.Report.BuildStepSections(result, collected, elapsed));
}
