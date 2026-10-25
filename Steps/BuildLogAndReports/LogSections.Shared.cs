using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>The log lines of the parts several phases print.</summary>
internal static partial class LogSections
{
    public static ImmutableArray<string> BuildKeptLines(CollectedObjects world, IEnumerable<KeptObject> kept) =>
        [.. kept.Select(entry => KeptLine(world, entry))];

    /// <remarks>Only the detailed log shows it: the removal summary repeats the count.</remarks>
    public static ImmutableArray<string> BuildLinkedRemovalLines(int count, string step, ReportContext context) =>
        context.Detailed
            ? [$"Linked groups: {TextFormat.Count(count)} more objects removed with the {step} removals they are linked to."]
            : [];

    /// <summary>How many objects a step's rounds removed as linked to their own removals.</summary>
    public static LogSection BuildLinkedSection(IRemovalDecisions decisions, IEnumerable<Round> rounds, RoundKind kind, ReportContext context) =>
        new("linked", BuildLinkedRemovalLines(rounds.Sum(round => RemovalList.CountLinkedIn(decisions, round)), RunPartText.LinkedStep(kind), context));

    /// <summary>The also-remove section when no round ran.</summary>
    public static LogSection BuildNoFollowUpSection() => BuildFollowUpSection([]);

    private static LogSection BuildFollowUpSection(ImmutableArray<string> lines) => new("follow-up", lines);

    /// <summary>The one line that replaces the summary of a step or the report files that an unexpected error stopped.</summary>
    public static string BuildNotPerformedLine(string sectionName) => $"{sectionName}: not performed because of an unexpected error.";

    /// <summary>The one line that replaces the summary of a step that did not run because an earlier section's step was not performed.</summary>
    public static string BuildNotPerformedBecauseEarlierLine(string sectionName, string earlierSectionName) =>
        $"{sectionName}: not performed because {earlierSectionName} was not performed.";

    /// <summary>Archive, data-folder and game INI problems can make results wrong, so they always show.</summary>
    public static LogSection BuildArchiveWarningsSection(IEnumerable<ArchiveProblem> problems) =>
        new("archiveWarnings", [.. problems.Select(problem => MeshProblemText.Line(problem))]);

    /// <summary>Errors nobody planned for can leave results incomplete, so they always show; the full lists of affected objects only show in the detailed log.</summary>
    public static LogSection BuildUnexpectedErrorsSection(IEnumerable<UnexpectedError> errors, ReportContext context) =>
        new("unexpectedErrors", UnexpectedErrorLines.Of(errors, context));

    /// <summary>Every broken mesh of the run, once, whichever step read it first: the detailed log lists the per-mesh problems, both logs list the grouped unexpected errors.</summary>
    /// <param name="problems">All mesh problems of the run; they are listed by mesh (ignoring case), then by the worded line.</param>
    /// <param name="errors">The unexpected errors whose subject is a mesh.</param>
    public static LogSection BuildMeshProblemsSection(ImmutableArray<AssetProblem> problems, IEnumerable<UnexpectedError> errors, ReportContext context) =>
        new("meshProblems",
        [
            .. context.Detailed ? MeshProblemLinesInOrder(problems) : [],
            .. UnexpectedErrorLines.Of(errors, context),
        ]);

    private static IEnumerable<string> MeshProblemLinesInOrder(ImmutableArray<AssetProblem> problems) =>
        problems
            .Select(problem => (problem.Mesh, Line: MeshProblemText.Line(problem)))
            .OrderBy(entry => entry.Mesh, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Line, StringComparer.Ordinal)
            .Select(entry => entry.Line);

    /// <summary>The not-performed line when the report-file details were stopped, by their own unexpected error or by the also-remove step's.</summary>
    public static LogSection BuildExplanationProblemsSection(StepOutcome detailsOutcome) =>
        new("explanationProblems", DetailsNotPerformedLines(detailsOutcome));

    /// <remarks>An earlier failure is the also-remove step's: its rounds or its touch-chain search were not performed.</remarks>
    private static ImmutableArray<string> DetailsNotPerformedLines(StepOutcome detailsOutcome) => detailsOutcome switch
    {
        StepOutcome.StoppedByUnexpectedError => [BuildNotPerformedLine("Report-file details")],
        StepOutcome.StoppedByEarlierFailure => [BuildNotPerformedBecauseEarlierLine("Report-file details", AlsoRemoveName)],
        StepOutcome.Performed or StepOutcome.SwitchedOff => [],
        _ => throw new UnreachableException($"Unknown step outcome {detailsOutcome}."),
    };

    private static string KeptLine(CollectedObjects world, KeptObject entry)
    {
        var target = world.Targets[entry.TargetIndex];
        var touched = entry.TouchedTargetIndex is { } touchedIndex
            ? $" (touches removed {RecordNames.Describe(world.Targets[touchedIndex])})"
            : string.Empty;
        return $"  Kept {RecordNames.Describe(target)} in {Describe.Space(world, target.SpaceKey)}: {KeepReasonText.Detail(entry.Reason)}{touched}.";
    }
}
