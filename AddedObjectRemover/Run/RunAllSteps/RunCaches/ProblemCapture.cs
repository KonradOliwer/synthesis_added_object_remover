using System.Collections.Immutable;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Run.RunAllSteps.RunCaches;

/// <summary>Hands out each problem once: mesh problems in the phase that first met them, archive problems and unexpected errors on request.</summary>
internal sealed class ProblemCapture(IMeshProblems problems)
{
    private ProblemMark _meshTakenUpTo;
    private ProblemMark _archiveTakenUpTo;
    private ProblemMark _unexpectedTakenUpTo;

    public PhaseProblems Take()
    {
        var taken = new PhaseProblems(problems.Since(_meshTakenUpTo));
        _meshTakenUpTo = problems.Mark();
        return taken;
    }

    public ImmutableArray<ArchiveProblem> TakeArchive()
    {
        var taken = problems.ArchiveProblemsSince(_archiveTakenUpTo);
        _archiveTakenUpTo = problems.Mark();
        return taken;
    }

    public ImmutableArray<UnexpectedError> TakeUnexpected()
    {
        var taken = problems.UnexpectedSince(_unexpectedTakenUpTo);
        _unexpectedTakenUpTo = problems.Mark();
        return taken;
    }
}
