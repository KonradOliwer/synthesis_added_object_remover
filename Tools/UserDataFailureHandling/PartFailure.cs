namespace AddedObjectRemover;

/// <summary>Names a part of the run that can fail on the user's data; the module that records the failure owns the key, and the log words it.</summary>
/// <param name="Owner">The module that owns the key.</param>
/// <param name="Name">The part, unique within its owner.</param>
public readonly record struct PartKey(string Owner, string Name);

/// <summary>A failure of the user's data, caught where one part of the run is processed so the rest of the run carries on.</summary>
/// <param name="Failure">The exception's type and message; null when the part failed without an exception.</param>
/// <param name="Subject">The one thing it happened for; null when it stopped a whole part.</param>
public sealed record PartFailure(PartKey Part, string? Failure, FailureSubject? Subject)
{
    /// <summary>
    /// The caught exception's full text with its stack trace; only for a failure that stopped a whole part. The failures of
    /// single things carry none: they are many, and one exception shared through a lazily built value gets its stack trace
    /// from whichever threads rethrew it.
    /// </summary>
    public string? Details { get; init; }

    public static PartFailure OfWholePart(PartKey part, CaughtFailure failure) =>
        new(part, failure.Description, Subject: null) { Details = failure.Details };

    /// <param name="failures">The failures a result carries; null where there was none.</param>
    /// <returns>The failures that are set, in order.</returns>
    public static IEnumerable<PartFailure> Present(params PartFailure?[] failures) => failures.OfType<PartFailure>();
}

/// <param name="Key">The thing's key text, such as a record key or a file path.</param>
/// <param name="Name">The thing's name, if it has one.</param>
/// <param name="Position">Where it sits in its owner's order, so the failures of many things can be listed in a fixed order.</param>
public sealed record FailureSubject(string Key, string? Name, int Position);
