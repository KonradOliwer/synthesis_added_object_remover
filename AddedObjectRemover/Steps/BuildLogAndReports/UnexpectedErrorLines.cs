using System.Collections.Immutable;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>
/// The log lines of the unexpected errors. Errors for single objects or meshes with the same part and the same failure
/// are one group, so a faulty mesh gives one line, not one per object. Groups come ordered by part, then failure,
/// and a group's subjects by their position in the record key order, so the text does not depend on timing.
/// </summary>
internal static class UnexpectedErrorLines
{
    /// <summary>How many subjects the group line names; the detailed log lists all of them.</summary>
    public const int ExampleCount = 3;

    private const string ListIndent = "    ";

    public static ImmutableArray<string> Of(IEnumerable<UnexpectedError> errors, ReportContext context) =>
        [.. InGroupOrder(errors).GroupBy(error => (error.Part, error.Failure, error.Subject?.Kind)).SelectMany(group => LinesOf([.. group], context))];

    private static IEnumerable<UnexpectedError> InGroupOrder(IEnumerable<UnexpectedError> errors) =>
        errors
            .OrderBy(error => error.Part, StringComparer.Ordinal)
            .ThenBy(error => error.Failure, StringComparer.Ordinal)
            .ThenBy(error => error.Subject?.Kind.Singular, StringComparer.Ordinal)
            .ThenBy(error => error.Subject?.Position)
            .ThenBy(error => error.Subject?.Label, StringComparer.Ordinal);

    private static IEnumerable<string> LinesOf(IReadOnlyList<UnexpectedError> group, ReportContext context)
    {
        var first = group[0];
        if (first.Subject is null) return [first.Message];

        var line = GroupLine(first, group);
        return context.Detailed && group.Count > ExampleCount
            ? [line, .. group.Select(error => ListIndent + error.Subject!.Label)]
            : [line];
    }

    private static string GroupLine(UnexpectedError first, IReadOnlyList<UnexpectedError> group)
    {
        var kind = first.Subject!.Kind;
        var examples = string.Join(", ", group.Take(ExampleCount).Select(error => error.Subject!.Label));
        var more = group.Count > ExampleCount ? $" (+{TextFormat.Count(group.Count - ExampleCount)} more)" : string.Empty;
        var unit = group.Count == 1 ? kind.Singular : kind.Plural;
        return $"  Unexpected error while {first.Part} ({first.Failure}): {TextFormat.Count(group.Count)} {unit}, e.g. {examples}{more}. {first.Consequence}";
    }
}
