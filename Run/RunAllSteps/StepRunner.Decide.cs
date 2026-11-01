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
    /// Selects the objects that can cause removals and finds the objects to keep, other mods' objects rest on included, then
    /// decides the fits of clashing objects and the removal rounds in order (changed terrain, too close, also remove, visible objects left behind
    /// with the also-remove follow-up of each of its rounds, other mods' objects left without support with the target objects that go
    /// with them, leftover invisible objects), then the moves of the kept markers, and last what the cleaned mod changed that goes
    /// back where it was removed.
    /// </summary>
    /// <remarks>
    /// The runner holds no error boundary: each step catches only user-data failures inside the step, and any other error ends
    /// the run. A step adds its rounds to the run's decisions outside that boundary, so any error there ends the run. Each deciding
    /// step's call is one timed section, so the printed times include adding the rounds.
    /// </remarks>
    private static StepResults Decide(RunScope scope, IRunCaches runCaches, ModTrustTable trust, CollectedObjects collected)
    {
        var selected = RunSelection(scope, runCaches, collected);
        var kept = RunKeepCheck(scope, runCaches, trust, collected);
        var judged = RunChangedObjectJudging(scope, runCaches, trust, collected);
        var groups = scope.Settings.CollisionGroupsOf(collected.Targets);
        var fitted = RunFit(scope, runCaches, trust, collected, selected, groups, kept, judged);
        var changedTerrain = RunChangedTerrain(scope, runCaches, trust, collected, groups, fitted);
        var tooClose = RunTooClose(scope, runCaches, collected, selected, groups, changedTerrain);
        var alsoRemove = RunAlsoRemove(scope, runCaches, collected, tooClose);
        var visibleLeftBehind = RunVisibleLeftBehind(scope, runCaches, collected, alsoRemove);
        var otherMods = RunOtherModsObjects(scope, runCaches, trust, collected, fitted.Decisions, visibleLeftBehind);
        var otherModsObjectsTakenAway = otherMods.Result.TakenAway();
        var visibleTargetsAsTheRoundsMeasureThem = runCaches.IndexVisibleTargetsAsTheRoundsMeasureThem(otherMods.Decisions);
        var leftBehind = RunLeftBehind(
            scope, runCaches, visibleTargetsAsTheRoundsMeasureThem, collected, selected.ObjectsThatCanCauseRemovals, otherModsObjectsTakenAway, otherMods);
        var markerMoves = RunMarkerMoves(scope, runCaches, collected, otherModsObjectsTakenAway, leftBehind);
        var whereRemoved = RunRestoreWhereRemoved(scope, runCaches, trust, visibleTargetsAsTheRoundsMeasureThem, collected, otherMods.Result, markerMoves.Decisions);
        return new StepResults(
            Collected: collected,
            ObjectsToKeep: kept.Kept.ObjectsToKeep,
            RemovalRisks: kept.Kept.Risks,
            ChangedObjects: judged.Result,
            OldPlaceWorld: judged.OldPlaceWorld,
            ChangedTerrain: changedTerrain.Result,
            RestingObjects: alsoRemove.Result,
            VisibleLeftBehind: visibleLeftBehind.Result,
            LeftBehind: leftBehind.Result,
            MarkerMoves: markerMoves.Result,
            OtherModsObjects: otherMods.Result,
            Final: markerMoves.Decisions,
            RestoreWhereRemoved: whereRemoved);
    }

    private static SelectedObjects RunSelection(RunScope scope, IRunCaches runCaches, CollectedObjects collected) =>
        scope.Environment.Log.Timed(
            () => RemovalCauseSelector.Select(new(collected, runCaches.OtherModObjects, runCaches.Shapes, scope.Settings.Selection, scope.Settings.Execution)),
            (selected, elapsed) => scope.Report.BuildStepSections(selected.Replacements, collected, elapsed));

    /// <remarks>
    /// The step times its two parts through the run's clock, since each prints its own sections: what other mods' objects rest on,
    /// then the objects to keep.
    /// </remarks>
    private static ObjectsToKeepFound RunKeepCheck(RunScope scope, IRunCaches runCaches, ModTrustTable trust, CollectedObjects collected)
    {
        var found = ObjectsToKeepFinder.Find(
            new(collected.Targets, collected.Links, runCaches.Bases, CreateHeldUpTargetsSearch(scope, runCaches, trust, collected), scope.Environment.TimeSinceStart));
        Print(scope.Environment.Log, scope.Report.BuildStepSections(found.HeldUp, found.HeldUpTime));
        Print(scope.Environment.Log, scope.Report.BuildStepSections(found.Kept, found.KeepTime));
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
        SelectedObjects selected,
        ImmutableArray<CollisionGroup> groups,
        ObjectsToKeepFound kept,
        ChangedObjectsJudged judged) =>
        scope.Environment.Log.Timed(
            () => ClashingObjectFitter.Fit(
                new(
                    collected.Targets,
                    kept.Kept.ObjectsToKeep,
                    CreateFitGroups(scope, runCaches, trust, collected, selected, groups),
                    CreateFitRestingRule(scope, runCaches, trust, collected, kept.HeldUp),
                    runCaches.Shapes,
                    collected.Order,
                    scope.Settings.Execution,
                    RemovalDecisions.Start(kept.Kept.ObjectsToKeep, judged.Result.WhenRemoved))),
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
        IRunCaches runCaches,
        CollectedObjects collected,
        SelectedObjects selected,
        ImmutableArray<CollisionGroup> groups,
        Decided<ChangedTerrainResult> changedTerrain) =>
        scope.Environment.Log.Timed(
            () => TooCloseObjects.Find(
                new(
                    collected,
                    runCaches.VisibleTargets,
                    selected,
                    runCaches.PlacedNpcs,
                    runCaches.Shapes,
                    runCaches.Triangles,
                    scope.Settings.Execution,
                    groups.Select(group => group.TooClose),
                    changedTerrain.Decisions)),
            (phase, elapsed) => scope.Report.BuildStepSections(phase.Result, phase.Decisions, collected, runCaches.VisibleTargets, elapsed));

    /// <remarks>The step also times its rounds alone, through the run's clock.</remarks>
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
                    CreateAlsoRemoveFollowUp(scope, runCaches, collected, alsoRemove.Decisions),
                    scope.Environment.TimeSinceStart,
                    collected.Order,
                    scope.Settings.Execution,
                    scope.Settings.VisibleLeftBehind)),
            (phase, elapsed) => scope.Report.BuildStepSections(phase.Result, phase.Decisions, collected, elapsed));

    /// <param name="afterFits">The decisions with every fit: the rounds measure an object shrunk or turned and removed later at its new placement.</param>
    private static Decided<OtherModsObjectsResult> RunOtherModsObjects(
        RunScope scope,
        IRunCaches runCaches,
        ModTrustTable trust,
        CollectedObjects collected,
        IRemovalDecisions afterFits,
        Decided<VisibleLeftBehindResult> visibleLeftBehind) =>
        scope.Environment.Log.Timed(
            () => OtherModsObjects.Settle(
                new(
                    CreateSupportJudging(scope, runCaches, trust, collected),
                    afterFits,
                    visibleLeftBehind.Decisions,
                    scope.Settings.AlsoRemove,
                    CreateAlsoRemoveRoundsAfter(scope, runCaches))),
            (phase, elapsed) => scope.Report.BuildStepSections(phase.Result, phase.Decisions, collected, elapsed));

    /// <param name="visibleTargets">The visible targets where the rounds measure them.</param>
    /// <param name="otherModsObjectsTakenAway">The other mods' objects pulled down (where they stand now) or removed (null), by id.</param>
    private static Decided<LeftBehindResult> RunLeftBehind(
        RunScope scope,
        IRunCaches runCaches,
        IVisibleTargetObjects visibleTargets,
        CollectedObjects collected,
        IObjectsThatCanCauseRemovals objectsThatCanCauseRemovals,
        IReadOnlyDictionary<OtherId, OtherObject?> otherModsObjectsTakenAway,
        Decided<OtherModsObjectsResult> otherMods) =>
        scope.Environment.Log.Timed(
            () => LeftBehindObjects.Find(
                new(
                    collected.Targets,
                    objectsThatCanCauseRemovals,
                    otherModsObjectsTakenAway,
                    otherMods.Decisions,
                    visibleTargets,
                    runCaches,
                    collected.Order,
                    scope.Settings.Execution,
                    scope.Settings.LeftBehind)),
            (phase, elapsed) => scope.Report.BuildStepSections(phase.Result, phase.Decisions, collected, elapsed));

    /// <param name="otherModsObjectsTakenAway">The other mods' objects pulled down (where they stand now) or removed (null), by id.</param>
    private static Decided<MarkerMoves> RunMarkerMoves(
        RunScope scope,
        IRunCaches runCaches,
        CollectedObjects collected,
        IReadOnlyDictionary<OtherId, OtherObject?> otherModsObjectsTakenAway,
        Decided<LeftBehindResult> leftBehind) =>
        scope.Environment.Log.Timed(
            () => MarkerMovePlanner.Plan(
                new(
                    collected.Targets,
                    leftBehind.Result,
                    leftBehind.Decisions,
                    otherModsObjectsTakenAway,
                    runCaches,
                    scope.Plugin,
                    scope.Settings.Execution,
                    scope.Settings.MarkerMoves)),
            (markerMoves, elapsed) => scope.Report.BuildStepSections(markerMoves.Result, collected, elapsed));

    /// <param name="visibleTargets">The visible targets where the rounds measure them.</param>
    /// <param name="final">The decisions with every round and every move.</param>
    private static RestoreWhereRemovedResult RunRestoreWhereRemoved(
        RunScope scope,
        IRunCaches runCaches,
        ModTrustTable trust,
        IVisibleTargetObjects visibleTargets,
        CollectedObjects collected,
        OtherModsObjectsResult otherModsObjects,
        IRemovalDecisions final) =>
        scope.Environment.Log.Timed(
            () => WhereRemovedRestorer.Plan(CreateRestoreInput(scope, runCaches, trust, visibleTargets, collected, otherModsObjects, final)),
            (result, elapsed) => scope.Report.BuildStepSections(result, collected, elapsed));
}
