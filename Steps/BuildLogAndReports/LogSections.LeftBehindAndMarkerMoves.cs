using System.Diagnostics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    /// <summary>The step's name, as the settings screen names its group.</summary>
    private const string LeftBehindStepName = "Invisible objects left behind";

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

        List<string> lines = [LeftBehindStepLine(leftBehind, elapsed, context)];
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
        lines.Add(MarkerMovesClosingLine(markerMoves));
        return new LogSection("relocations", [.. lines]);
    }

    private static string MarkerMovesClosingLine(MarkerMoves markerMoves) => markerMoves.Outcome switch
    {
        StepOutcome.Performed => MarkerMovesSummaryLine(markerMoves),
        StepOutcome.StoppedByUnexpectedError => BuildNotPerformedLine("Moved kept markers"),
        StepOutcome.StoppedByEarlierFailure => BuildNotPerformedBecauseEarlierLine("Moved kept markers", LeftBehindStepName),
        _ => throw new UnreachableException($"Unexpected marker move outcome {markerMoves.Outcome}."),
    };

    private static string LeftBehindStepLine(LeftBehindResult leftBehind, TimeSpan elapsed, ReportContext context) => leftBehind.Outcome switch
    {
        StepOutcome.Performed => LeftBehindSummaryLine(leftBehind, elapsed, context),
        StepOutcome.StoppedByUnexpectedError => BuildNotPerformedLine(LeftBehindStepName),
        _ => throw new UnreachableException($"Unexpected left-behind outcome {leftBehind.Outcome}."),
    };

    private static string MarkerMovesSummaryLine(MarkerMoves markerMoves) =>
        $"Moved {TextFormat.Count(markerMoves.Moves.Length)} kept markers out of other mods' objects; {TextFormat.Count(markerMoves.LeftInPlace.Count)} left in place.";

    private static string LeftBehindOutcomeLine(CollectedObjects world, LeftBehindCheck evaluation)
    {
        var target = world.Targets[evaluation.TargetIndex];
        return $"  Left behind: {RecordNames.Describe(target)} ({ObjectVisibilityText.KindName(evaluation.Kind)}) in {Describe.Space(world, target.SpaceKey)}: "
            + $"{(evaluation.IsRemoved ? "removed" : "kept")}, {Describe.LeftBehindReason(evaluation)}; radius {TextFormat.Fixed(evaluation.Radius, 0)}, "
            + $"removed/total ground area {SectorAreasText.Describe(evaluation.Surroundings)}.";
    }

    private static string LeftBehindSummaryLine(LeftBehindResult leftBehind, TimeSpan elapsed, ReportContext context)
    {
        var outcomes = Enum.GetValues<LeftBehindOutcome>();
        var removedCount = leftBehind.Evaluations.Count(evaluation => evaluation.IsRemoved);
        return $"{LeftBehindStepName}: {TextFormat.Count(leftBehind.Evaluations.Count)} evaluated{Describe.TimedSuffix(elapsed, context)}; "
            + $"removed {TextFormat.Count(removedCount)} ({CountOutcomes(leftBehind, outcomes.Where(LeftBehindOutcomes.IsRemoval))}); "
            + $"kept {TextFormat.Count(leftBehind.Evaluations.Count - removedCount)} "
            + $"({CountOutcomes(leftBehind, outcomes.Where(outcome => !LeftBehindOutcomes.IsRemoval(outcome)))}).";
    }

    private static string CountOutcomes(LeftBehindResult leftBehind, IEnumerable<LeftBehindOutcome> outcomes) =>
        string.Join(
            Describe.ListSeparator,
            TextLists.Counts(outcomes.Select(outcome => KeyValuePair.Create(LeftBehindOutcomeText.Describe(outcome), leftBehind.Evaluations.Count(evaluation => evaluation.Decision == outcome)))));
}
