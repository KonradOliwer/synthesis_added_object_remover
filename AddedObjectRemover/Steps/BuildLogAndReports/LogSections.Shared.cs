using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>The log lines of the parts several phases print.</summary>
internal static partial class LogSections
{
    public static ImmutableArray<string> Kept(CollectedObjects world, IEnumerable<KeptObject> kept, ReportContext context) =>
        [.. kept.Select(entry => KeptLine(world, entry))];

    /// <remarks>Only the detailed log shows it: the removal summary repeats the count.</remarks>
    public static ImmutableArray<string> LinkedRemovals(int count, string step, ReportContext context) =>
        context.Detailed
            ? [$"Linked groups: {TextFormat.Count(count)} more objects removed with the {step} removals they are linked to."]
            : [];

    /// <summary>How many objects a step's rounds removed as linked to their own removals.</summary>
    public static LogSection Linked(IRemovalDecisions decisions, IEnumerable<Round> rounds, string step, ReportContext context) =>
        new("linked", LinkedRemovals(rounds.Sum(round => RemovalList.CountLinkedIn(decisions, round)), step, context));

    /// <summary>The problems met while computing the explanations, which run after the patch is written.</summary>
    public static LogSection ExplanationProblems(PhaseProblems problems, ReportContext context) =>
        new("explanationProblems", Problems(problems, context));

    /// <summary>Archive, data-folder and game INI problems can make results wrong, so they always show.</summary>
    public static LogSection ArchiveWarnings(IEnumerable<ArchiveProblem> problems) =>
        new("archiveWarnings", [.. problems.Select(problem => problem.Message)]);

    /// <summary>Errors nobody planned for can leave results incomplete, so they always show; the full lists of affected objects only show in the detailed log.</summary>
    public static LogSection UnexpectedErrors(IEnumerable<UnexpectedError> errors, ReportContext context) =>
        new("unexpectedErrors", UnexpectedErrorLines.Of(errors, context));

    /// <remarks>Per-mesh problems only show in the detailed log.</remarks>
    public static ImmutableArray<string> Problems(PhaseProblems problems, ReportContext context) =>
        context.Detailed ? [.. problems.Assets.Select(problem => problem.Message)] : [];

    /// <returns>A list the section adds its own lines to.</returns>
    private static List<string> KeptThenProblems(ImmutableArray<string> keptLines, PhaseProblems problems, ReportContext context) =>
        [.. keptLines, .. Problems(problems, context)];

    private static string KeptLine(CollectedObjects world, KeptObject entry)
    {
        var target = world.Targets[entry.TargetIndex];
        var touched = entry.TouchedTargetIndex is { } touchedIndex
            ? $" (touches removed {RecordNames.Describe(world.Targets[touchedIndex])})"
            : string.Empty;
        return $"  Kept {RecordNames.Describe(target)} in {Describe.Space(world, target.SpaceKey)}: {KeepReasonText.Detail(entry.Reason)}{touched}.";
    }
}
