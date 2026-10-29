namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>An unexpected error as the log words it.</summary>
/// <param name="Part">What failed, as a phrase ("the also-remove step" for a whole part; "checking objects for being too close" when objects are handled one by one).</param>
/// <param name="Failure">The exception's type and message.</param>
/// <param name="Consequence">A full sentence saying what is missing or incomplete in the results because of it.</param>
/// <param name="Subject">The one object or mesh it happened for; null when it stopped a whole part.</param>
internal sealed record UnexpectedError(string Part, string Failure, string Consequence, ErrorSubject? Subject)
{
    /// <summary>The exception's full text with its stack trace, which only the detailed log prints; null when the part failed without an exception.</summary>
    public string? Details { get; init; }

    /// <summary>The log line of an error that stopped a whole part, as printed; errors for single objects are printed grouped.</summary>
    public string Message => $"  Unexpected error {(Subject is null ? "in" : "while")} {Part} ({Failure}). {Consequence}";
}

/// <summary>What kind of thing an error happened for, named singular and plural for the log.</summary>
internal sealed record SubjectKind(string Singular, string Plural)
{
    public static readonly SubjectKind Object = new("object", "objects");
    public static readonly SubjectKind Mesh = new("mesh", "meshes");
    public static readonly SubjectKind OldPlaceObject = new("old-place object", "old-place objects");
}

/// <param name="Label">How the log names it.</param>
/// <param name="Position">Where it sits in the record key order, so a group lists its examples in a fixed order; subjects with the same position are ordered by label.</param>
internal sealed record ErrorSubject(SubjectKind Kind, string Label, int Position);
