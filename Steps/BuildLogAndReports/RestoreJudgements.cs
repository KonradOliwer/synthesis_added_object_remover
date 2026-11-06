using System.Collections.Immutable;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
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

    /// <summary>
    /// What the follow-up of what went back does to each placed record that went back and that it then pulls down or removes where it
    /// arrives, by key.
    /// </summary>
    public IReadOnlyDictionary<RecordKey, OtherModsObjectOutcome> FollowedAfterGoingBack { get; } = FollowedAfterGoingBackIn(result);

    /// <summary>The step judged nothing: no placed record, cell, land or navmesh.</summary>
    public bool IsEmpty => Result.Objects.IsEmpty && Result.Cells.IsEmpty && Result.Lands.IsEmpty && Result.Navmeshes.IsEmpty;

    /// <summary>The placed records that went back are the restore's candidates; the follow-up judges one of them only where it arrives.</summary>
    private static Dictionary<RecordKey, OtherModsObjectOutcome> FollowedAfterGoingBackIn(RestoreWhereRemovedResult result)
    {
        var restored = result.Objects.Where(judged => judged.IsRestored).Select(judged => judged.Change.Key).ToHashSet();
        return result.FollowUp.Others.Where(other => restored.Contains(other.Outcome.Object.Key)).ToDictionary(other => other.Outcome.Object.Key, other => other.Outcome);
    }
}
