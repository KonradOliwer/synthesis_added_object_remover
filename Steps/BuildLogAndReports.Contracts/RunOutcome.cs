using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.WriteThePatch.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

/// <summary>Everything a run decided and found, from which the end of the log and the report files are built.</summary>
public sealed record RunOutcome : StepResults
{
    /// <param name="removals">The removals in the order the removal list reports them.</param>
    /// <param name="restorations">The restored objects in the order the removal list reports them.</param>
    /// <param name="hints">The removed markers that may need a manual patch.</param>
    /// <param name="shapes">Taken after the explanations, so their mesh reads are counted.</param>
    public RunOutcome(
        StepResults decided,
        ImmutableArray<ObjectWithReason> removals,
        ImmutableArray<ObjectWithReason> restorations,
        ImmutableArray<ManualPatchHint> hints,
        WriteSummary written,
        BoundsStats shapes)
        : base(decided)
    {
        Removals = removals;
        Restorations = restorations;
        Hints = hints;
        Written = written;
        Shapes = shapes;
    }

    public ImmutableArray<ObjectWithReason> Removals { get; }

    public ImmutableArray<ObjectWithReason> Restorations { get; }

    public ImmutableArray<ManualPatchHint> Hints { get; }

    public WriteSummary Written { get; }

    public BoundsStats Shapes { get; }
}
