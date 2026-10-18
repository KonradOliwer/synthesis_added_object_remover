using System.Collections.Immutable;

namespace AddedObjectRemover;

public enum AssetProblemKind { NotFound, LooseFileUnreadable, ArchiveExtractFailed, Unreadable, TrianglesUnreadable, TrianglesUnreadableAfterGoodRead, ReadWarning, FooterRoot }

/// <summary>The two block indices of a footer root that was used instead of the first-node root.</summary>
public readonly record struct FooterRootBlocks(int Used, int FirstNodeRoot);

/// <summary>A problem with one mesh file, as facts only; the wording is the reader's.</summary>
/// <param name="Detail">
/// The exception message; only for <see cref="AssetProblemKind.LooseFileUnreadable"/>, <see cref="AssetProblemKind.ArchiveExtractFailed"/>
/// and an unexpected <see cref="AssetProblemKind.TrianglesUnreadableAfterGoodRead"/>.
/// </param>
/// <param name="LoosePath">The loose file that could not be read; only for <see cref="AssetProblemKind.LooseFileUnreadable"/>.</param>
/// <param name="ExceptionType">The type name of the archive extract's exception; only for <see cref="AssetProblemKind.ArchiveExtractFailed"/>.</param>
/// <param name="FooterRoot">Only for <see cref="AssetProblemKind.FooterRoot"/>.</param>
/// <param name="ReadFailure">
/// Only for <see cref="AssetProblemKind.Unreadable"/>, <see cref="AssetProblemKind.TrianglesUnreadable"/>
/// and a <see cref="AssetProblemKind.TrianglesUnreadableAfterGoodRead"/> that is not unexpected.
/// </param>
/// <param name="MismatchedStrips">The NiTriStrips shapes whose strip lengths do not match their points; only for <see cref="AssetProblemKind.ReadWarning"/>.</param>
public sealed record AssetProblem(
    string Mesh,
    AssetProblemKind Kind,
    string? Detail,
    string? LoosePath,
    string? ExceptionType,
    FooterRootBlocks? FooterRoot,
    MeshReadFailure? ReadFailure,
    int? MismatchedStrips)
{
    public static AssetProblem NotFound(string mesh) => new(mesh, AssetProblemKind.NotFound, null, null, null, null, null, null);

    public static AssetProblem Unreadable(string mesh, MeshReadFailure failure) =>
        new(mesh, AssetProblemKind.Unreadable, null, null, null, null, failure, null);

    public static AssetProblem TrianglesUnreadable(string mesh, MeshReadFailure failure) =>
        new(mesh, AssetProblemKind.TrianglesUnreadable, null, null, null, null, failure, null);

    public static AssetProblem TrianglesUnreadableAfterGoodRead(string mesh, MeshReadFailure failure) =>
        new(mesh, AssetProblemKind.TrianglesUnreadableAfterGoodRead, null, null, null, null, failure, null);

    public static AssetProblem TrianglesUnreadableAfterGoodRead(string mesh, string unexpectedFailure) =>
        new(mesh, AssetProblemKind.TrianglesUnreadableAfterGoodRead, unexpectedFailure, null, null, null, null, null);

    public static AssetProblem ReadWarning(string mesh, int mismatchedStrips) =>
        new(mesh, AssetProblemKind.ReadWarning, null, null, null, null, null, mismatchedStrips);

    public static AssetProblem LooseFileUnreadable(string mesh, string loosePath, string exceptionMessage) =>
        new(mesh, AssetProblemKind.LooseFileUnreadable, exceptionMessage, loosePath, null, null, null, null);

    public static AssetProblem ArchiveExtractFailed(string mesh, string exceptionType, string exceptionMessage) =>
        new(mesh, AssetProblemKind.ArchiveExtractFailed, exceptionMessage, null, exceptionType, null, null, null);

    public static AssetProblem FooterRootUsed(string mesh, FooterRootBlocks blocks) =>
        new(mesh, AssetProblemKind.FooterRoot, null, null, null, blocks, null, null);
}

public enum ArchiveProblemKind { ArchiveUnreadable, DataFolderUnlistable, IniArchiveListUnreadable, StripFieldsMissing }

/// <summary>A problem with where meshes come from or how they are read, not with one mesh, as facts only.</summary>
/// <param name="Subject">What the problem is about (an archive, a folder), so each is recorded once.</param>
/// <param name="ExceptionMessage">The exception message; null for <see cref="ArchiveProblemKind.StripFieldsMissing"/>.</param>
public sealed record ArchiveProblem(ArchiveProblemKind Kind, string Subject, string? ExceptionMessage);

/// <summary>A point in a run's problem channel; problems recorded later are "since" it.</summary>
public readonly record struct ProblemMark(int Sequence);

/// <summary>
/// The problems a run's mesh reading met. Each problem is recorded once (per mesh and kind, or per kind and subject);
/// the first record wins and later records of the same problem are ignored. Which problems a phase records depends only
/// on the meshes it reads, so every list is deterministic.
/// </summary>
public interface IMeshProblems
{
    ProblemMark Mark();

    /// <summary>The mesh problems first recorded after the mark, in no particular order: the caller orders them.</summary>
    ImmutableArray<AssetProblem> Since(ProblemMark mark);

    /// <summary>The archive and loader problems first recorded after the mark, in the order recorded.</summary>
    ImmutableArray<ArchiveProblem> ArchiveProblemsSince(ProblemMark mark);

    /// <summary>The spelling of a mesh path to print: of all the spellings the mesh was asked for, the one that sorts first (ordinal). Throws for a mesh nobody asked for.</summary>
    string PrintedSpelling(string mesh);
}
