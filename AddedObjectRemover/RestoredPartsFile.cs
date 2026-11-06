using System.Collections.Immutable;
using AddedObjectRemover.Run.RunAllSteps.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover;

/// <summary>
/// The restored parts file of a Standard run inside Synthesis. Synthesis gives each patcher of a group its own output folder and hands
/// the next one the patch of the one before, so the file beside that patch is the one the copy before wrote. Its name is the patch's
/// file name followed by <see cref="FileNameEnding"/>, which no file Synthesis takes along with the patch ends in.
/// </summary>
/// <param name="sourcePatch">The patch the patcher before this one in the group wrote.</param>
/// <param name="outputPatch">Where this patcher's patch is written.</param>
/// <param name="earlierCopyWroteThePatch">The patch handed on carries this patcher's marker, so an earlier copy wrote into it.</param>
internal sealed class RestoredPartsFile(string sourcePatch, string outputPatch, bool earlierCopyWroteThePatch) : IRestoredPartsFile
{
    internal const string FileNameEnding = ".restored-parts.txt";

    public RestoredPartsRead Read() => earlierCopyWroteThePatch ? ReadBeside(sourcePatch) : new RestoredPartsRead([], Problem: null);

    /// <remarks>Written also without any part, so the next copy can tell nothing restored from no file.</remarks>
    public void Write(ImmutableArray<RestoredPart> parts) => Files.ReplaceText(PathBeside(outputPatch), RestoredPartsText.Write(parts));

    internal static string PathBeside(string patch) => patch + FileNameEnding;

    private static RestoredPartsRead ReadBeside(string patch)
    {
        var file = PathBeside(patch);
        string? text = null;
        var failure = FileFailures.Guard(() => text = Files.ReadTextIfPresent(file), exception => new RestoredPartsFileProblem.NotRead(file, exception.Message));
        if (failure is not null) return new RestoredPartsRead([], failure);
        return text is null ? new RestoredPartsRead([], new RestoredPartsFileProblem.NotFound(patch)) : RestoredPartsText.Read(text, file);
    }
}

/// <summary>
/// The restored parts of a run with no file: a Batch run, whose runs share them in memory, or a run outside Synthesis, which has no
/// copy of this patcher before or after it.
/// </summary>
internal sealed class NoRestoredPartsFile : IRestoredPartsFile
{
    public RestoredPartsRead Read() => new([], Problem: null);

    public void Write(ImmutableArray<RestoredPart> parts)
    {
    }
}
