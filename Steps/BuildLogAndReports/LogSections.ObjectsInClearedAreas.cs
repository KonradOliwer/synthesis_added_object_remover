using System.Collections.Immutable;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsInClearedAreas.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    /// <summary>The step's name in the log.</summary>
    public const string ObjectsInClearedAreasStepName = "Objects in cleared areas";

    private const string ObjectsInClearedAreasSectionId = "cleared-areas";

    /// <param name="kept">The objects the step's round held.</param>
    /// <param name="minObjectsAround">The fewest objects around the step removes an object with.</param>
    /// <remarks>
    /// The counts line comes first and only when the step removed or kept something; then the detailed log's time line and the kept
    /// lines. A stopped step has the not-performed line alone.
    /// </remarks>
    public static LogSection BuildObjectsInClearedAreasSection(
        CollectedObjects world, ObjectsInClearedAreasResult found, IEnumerable<KeptObject> kept, int minObjectsAround, ReportContext context)
    {
        if (found.Outcome == StepOutcome.StoppedByUnexpectedError) return new(ObjectsInClearedAreasSectionId, [BuildNotPerformedLine(ObjectsInClearedAreasStepName)]);

        return new(
            ObjectsInClearedAreasSectionId,
            [
                .. BuildClearedAreasCountLine(found.Evaluations, minObjectsAround),
                .. context.Detailed ? [ClearedAreasJudgedLine(found)] : ImmutableArray<string>.Empty,
                .. BuildKeptLines(world, kept),
            ]);
    }

    /// <summary>
    /// The counts line, "Objects in cleared areas: 412 removed (380 drawn, 32 invisible); kept: 5 of a kind always kept, 3 with fewer
    /// than 5 of the cleaned mod's other objects around, 7 where the area still stands.", or nothing when the step neither removed nor
    /// kept an object. Every object the step judges is removed or kept for one of these three reasons, so the counts add up to the
    /// objects judged.
    /// </summary>
    private static ImmutableArray<string> BuildClearedAreasCountLine(IReadOnlyList<ClearedAreaCheck> evaluations, int minObjectsAround)
    {
        var removed = evaluations.Where(evaluation => evaluation.IsRemoved).ToList();
        var invisible = removed.Count(evaluation => evaluation.Kind == ClearedAreaObjectKind.Invisible);
        var keptCounts = new (LeftBehindOutcome Decision, string Reason)[]
        {
            (LeftBehindOutcome.KeptProtectedType, "of a kind always kept"),
            (LeftBehindOutcome.KeptTooFewObjectsAround, $"with {LeftBehindOutcomeText.TooFewObjectsAround(minObjectsAround)}"),
            (LeftBehindOutcome.KeptAreaStillStands, "where the area still stands"),
        }
            .Select(kept => (Count: evaluations.Count(evaluation => evaluation.Decision == kept.Decision), kept.Reason))
            .Where(kept => kept.Count > 0)
            .ToList();
        if (removed.Count == 0 && keptCounts.Count == 0) return [];

        var removedPart = removed.Count == 0
            ? "0 removed"
            : $"{TextFormat.Count(removed.Count)} removed ({TextFormat.Count(removed.Count - invisible)} drawn, {TextFormat.Count(invisible)} invisible)";
        var keptPart = keptCounts.Count == 0
            ? string.Empty
            : $"; kept: {string.Join(Describe.ListSeparator, keptCounts.Select(kept => $"{TextFormat.Count(kept.Count)} {kept.Reason}"))}";
        return [$"{ObjectsInClearedAreasStepName}: {removedPart}{keptPart}."];
    }

    /// <summary>"Objects in cleared areas: judged 30,412 objects in 1.2s."</summary>
    private static string ClearedAreasJudgedLine(ObjectsInClearedAreasResult found) =>
        $"{ObjectsInClearedAreasStepName}: judged {TextFormat.Count(found.Evaluations.Count)} objects in {TextFormat.Seconds(found.JudgeTime)}.";
}
