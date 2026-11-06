namespace AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

/// <summary>
/// Why the restored parts an earlier copy of this patcher wrote beside the patch it hands on are not used. A fact of the user's
/// setup, never a stop: the run goes on without them.
/// </summary>
public abstract record RestoredPartsFileProblem
{
    /// <param name="Patch">The earlier copy's patch, beside which no file lies.</param>
    public sealed record NotFound(string Patch) : RestoredPartsFileProblem;

    public sealed record NotRead(string File, string ExceptionMessage) : RestoredPartsFileProblem;

    /// <param name="FirstLine">The file's first line, which names the kind of file and its version.</param>
    public sealed record UnknownFirstLine(string File, string FirstLine) : RestoredPartsFileProblem;

    /// <param name="Word">The first part word this version of the patcher does not know, as a copy of another version can write.</param>
    public sealed record UnknownPart(string File, string Word) : RestoredPartsFileProblem;

    /// <summary>The check value the file holds is not the one of its parts: the file is torn or was edited.</summary>
    public sealed record CheckValueDiffers(string File) : RestoredPartsFileProblem;
}
