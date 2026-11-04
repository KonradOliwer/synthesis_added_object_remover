using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>What the guards' refusals make of the judged placed records.</summary>
internal static class PlacedJudgments
{
    /// <summary>Each placed record judged, with the first refusal of each whose areas let it go back.</summary>
    /// <param name="cleared">The records whose areas let them go back, in key order.</param>
    /// <param name="refusals">By their index among <paramref name="cleared"/>.</param>
    public static ImmutableArray<PlacedChangeJudged> WithRefusals(PlacedChangeJudged[] judged, List<RecordChangedByCleanedMod> cleared, ImmutableArray<PlacedRefusal?> refusals)
    {
        var refusalOf = cleared.Zip(refusals).Where(pair => pair.Second is not null).ToDictionary(pair => pair.First.Key, pair => pair.Second!);
        return [.. judged.Select(change => refusalOf.TryGetValue(change.Change.Key, out var refused) ? change with { Refused = refused, IsRestored = false } : change)];
    }

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
