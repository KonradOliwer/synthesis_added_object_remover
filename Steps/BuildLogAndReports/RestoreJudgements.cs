using System.Collections.Immutable;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>A LAND record judged, with the facts of each of its four quarters.</summary>
internal sealed record LandJudgement(LandChangeJudged Land, ImmutableArray<LandQuarterFacts> Quarters);

/// <summary>
/// What the step that restores what the cleaned mod changed where it was removed judged, with the pairings the log and the report
/// files both need made once: each navmesh with its group, each land with its quarters.
/// </summary>
internal sealed class RestoreJudgements(RestoreWhereRemovedResult result)
{
    public RestoreWhereRemovedResult Result { get; } = result;

    public ImmutableArray<NavmeshOutcome> Navmeshes { get; } = RestoreOutcomes.NavmeshOutcomes(result);

    public ImmutableArray<LandJudgement> Lands { get; } = [.. result.Lands.Select(land => new LandJudgement(land, RestoreOutcomes.QuartersOf(land)))];

    /// <summary>The step judged nothing: no placed record, cell, land or navmesh.</summary>
    public bool IsEmpty => Result.Objects.IsEmpty && Result.Cells.IsEmpty && Result.Lands.IsEmpty && Result.Navmeshes.IsEmpty;
}
