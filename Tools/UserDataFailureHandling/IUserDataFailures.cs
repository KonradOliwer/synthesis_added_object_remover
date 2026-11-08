using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>A point in a run's user-data failures; failures recorded later are "since" it.</summary>
public readonly record struct ErrorMark(int Sequence);

/// <summary>
/// The user-data failures a run met. Each entry is recorded once: the first record wins and later records of the same
/// entry are ignored. Thread-safe.
/// </summary>
public interface IUserDataFailures
{
    ErrorMark Mark();

    void Add(PartFailure failure);

    /// <summary>The failures first recorded after the mark, in no particular order; the caller words and orders them.</summary>
    ImmutableArray<PartFailure> Since(ErrorMark mark);
}
