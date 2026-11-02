using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

public sealed partial class LogAndReports
{
    private ErrorMark _errorsTakenUpTo;
    private ProblemMark _archiveProblemsTakenUpTo;
    private ProblemMark _meshProblemsAtMeshSection;

    /// <summary>The errors recorded since the last take, worded.</summary>
    private LogSection TakeUnexpectedErrors() => TakeUnexpectedErrors([]);

    /// <param name="resultFailures">The failures the step result just handed over; they are worded together with the channel's, as one set.</param>
    private LogSection TakeUnexpectedErrors(IEnumerable<PartFailure> resultFailures)
    {
        var channelFailures = _unexpectedErrors.Since(_errorsTakenUpTo);
        _errorsTakenUpTo = _unexpectedErrors.Mark();
        return LogSections.BuildUnexpectedErrorsSection(resultFailures.Concat(channelFailures).Select(RunPartText.Word), _context);
    }

    /// <summary>Every mesh problem of the run.</summary>
    private LogSection TakeMeshProblems()
    {
        var problems = _caches.MeshFiles.Problems;
        _meshProblemsAtMeshSection = problems.Mark();
        return LogSections.BuildMeshProblemsSection(problems.Since(default), _context);
    }

    /// <remarks>Nothing reads meshes after the report tables are built, so a mesh or archive problem recorded since the end takes is a bug in the run.</remarks>
    private void RequireNoProblemRecordedSinceEndOfRunTakes()
    {
        var problems = _caches.MeshFiles.Problems;
        if (problems.Since(_meshProblemsAtMeshSection).Length > 0 || problems.ArchiveProblemsSince(_archiveProblemsTakenUpTo).Length > 0)
        {
            throw new UnreachableException("A mesh problem or an archive problem was recorded after the end of the run took them.");
        }
    }

    /// <summary>The archive and data-folder problems met since the last take; each is handed out once.</summary>
    private ImmutableArray<ArchiveProblem> TakeArchiveProblems()
    {
        var problems = _caches.MeshFiles.Problems;
        var taken = problems.ArchiveProblemsSince(_archiveProblemsTakenUpTo);
        _archiveProblemsTakenUpTo = problems.Mark();
        return taken;
    }
}
