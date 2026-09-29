using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>Everything a run decided and found, from which the end of the log and the report files are built.</summary>
internal sealed record Outcome : Decided
{
    /// <param name="removals">The removals in the order the removal list reports them.</param>
    /// <param name="problems">Every mesh problem of the run, reading for the explanations included.</param>
    /// <param name="archiveProblems">Every archive and loader problem of the run.</param>
    /// <param name="shapes">Taken after the explanations, so their mesh reads are counted.</param>
    public Outcome(
        Decided decided,
        ImmutableArray<Removal> removals,
        WriteSummary written,
        ImmutableArray<AssetProblem> problems,
        ImmutableArray<ArchiveProblem> archiveProblems,
        BoundsStats shapes)
        : base(decided)
    {
        Removals = removals;
        Hints = ManualPatchHints.Hints(decided, removals);
        Written = written;
        Problems = problems;
        ArchiveProblems = archiveProblems;
        Shapes = shapes;
    }

    public ImmutableArray<Removal> Removals { get; }

    public ImmutableArray<ManualPatchHint> Hints { get; }

    public WriteSummary Written { get; }

    public ImmutableArray<AssetProblem> Problems { get; }

    public ImmutableArray<ArchiveProblem> ArchiveProblems { get; }

    public BoundsStats Shapes { get; }
}

/// <summary>The lines of one part of the log, printed together.</summary>
/// <param name="Id">Names the part, for tests.</param>
internal sealed record LogSection(string Id, ImmutableArray<string> Lines);

/// <summary>One report file's content, not yet formatted as CSV text.</summary>
internal sealed record CsvTable(string FileName, ImmutableArray<string> Header, ImmutableArray<ImmutableArray<string>> Rows);

/// <summary>What the report builders read besides a step's result.</summary>
/// <param name="Shapes">For world boxes of target objects.</param>
/// <param name="Detailed">Whether the detailed log is on: lines and timings only it shows are left out otherwise.</param>
internal sealed record ReportContext(IBaseFacts Bases, ShapeCatalog Shapes, bool Detailed);

/// <summary>The problems first met during one phase, printed where the phase prints them.</summary>
internal sealed record PhaseProblems(ImmutableArray<ArchiveProblem> Archive, ImmutableArray<AssetProblem> Assets);

/// <summary>A report file that was written.</summary>
/// <param name="Elapsed">How long formatting and writing it took.</param>
internal sealed record WrittenTable(string FileName, int Rows, string Path, TimeSpan Elapsed);
