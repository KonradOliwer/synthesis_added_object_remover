using System.Collections.Immutable;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>
/// What the step that restores what the cleaned mod changed where it was removed judged, with the pairing the log and the report
/// files both need made once: each navmesh with its group.
/// </summary>
internal sealed class RestoreJudgements(RestoreWhereRemovedResult result)
{
    public RestoreWhereRemovedResult Result { get; } = result;

    public ImmutableArray<NavmeshOutcome> Navmeshes { get; } = RestoreOutcomes.NavmeshOutcomes(result);

    /// <summary>The step judged nothing: no placed record, cell, land or navmesh.</summary>
    public bool IsEmpty => Result.Objects.IsEmpty && Result.Cells.IsEmpty && Result.Lands.IsEmpty && Result.Navmeshes.IsEmpty;
}
