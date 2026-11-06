using System.Collections.Immutable;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Run.RunAllSteps.Contracts;

/// <summary>
/// The restored parts that reach the runs from an earlier copy of this patcher, and where the parts known after the last run go for
/// the next copy. Only a Standard run inside Synthesis reads and writes a file; a Batch run shares the parts between its runs in
/// memory, and a run outside Synthesis has no copy before or after it.
/// </summary>
public interface IRestoredPartsFile
{
    RestoredPartsRead Read();

    /// <param name="parts">The parts read, in their order, then the parts each run gave back, run by run.</param>
    void Write(ImmutableArray<RestoredPart> parts);
}

/// <param name="Parts">In the order the earlier copy wrote them; none when <paramref name="Problem"/> is set.</param>
/// <param name="Problem">Why the earlier copy's parts are not used; null when they are, or when no earlier copy of this patcher wrote the patch.</param>
public sealed record RestoredPartsRead(ImmutableArray<RestoredPart> Parts, RestoredPartsFileProblem? Problem);
