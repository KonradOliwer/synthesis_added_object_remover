using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.WriteThePatch.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

/// <summary>Everything a run decided and found, from which the end of the log and the report files are built.</summary>
public sealed record RunOutcome : StepResults
{
    /// <param name="removals">The removals in the order the removal list reports them.</param>
    /// <param name="hints">The removed markers that may need a manual patch.</param>
    /// <param name="problems">Every mesh problem of the run, reading for the explanations included.</param>
    /// <param name="shapes">Taken after the explanations, so their mesh reads are counted.</param>
    public RunOutcome(
        StepResults decided,
        ImmutableArray<RemovedObject> removals,
        ImmutableArray<ManualPatchHint> hints,
        WriteSummary written,
        ImmutableArray<AssetProblem> problems,
        BoundsStats shapes)
        : base(decided)
    {
        Removals = removals;
        Hints = hints;
        Written = written;
        Problems = problems;
        Shapes = shapes;
    }

    public ImmutableArray<RemovedObject> Removals { get; }

    public ImmutableArray<ManualPatchHint> Hints { get; }

    public WriteSummary Written { get; }

    public ImmutableArray<AssetProblem> Problems { get; }

    public BoundsStats Shapes { get; }
}
