using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes;

/// <summary>
/// The also-remove rounds seeded by the given rounds, decided on a working copy of the earlier decisions, which never change.
/// </summary>
internal static class RestingObjectsFinder
{
    /// <summary>
    /// The rounds, then the touch chains when they are shown, then the report-file details when report files are
    /// written, each stopped on its own by a user-data failure.
    /// </summary>
    /// <param name="caches">
    /// Its base facts, shapes, triangles, surface samples and terrain are read; its objects of any plugin are asked only when the
    /// support rounds have seeds: the first ask builds the supporters' bounds index.
    /// </param>
    public static RestingObjectsResult Find(RestingObjectsInput input, IRunCaches caches)
    {
        if (input.Settings.Mode == FollowUpRemovalMode.Nothing) return NotPerformed(input, StepOutcome.SwitchedOff, failure: null).Result;

        var rounds = Failures.Attempt(
            () => DecideRounds(input, caches),
            failure => NotPerformed(input, StepOutcome.StoppedByUserDataFailure, PartFailure.OfWholePart(RestingObjectsParts.AlsoRemove, failure)));
        var run = rounds with { Result = WithTouchChains(rounds, input) };
        return run.Result with { ReportFileDetails = FindReportFileDetails(run, input, caches.Bases, caches.Shapes, caches.Triangles) };
    }

    private static RestingObjectsRun DecideRounds(RestingObjectsInput input, IRunCaches caches)
    {
        var (earlierRounds, seedRounds, settings) = (input.EarlierRounds, input.SeedRounds, input.Settings);
        var (run, roundsTime) = RestingRounds.TimeRounds(
            RestingRounds.NeedsObjectsOfAnyPlugin(earlierRounds, seedRounds, settings),
            caches.VisibleObjectsOfAnyPlugin,
            input.TimeSinceStart,
            objectsOfAnyPlugin => RestingRounds.DecideOnWorkingCopy(
                RestingRounds.Prepare(earlierRounds, seedRounds, RoundsInput.From(input.Targets, objectsOfAnyPlugin, caches, input.Execution), settings),
                earlierRounds,
                seedRounds));
        return run with { Result = run.Result with { RoundsTime = roundsTime } };
    }

    private static RestingObjectsResult WithTouchChains(RestingObjectsRun run, RestingObjectsInput input) =>
        RestingRounds.WithTouchChains(run.Result, input.Settings, () => TouchChains.Find(run, input.Targets.Length, input.Execution));

    /// <remarks>A step whose rounds or touch chains failed computes no details; their status says so.</remarks>
    private static ReportFileDetails FindReportFileDetails(
        RestingObjectsRun run,
        RestingObjectsInput input,
        IBaseFacts bases,
        IBaseObjectShapes shapes,
        ITriangleMeshes triangles)
    {
        if (input.ReportFiles != StepOutcome.Performed) return ReportFileDetails.SwitchedOff;
        if (run.Result.Outcome == StepOutcome.StoppedByUserDataFailure) return ReportFileDetails.StoppedByEarlierFailure;
        if (run.Result.TouchChainsFailure is not null) return ReportFileDetails.StoppedByEarlierFailure;

        var detailsInput = new ReportFileDetailsInput(run, run.Result.TouchChains, input.Targets, shapes, triangles, bases, input.Execution);
        return Failures.Attempt(
            () => ExplanationFinder.Compute(detailsInput),
            failure => new ReportFileDetails(
                StepOutcome.StoppedByUserDataFailure, Touch: null, MeshOrigins: null, PartFailure.OfWholePart(RestingObjectsParts.ReportFileDetails, failure)));
    }

    /// <summary>The step without rounds: switched off, or stopped by <paramref name="failure"/>; it proposes nothing.</summary>
    private static RestingObjectsRun NotPerformed(RestingObjectsInput input, StepOutcome outcome, PartFailure? failure) =>
        new(RestingRounds.WithoutRounds(outcome, input.Settings.Mode, hadSeeds: false, failure), input.EarlierRounds, Search: null);
}
