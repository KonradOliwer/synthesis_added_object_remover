using System.Diagnostics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    /// <summary>The step's name, as the settings screen names its group.</summary>
    private const string LeftBehindStepName = "Invisible objects left behind";

    public const string MarkerMovesName = "Moved kept markers";

    /// <param name="kept">The objects the left-behind round held.</param>
    /// <remarks>The summary comes first, then the per-object decisions and the kept lines.</remarks>
    public static LogSection BuildLeftBehindSection(
        CollectedObjects world,
        LeftBehindResult leftBehind,
        IEnumerable<KeptObject> kept,
        TimeSpan elapsed,
        ReportContext context)
    {
        if (leftBehind.Outcome == StepOutcome.SwitchedOff) return new LogSection("leftovers", []);

        List<string> lines =
        [
            BuildPerformedOrNotPerformedLine(
                leftBehind.Outcome, LeftBehindStepName, () => LeftBehindSummaryLine(LeftBehindStepName, leftBehind.Evaluations, judgesInsideOtherObjects: true, elapsed, context)),
        ];
        if (context.Detailed) lines.AddRange(leftBehind.Evaluations.Select(evaluation => LeftBehindOutcomeLine(world, evaluation)));
        lines.AddRange(BuildKeptLines(world, kept));
        return new LogSection("leftovers", [.. lines]);
    }

    public static LogSection BuildKeptMarkerMovesSection(
        CollectedObjects world,
        MarkerMoves markerMoves,
        ReportContext context)
    {
        if (markerMoves.Outcome == StepOutcome.SwitchedOff) return new LogSection("relocations", []);

        List<string> lines = [];
        foreach (var reported in ReportedMove.Of(markerMoves))
        {
            var target = world.Targets[reported.Move.Target.Index];
            if (context.Detailed)
            {
                lines.Add(
                    $"  Moved kept {RecordNames.Describe(target)} in {Describe.Space(world, target.SpaceKey)} "
                    + $"out of {Describe.OtherObject(reported.Detail.Evaluation.ContainingObject!.Value)}: {TextFormat.Fixed(reported.Distance, 0)} units onto the {reported.Detail.Surface.ToString().ToLowerInvariant()}.");
            }
            if (reported.Detail.LeftHomeCell)
            {
                lines.Add(
                    $"  Warning: {RecordNames.Describe(target)} was moved into the neighbouring cell "
                    + $"({ExteriorGrid.CellIndex(reported.Move.To.X)}, {ExteriorGrid.CellIndex(reported.Move.To.Y)}) because its own cell has no free spot.");
            }
        }
        foreach (var evaluation in markerMoves.LeftInPlace)
        {
            var target = world.Targets[evaluation.TargetIndex];
            lines.Add(
                $"  Left kept {RecordNames.Describe(target)} in {Describe.Space(world, target.SpaceKey)} "
                + $"inside {Describe.OtherObject(evaluation.ContainingObject!.Value)}: no free navmesh or terrain spot within {TextFormat.Fixed(markerMoves.MaxDistance, 0)} units.");
        }
        foreach (var evaluation in markerMoves.LeftInDeletedCell)
        {
            var target = world.Targets[evaluation.TargetIndex];
            lines.Add(
                $"  Left kept {RecordNames.Describe(target)} in {Describe.Space(world, target.SpaceKey)} "
                + $"inside {Describe.OtherObject(evaluation.ContainingObject!.Value)}: {Describe.DeletedCell(target.CellDeleted!)}.");
        }
        lines.Add(MarkerMovesClosingLine(markerMoves));
        return new LogSection("relocations", [.. lines]);
    }

    private static string MarkerMovesClosingLine(MarkerMoves markerMoves) => markerMoves.Outcome switch
    {
        StepOutcome.Performed => MarkerMovesSummaryLine(markerMoves),
        StepOutcome.StoppedByUnexpectedError => BuildNotPerformedLine(MarkerMovesName),
        StepOutcome.StoppedByEarlierFailure => BuildNotPerformedBecauseEarlierLine(MarkerMovesName, LeftBehindStepName),
        _ => throw new UnreachableException($"Unexpected marker move outcome {markerMoves.Outcome}."),
    };

    private static string MarkerMovesSummaryLine(MarkerMoves markerMoves) =>
        $"Moved {TextFormat.Count(markerMoves.Moves.Length)} kept markers out of other mods' objects; {TextFormat.Count(markerMoves.LeftInPlace.Count + markerMoves.LeftInDeletedCell.Count)} left in place.";

    private static string LeftBehindOutcomeLine(CollectedObjects world, LeftBehindCheck evaluation)
    {
        var target = world.Targets[evaluation.TargetIndex];
        return $"  Left behind: {RecordNames.Describe(target)} ({ObjectVisibilityText.KindName(evaluation.Kind)}) in {Describe.Space(world, target.SpaceKey)}: "
            + $"{LeftBehindOutcomeText.Verdict(evaluation)}, {Describe.LeftBehindReason(evaluation)}; radius {TextFormat.Fixed(evaluation.Radius, 0)}, "
            + $"removed/total ground area {SectorAreasText.Describe(evaluation.Surroundings)}.";
    }

    /// <summary>The summary line of either left-behind step.</summary>
    /// <param name="judgesInsideOtherObjects">
    /// Whether the step removes objects inside another mod's object: only then is that outcome listed when no object has it. The visible
    /// step never gives it.
    /// </param>
    private static string LeftBehindSummaryLine(
        string stepName, IEnumerable<LeftBehindVerdict> evaluations, bool judgesInsideOtherObjects, TimeSpan elapsed, ReportContext context) =>
        BuildOutcomeSummaryLine(
            stepName,
            [.. evaluations.Select(evaluation => evaluation.Decision)],
            LeftBehindOutcomes.IsRemoval,
            LeftBehindOutcomes.IsRestoration,
            outcome => IsRemovedAsLinked(outcome) || LeftBehindOutcomes.IsRestoration(outcome)
                || outcome is LeftBehindOutcome.RemovedSurroundingsRemovedAndRestored or LeftBehindOutcome.KeptInDeletedCell
                || (outcome is LeftBehindOutcome.RemovedInsideOtherObject && !judgesInsideOtherObjects),
            LeftBehindOutcomeText.Describe,
            elapsed,
            context);

    /// <summary>The outcomes of an object disabled as a linked group member.</summary>
    private static bool IsRemovedAsLinked(LeftBehindOutcome outcome) =>
        outcome is LeftBehindOutcome.RemovedLinkedToRemoval or LeftBehindOutcome.RemovedLinkedToRestoration;
}
