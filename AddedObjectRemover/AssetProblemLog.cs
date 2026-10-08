using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>The problem channel of one run's asset reading. Thread-safe.</summary>
internal sealed class AssetProblemLog : IMeshProblems
{
    private readonly record struct Recorded<T>(int Sequence, T Problem);

    private readonly ConcurrentDictionary<(string Mesh, AssetProblemKind Kind), Recorded<AssetProblem>> _meshProblems =
        new(new MeshKindComparer());
    private readonly ConcurrentDictionary<(ArchiveProblemKind Kind, string Subject), Recorded<ArchiveProblem>> _archiveProblems = new();
    private readonly ConcurrentDictionary<UnexpectedError, Recorded<UnexpectedError>> _unexpectedErrors = new();
    private readonly AtomicCounter _sequence = new();

    public void Add(AssetProblem problem) =>
        _meshProblems.TryAdd((problem.Mesh, problem.Kind), new Recorded<AssetProblem>(_sequence.Increment(), problem));

    public void Add(ArchiveProblem problem) =>
        _archiveProblems.TryAdd((problem.Kind, problem.Subject), new Recorded<ArchiveProblem>(_sequence.Increment(), problem));

    public void Add(UnexpectedError error) =>
        _unexpectedErrors.TryAdd(error, new Recorded<UnexpectedError>(_sequence.Increment(), error));

    public ProblemMark Mark() => new(_sequence.Value);

    public ImmutableArray<UnexpectedError> UnexpectedSince(ProblemMark mark) =>
    [
        .. _unexpectedErrors.Values
            .Where(recorded => recorded.Sequence > mark.Sequence)
            .Select(recorded => recorded.Problem)
            .OrderBy(error => error.Message, StringComparer.Ordinal)
            .ThenBy(error => error.Subject?.Position)
            .ThenBy(error => error.Subject?.Label, StringComparer.Ordinal),
    ];

    public ImmutableArray<AssetProblem> Since(ProblemMark mark) =>
    [
        .. _meshProblems.Values
            .Where(recorded => recorded.Sequence > mark.Sequence)
            .Select(recorded => recorded.Problem)
            .OrderBy(problem => problem.Mesh, StringComparer.OrdinalIgnoreCase)
            .ThenBy(problem => problem.Message, StringComparer.Ordinal),
    ];

    public ImmutableArray<ArchiveProblem> ArchiveProblemsSince(ProblemMark mark) =>
    [
        .. _archiveProblems.Values
            .Where(recorded => recorded.Sequence > mark.Sequence)
            .OrderBy(recorded => recorded.Sequence)
            .Select(recorded => recorded.Problem),
    ];

    /// <summary>Mesh paths compare like the game's file system: ignoring case.</summary>
    private sealed class MeshKindComparer : IEqualityComparer<(string Mesh, AssetProblemKind Kind)>
    {
        public bool Equals((string Mesh, AssetProblemKind Kind) x, (string Mesh, AssetProblemKind Kind) y) =>
            x.Kind == y.Kind && StringComparer.OrdinalIgnoreCase.Equals(x.Mesh, y.Mesh);

        public int GetHashCode((string Mesh, AssetProblemKind Kind) key) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(key.Mesh), key.Kind);
    }
}
