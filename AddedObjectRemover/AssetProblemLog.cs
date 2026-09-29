using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>The problem channel of one run's asset reading. Thread-safe.</summary>
internal sealed class AssetProblemLog : IAssetProblems
{
    private readonly record struct Recorded<T>(long Sequence, T Problem);

    private readonly ConcurrentDictionary<(string Mesh, AssetProblemKind Kind), Recorded<AssetProblem>> _meshProblems =
        new(new MeshKindComparer());
    private readonly ConcurrentDictionary<(ArchiveProblemKind Kind, string Subject), Recorded<ArchiveProblem>> _archiveProblems = new();
    private long _sequence;

    public void Add(AssetProblem problem) =>
        _meshProblems.TryAdd((problem.Mesh, problem.Kind), new Recorded<AssetProblem>(NextSequence(), problem));

    public void Add(ArchiveProblem problem) =>
        _archiveProblems.TryAdd((problem.Kind, problem.Subject), new Recorded<ArchiveProblem>(NextSequence(), problem));

    public ProblemMark Mark() => new(Interlocked.Read(ref _sequence));

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

    private long NextSequence() => Interlocked.Increment(ref _sequence);

    /// <summary>Mesh paths compare like the game's file system: ignoring case.</summary>
    private sealed class MeshKindComparer : IEqualityComparer<(string Mesh, AssetProblemKind Kind)>
    {
        public bool Equals((string Mesh, AssetProblemKind Kind) x, (string Mesh, AssetProblemKind Kind) y) =>
            x.Kind == y.Kind && StringComparer.OrdinalIgnoreCase.Equals(x.Mesh, y.Mesh);

        public int GetHashCode((string Mesh, AssetProblemKind Kind) key) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(key.Mesh), key.Kind);
    }
}
