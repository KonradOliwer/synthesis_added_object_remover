using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>The log lines of the parts several phases print.</summary>
internal static partial class LogSections
{
    private const double BytesPerMebibyte = 1024.0 * 1024.0;

    public static ImmutableArray<string> Kept(World world, IEnumerable<KeptTarget> kept, ReportContext context) =>
        [.. kept.Select(entry => KeptLine(world, entry))];

    /// <remarks>Only the detailed log shows it: the removal summary repeats the count.</remarks>
    public static ImmutableArray<string> LinkedRemovals(int count, string step, ReportContext context) =>
        context.Detailed
            ? [$"Linked groups: {count:N0} more objects removed with the {step} removals they are linked to."]
            : [];

    /// <summary>How many objects a step's rounds removed as linked to their own removals.</summary>
    public static LogSection Linked(Ledger ledger, IEnumerable<Round> rounds, string step, ReportContext context) =>
        new("linked", LinkedRemovals(rounds.Sum(round => Decisions.CountLinkedIn(ledger, round)), step, context));

    /// <summary>The problems met while computing the explanations, which run after the patch is written.</summary>
    public static LogSection ExplanationProblems(PhaseProblems problems, ReportContext context) =>
        new("explanationProblems", Problems(problems, context));

    /// <remarks>Archive problems can make results wrong, so they always show; per-mesh problems only in the detailed log.</remarks>
    public static ImmutableArray<string> Problems(PhaseProblems problems, ReportContext context) =>
    [
        .. problems.Archive.Select(problem => problem.Message),
        .. context.Detailed ? problems.Assets.Select(problem => problem.Message) : [],
    ];

    private static string KeptLine(World world, KeptTarget entry)
    {
        var target = world.Targets[entry.TargetIndex];
        var touched = entry.TouchedTargetIndex is { } touchedIndex
            ? $" (touches removed {RecordNames.Describe(world.Targets[touchedIndex])})"
            : string.Empty;
        return $"  Kept {RecordNames.Describe(target)} in {world.SpaceNames[target.SpaceKey]}: {entry.Reason.Detail}{touched}.";
    }
}
