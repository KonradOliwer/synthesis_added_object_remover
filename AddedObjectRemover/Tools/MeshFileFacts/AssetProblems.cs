using System.Collections.Immutable;

namespace AddedObjectRemover;

public enum AssetProblemKind { NotFound, LooseFileUnreadable, ArchiveExtractFailed, Unreadable, TrianglesUnreadable, ReadWarning, FooterRoot }

/// <summary>A problem with one mesh file.</summary>
/// <param name="Message">The log line, as printed.</param>
public sealed record AssetProblem(string Mesh, AssetProblemKind Kind, string Message);

public enum ArchiveProblemKind { ArchiveUnreadable, DataFolderUnlistable, IniArchiveListUnreadable, StripFieldsMissing }

/// <summary>A problem with where meshes come from or how they are read, not with one mesh.</summary>
/// <param name="Subject">What the problem is about (an archive, a folder), so each is recorded once.</param>
/// <param name="Message">The log line, as printed.</param>
public sealed record ArchiveProblem(ArchiveProblemKind Kind, string Subject, string Message);

/// <summary>A point in a run's problem channel; problems recorded later are "since" it.</summary>
public readonly record struct ProblemMark(int Sequence);

/// <summary>
/// The problems a run's mesh reading met, and the unexpected errors the whole run met. Each problem is recorded once
/// (per mesh and kind, per kind and subject, or per error line); the first record wins and later records of the same
/// problem are ignored. Which problems a phase records depends only on the meshes it reads, so every list is deterministic.
/// </summary>
public interface IMeshProblems
{
    ProblemMark Mark();

    void Add(UnexpectedError error);

    /// <summary>The unexpected errors first recorded after the mark, by message, then by subject position and label.</summary>
    ImmutableArray<UnexpectedError> UnexpectedSince(ProblemMark mark);

    /// <summary>The mesh problems first recorded after the mark, by mesh (ignoring case), then message.</summary>
    ImmutableArray<AssetProblem> Since(ProblemMark mark);

    /// <summary>The archive and loader problems first recorded after the mark, in the order recorded.</summary>
    ImmutableArray<ArchiveProblem> ArchiveProblemsSince(ProblemMark mark);
}
