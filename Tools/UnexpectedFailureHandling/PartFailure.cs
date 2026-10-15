namespace AddedObjectRemover;

/// <summary>Names a part of the run that can fail unexpectedly; the module that records the failure owns the key, and the log words it.</summary>
/// <param name="Owner">The module that owns the key.</param>
/// <param name="Name">The part, unique within its owner.</param>
public readonly record struct PartKey(string Owner, string Name);

/// <summary>An error nobody planned for, caught where one part of the run is processed so the rest of the run carries on.</summary>
/// <param name="Failure">The exception's type and message; null when the part failed without an exception.</param>
/// <param name="Subject">The one thing it happened for; null when it stopped a whole part.</param>
public sealed record PartFailure(PartKey Part, string? Failure, FailureSubject? Subject);

/// <param name="Key">The thing's key text, such as a record key or a file path.</param>
/// <param name="Name">The thing's name, if it has one.</param>
/// <param name="Position">Where it sits in its owner's order, so the failures of many things can be listed in a fixed order.</param>
public sealed record FailureSubject(string Key, string? Name, int Position);
