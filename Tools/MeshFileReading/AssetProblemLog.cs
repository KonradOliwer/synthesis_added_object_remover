using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;

namespace AddedObjectRemover;

/// <summary>The problem channel of one run's asset reading. Thread-safe.</summary>
internal sealed class AssetProblemLog : IMeshProblems
{
    private readonly record struct Recorded<T>(int Sequence, T Problem);

    private readonly ConcurrentDictionary<(string Mesh, AssetProblemKind Kind), Recorded<AssetProblem>> _meshProblems =
        new(new MeshKindComparer());
    private readonly ConcurrentDictionary<(ArchiveProblemKind Kind, string Subject), Recorded<ArchiveProblem>> _archiveProblems = new();
    private readonly ConcurrentDictionary<string, string> _printedSpellings = new(StringComparer.OrdinalIgnoreCase);
    private readonly AtomicCounter _sequence = new();

    public void Add(AssetProblem problem)
    {
        NoteSpelling(problem.Mesh);
        _meshProblems.TryAdd((problem.Mesh, problem.Kind), new Recorded<AssetProblem>(_sequence.Increment(), problem));
    }

    /// <summary>Notes a spelling a mesh was asked for. The log prints the one that sorts first (ordinal), so no thread's timing decides it; paths are never lower-cased.</summary>
    public void NoteSpelling(string mesh) =>
        _printedSpellings.AddOrUpdate(mesh, mesh, (_, known) => string.CompareOrdinal(mesh, known) < 0 ? mesh : known);

    public string PrintedSpelling(string mesh) =>
        _printedSpellings.TryGetValue(mesh, out var printed) ? printed : throw new UnreachableException($"No spelling of {mesh} was noted.");

    public void Add(ArchiveProblem problem) =>
        _archiveProblems.TryAdd((problem.Kind, problem.Subject), new Recorded<ArchiveProblem>(_sequence.Increment(), problem));

    public ProblemMark Mark() => new(_sequence.Value);

    public ImmutableArray<AssetProblem> Since(ProblemMark mark) =>
    [
        .. _meshProblems.Values
            .Where(recorded => recorded.Sequence > mark.Sequence)
            .Select(recorded => InPrintedSpelling(recorded.Problem)),
    ];

    /// <summary>The loose path and the operating system's message carry the reading thread's spelling, so they are re-spelled too.</summary>
    private AssetProblem InPrintedSpelling(AssetProblem problem)
    {
        var printed = PrintedSpelling(problem.Mesh);
        var recordedLoose = MeshFileSource.InLooseFileForm(problem.Mesh);
        var printedLoose = MeshFileSource.InLooseFileForm(printed);
        return problem with
        {
            Mesh = printed,
            LoosePath = problem.LoosePath?.Replace(recordedLoose, printedLoose, StringComparison.Ordinal),
            Detail = problem.Detail?.Replace(recordedLoose, printedLoose, StringComparison.Ordinal),
        };
    }

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
