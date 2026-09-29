using System.Collections.Immutable;

namespace AddedObjectRemover;

internal enum AssetProblemKind { NotFound, LooseFileUnreadable, ArchiveExtractFailed, Unreadable, TrianglesUnreadable, ReadWarning, FooterRoot }

/// <summary>A problem with one mesh file.</summary>
/// <param name="Message">The log line, as printed.</param>
internal sealed record AssetProblem(string Mesh, AssetProblemKind Kind, string Message);

internal enum ArchiveProblemKind { ArchiveUnreadable, DataFolderUnlistable, IniArchiveListUnreadable, NifLoaderWarmUpFailed, StripFieldsMissing }

/// <summary>A problem with where meshes come from or how they are read, not with one mesh.</summary>
/// <param name="Subject">What the problem is about (an archive, a folder), so each is recorded once.</param>
/// <param name="Message">The log line, as printed.</param>
internal sealed record ArchiveProblem(ArchiveProblemKind Kind, string Subject, string Message);

/// <summary>A point in a run's problem channel; problems recorded later are "since" it.</summary>
internal readonly record struct ProblemMark(long Sequence);

/// <summary>
/// The problems a run's asset reading met. Each problem is recorded once (per mesh and kind, or
/// per kind and subject); the first record wins and nothing is ever dropped. Which problems a phase
/// records depends only on the meshes it reads, so every list is deterministic.
/// </summary>
internal interface IAssetProblems
{
    ProblemMark Mark();

    /// <summary>The mesh problems first recorded after the mark, by mesh (ignoring case), then message.</summary>
    ImmutableArray<AssetProblem> Since(ProblemMark mark);

    /// <summary>The archive and loader problems first recorded after the mark, in the order recorded.</summary>
    ImmutableArray<ArchiveProblem> ArchiveProblemsSince(ProblemMark mark);
}
