using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    /// <summary>The step's name, as the settings screen names its group.</summary>
    public const string VisibleLeftBehindStepName = "Visible objects left behind";

    /// <param name="kept">The objects the visible left-behind rounds held.</param>
    /// <param name="elapsed">The whole step's time as the run measured it: the rounds, their also-remove follow-ups and the measuring before the first round.</param>
    /// <remarks>
    /// The summary comes first, then the detailed log's rounds line, its lines for the objects that have one (see <see cref="HasDetailedLine"/>)
    /// and the kept lines.
    /// </remarks>
    public static LogSection BuildVisibleLeftBehindSection(
        CollectedObjects world,
        VisibleLeftBehindResult visibleLeftBehind,
        IEnumerable<KeptObject> kept,
        TimeSpan elapsed,
        ReportContext context)
    {
        var evaluationLines = context.Detailed
            ? visibleLeftBehind.Evaluations.Where(HasDetailedLine).Select(evaluation => VisibleLeftBehindOutcomeLine(world, evaluation))
            : [];
        return new LogSection(
            "visible-leftovers",
            [
                BuildPerformedOrNotPerformedLine(visibleLeftBehind.Outcome, VisibleLeftBehindStepName, () => VisibleLeftBehindSummaryLine(visibleLeftBehind, elapsed, context)),
                .. VisibleRoundsLines(visibleLeftBehind, context),
                .. evaluationLines,
                .. BuildKeptLines(world, kept),
            ]);
    }

    /// <summary>The detailed log's line for the rounds that judged at least one object, with the time of those rounds alone; none when no round ran.</summary>
    private static ImmutableArray<string> VisibleRoundsLines(VisibleLeftBehindResult visibleLeftBehind, ReportContext context) =>
        context.Detailed && !visibleLeftBehind.Rounds.IsEmpty
            ? [$"{VisibleLeftBehindStepName}: {TextFormat.Count(visibleLeftBehind.Rounds.Length)} rounds in {TextFormat.Seconds(visibleLeftBehind.RoundsTime)}."]
            : [];

    /// <summary>
    /// Removed objects, objects kept because something refers to them or because their kind is ticked, and objects whose
    /// surroundings were mostly kept although at least one direction was cleared have a line. The others are only in the report file.
    /// </summary>
    private static bool HasDetailedLine(VisibleLeftBehindCheck evaluation) => evaluation.Decision switch
    {
        VisibleLeftBehindOutcome.KeptSurroundingsMostlyKept => evaluation.Surroundings.ClearedCount > 0,
        VisibleLeftBehindOutcome.KeptTooFewSurroundingObjects => false,
        _ => true,
    };

    private static string VisibleLeftBehindOutcomeLine(CollectedObjects world, VisibleLeftBehindCheck evaluation)
    {
        var target = world.Targets[evaluation.TargetIndex];
        return $"  Visible left behind: {RecordNames.Describe(target)}{(evaluation.Kind is { } kind ? $" ({VisibleLeftBehindOutcomeText.KindName(kind)})" : string.Empty)} in {Describe.Space(world, target.SpaceKey)}: "
            + $"{VisibleLeftBehindOutcomeText.Verdict(evaluation)}, {VisibleLeftBehindOutcomeText.DescribeReason(evaluation)}; "
            + $"removed/total ground area {SectorAreasText.Describe(evaluation.Surroundings)}.";
    }

    private static string VisibleLeftBehindSummaryLine(VisibleLeftBehindResult visibleLeftBehind, TimeSpan elapsed, ReportContext context) =>
        BuildOutcomeSummaryLine(
            VisibleLeftBehindStepName,
            [.. visibleLeftBehind.Evaluations.Select(evaluation => evaluation.Decision)],
            VisibleLeftBehindOutcomes.IsRemoval,
            VisibleLeftBehindOutcomes.IsRestoration,
            outcome => VisibleLeftBehindOutcomes.IsRemovedAsLinked(outcome) || VisibleLeftBehindOutcomes.IsRestoration(outcome)
                || outcome == VisibleLeftBehindOutcome.RemovedSurroundingsRemovedAndRestored,
            VisibleLeftBehindOutcomeText.Describe,
            elapsed,
            context);
}
