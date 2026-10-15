using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>A point in a run's unexpected errors; errors recorded later are "since" it.</summary>
public readonly record struct ErrorMark(int Sequence);

/// <summary>
/// The unexpected errors a run met. Each entry is recorded once: the first record wins and later records of the same
/// entry are ignored. Thread-safe.
/// </summary>
public interface IUnexpectedErrors
{
    ErrorMark Mark();

    void Add(PartFailure failure);

    /// <summary>The failures first recorded after the mark, in no particular order; the caller words and orders them.</summary>
    ImmutableArray<PartFailure> Since(ErrorMark mark);
}
