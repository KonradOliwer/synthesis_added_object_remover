using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>What the guards' refusals make of the judged placed records.</summary>
internal static class PlacedJudgments
{
    /// <summary>
    /// Each placed record judged, with the first refusal of each whose areas let it go back, or the rule that lost its copy, which
    /// keeps it as a refused copy does (<see cref="ChangeJudge.WithCloning"/>).
    /// </summary>
    /// <param name="cleared">The records whose areas let them go back, in key order.</param>
    /// <param name="refusals">By their index among <paramref name="cleared"/>.</param>
    /// <param name="clones">The copy rule's outcome of each, by the same index.</param>
    public static ImmutableArray<PlacedChangeJudged> WithOutcomes(
        PlacedChangeJudged[] judged, List<RecordChangedByCleanedMod> cleared, ImmutableArray<PlacedRefusal?> refusals, ImmutableArray<CloneJudged?> clones)
    {
        var refusalOf = cleared.Zip(refusals).Where(pair => pair.Second is not null).ToDictionary(pair => pair.First.Key, pair => pair.Second!);
        var lostCloneOf = cleared.Zip(clones).Where(pair => pair.Second is { RefusedBy: not null }).ToDictionary(pair => pair.First.Key, pair => pair.Second!);
        return [.. judged.Select(change => OutcomeOf(change, refusalOf.GetValueOrDefault(change.Change.Key), lostCloneOf.GetValueOrDefault(change.Change.Key)))];
    }

    private static PlacedChangeJudged OutcomeOf(PlacedChangeJudged judged, PlacedRefusal? refused, CloneJudged? lostClone) =>
        refused is not null ? judged with { Refused = refused, IsRestored = false }
        : lostClone is not null ? ChangeJudge.WithCloning(judged, lostClone)
        : judged;

    /// <summary>
    /// The placed records whose guard check stopped, which stay as the cleaned mod changed them, and those whose copy's check stopped,
    /// which get no copy, in the order of <paramref name="objects"/>.
    /// </summary>
    public static ImmutableArray<PartFailure> FailedChecks(ImmutableArray<PlacedChangeJudged> objects) =>
    [
        .. objects
            .Select((judged, position) => (Judged: judged, Position: position, Failure: judged.Refused?.Failure ?? judged.Cloning?.Failure))
            .Where(entry => entry.Failure is not null)
            .Select(entry => new PartFailure(
                RestoreWhereRemovedParts.ObjectCheck,
                entry.Failure!,
                new FailureSubject(entry.Judged.Change.Key.ToString(), entry.Judged.Change.EditorId, entry.Position))),
    ];
}
