using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    /// <param name="kept">The objects the left-behind round held.</param>
    /// <remarks>The kept lines come first, then the problems the phase met, then the per-object decisions and the summary.</remarks>
    public static LogSection LeftBehind(
        CollectedObjects world,
        LeftBehindResult leftBehind,
        IEnumerable<KeptObject> kept,
        PhaseProblems problems,
        TimeSpan elapsed,
        ReportContext context)
    {
        var lines = KeptThenProblems(Kept(world, kept, context), problems, context);
        if (context.Detailed) lines.AddRange(leftBehind.Evaluations.Select(evaluation => LeftBehindOutcomeLine(world, evaluation)));
        lines.Add(LeftBehindSummaryLine(leftBehind, elapsed, context));
        return new LogSection("leftovers", [.. lines]);
    }

    public static LogSection KeptMarkerMoves(
        CollectedObjects world,
        MarkerMoves markerMoves,
        MarkerMoveSettings options,
        PhaseProblems problems,
        ReportContext context)
    {
        List<string> lines = [.. Problems(problems, context)];
        foreach (var move in markerMoves.Moved)
        {
            var target = world.Targets[move.Evaluation.TargetIndex];
            if (context.Detailed)
            {
                lines.Add(
                    $"  Moved kept {RecordNames.Describe(target)} in {Describe.Space(world, target.SpaceKey)} "
                    + $"out of {Describe.OtherObject(move.Evaluation.ContainingObject!.Value)}: {TextFormat.Fixed(move.Distance, 0)} units onto the {move.Surface.ToString().ToLowerInvariant()}.");
            }
            if (move.LeftHomeCell)
            {
                lines.Add(
                    $"  Warning: {RecordNames.Describe(target)} was moved into the neighboring cell "
                    + $"({ExteriorGrid.CellIndex(move.To.X)}, {ExteriorGrid.CellIndex(move.To.Y)}) because its own cell has no free spot.");
            }
        }
        foreach (var evaluation in markerMoves.LeftInPlace)
        {
            var target = world.Targets[evaluation.TargetIndex];
            lines.Add(
                $"  Left kept {RecordNames.Describe(target)} in {Describe.Space(world, target.SpaceKey)} "
                + $"inside {Describe.OtherObject(evaluation.ContainingObject!.Value)}: no free navmesh or terrain spot within {TextFormat.Fixed(options.MaxDistance, 0)} units.");
        }
        lines.Add($"Moved {TextFormat.Count(markerMoves.Moved.Count)} kept markers out of other mods' objects; {TextFormat.Count(markerMoves.LeftInPlace.Count)} left in place.");
        return new LogSection("relocations", [.. lines]);
    }

    private static string LeftBehindOutcomeLine(CollectedObjects world, LeftBehindCheck evaluation)
    {
        var target = world.Targets[evaluation.TargetIndex];
        return $"  Leftover {RecordNames.Describe(target)} ({evaluation.Kind}) in {Describe.Space(world, target.SpaceKey)}: "
            + $"{(evaluation.IsRemoved ? "removed" : "kept")}, {Describe.LeftBehindReason(evaluation)}; radius {TextFormat.Fixed(evaluation.Radius, 0)}, "
            + $"removed/total ground area {SectorAreasText.Describe(evaluation.Surroundings)}.";
    }

    private static string LeftBehindSummaryLine(LeftBehindResult leftBehind, TimeSpan elapsed, ReportContext context)
    {
        var outcomes = Enum.GetValues<LeftBehindOutcome>();
        return $"Leftover invisible objects: {TextFormat.Count(leftBehind.Evaluations.Count)} evaluated{Describe.TimedSuffix(elapsed, context)}; "
            + $"removed {TextFormat.Count(leftBehind.RemovedCount)} ({CountOutcomes(leftBehind, outcomes.Where(LeftBehindOutcomes.IsRemoval))}); "
            + $"kept {TextFormat.Count(leftBehind.Evaluations.Count - leftBehind.RemovedCount)} "
            + $"({CountOutcomes(leftBehind, outcomes.Where(outcome => !LeftBehindOutcomes.IsRemoval(outcome)))}).";
    }

    private static string CountOutcomes(LeftBehindResult leftBehind, IEnumerable<LeftBehindOutcome> outcomes) =>
        string.Join(
            Describe.ListSeparator,
            TextLists.Counts(outcomes.Select(outcome => KeyValuePair.Create(LeftBehindOutcomeText.Describe(outcome), leftBehind.CountOutcomes(outcome)))));
}
