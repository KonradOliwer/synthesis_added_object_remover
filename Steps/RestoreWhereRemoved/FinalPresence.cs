using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// Whether a placed record stands in the final world: it stands only when its whole enable-parent chain is enabled after this run's
/// orders, a record enabled with its parent exactly when the parent is, or when it is not if it is set to the opposite. A record
/// ends removed when the run removes it; otherwise, when a restore that brings back its presence goes back, its version just below
/// the cleaned mod's decides; otherwise its winning version does, a later plugin carrying the cleaned mod's disable forward
/// included. The chain is cut off at a record that is read as plainly present, such as one outside the target spaces, and at a cycle.
/// </summary>
/// <param name="removedByRun">The placed records the rounds or the other mods' objects step remove.</param>
/// <param name="winning">The winning version of each placed record that is removed or follows an enable parent.</param>
/// <param name="changedRecords">The placed records of other plugins the cleaned mod changed, by key.</param>
/// <param name="candidates">The index of each placed record whose areas let it go back, among those, by key.</param>
/// <param name="doors">Every door a navmesh the cleaned mod changed names, in key order.</param>
internal sealed class FinalPresence(
    IReadOnlySet<RecordKey> removedByRun,
    IReadOnlyDictionary<RecordKey, PlacedPresence> winning,
    IReadOnlyDictionary<RecordKey, RecordChangedByCleanedMod> changedRecords,
    IReadOnlyDictionary<RecordKey, int> candidates,
    ImmutableArray<RecordKey> doors)
{
    /// <param name="cleared">The placed records whose areas let them go back, in key order.</param>
    public static FinalPresence Of(CollectedObjects collected, IReadOnlySet<RecordKey> removedByRun, IReadOnlyList<RecordChangedByCleanedMod> cleared) => new(
        removedByRun,
        collected.WinningPresence,
        collected.ChangedRecords.ToDictionary(record => record.Key),
        cleared.Select((change, index) => KeyValuePair.Create(change.Key, index)).ToDictionary(),
        [.. collected.ChangedNavmeshes.SelectMany(navmesh => navmesh.Doors).Select(door => door.Door).Distinct().Order(RecordKeyOrder.Comparer)]);

    /// <param name="goesBack">Whether the placed record at an index among those whose areas let them go back goes back.</param>
    /// <remarks>
    /// Walks the chain without recursing, since a mod can chain any number of records: each record set to the opposite of its parent
    /// flips what the rest of the chain above it decides.
    /// </remarks>
    public bool EndsRemoved(RecordKey placed, Func<int, bool> goesBack)
    {
        HashSet<RecordKey> followed = [];
        var oppositeOfChainAbove = false;
        for (var current = placed; ; )
        {
            if (removedByRun.Contains(current)) return !oppositeOfChainAbove;
            var presence = FinalVersionOf(current, goesBack);
            if (presence.Removed) return !oppositeOfChainAbove;
            if (presence.Parent is not { } parent || !followed.Add(current)) return oppositeOfChainAbove;
            if (presence.OppositeOfParent) oppositeOfChainAbove = !oppositeOfChainAbove;
            current = parent;
        }
    }

    /// <summary>The doors the navmeshes the cleaned mod changed name that end removed, in key order.</summary>
    /// <param name="goesBack">Whether the placed record at an index among those whose areas let them go back goes back.</param>
    public ImmutableArray<RecordKey> DoorsEndingRemoved(Func<int, bool> goesBack) => [.. doors.Where(door => EndsRemoved(door, goesBack))];

    /// <summary>
    /// The placed records going back whose restore brings back their presence, among the record and its enable-parent chain as it
    /// ends, by their index among those whose areas let them go back, in chain order.
    /// </summary>
    /// <param name="goesBack">Whether the placed record at an index among those whose areas let them go back goes back.</param>
    public IEnumerable<int> CandidatesRestoringPresenceOnChainOf(RecordKey placed, Func<int, bool> goesBack)
    {
        HashSet<RecordKey> followed = [];
        for (RecordKey? record = placed; record is { } current && followed.Add(current); record = FinalVersionOf(current, goesBack).Parent)
        {
            if (changedRecords.TryGetValue(current, out var change) && PresenceGoesBack(change, goesBack)) yield return candidates[current];
        }
    }

    private PlacedPresence FinalVersionOf(RecordKey placed, Func<int, bool> goesBack) =>
        changedRecords.TryGetValue(placed, out var change) && PresenceGoesBack(change, goesBack) ? change.PresenceBefore : winning.GetValueOrDefault(placed);

    private bool PresenceGoesBack(RecordChangedByCleanedMod change, Func<int, bool> goesBack) =>
        change.Earlier.Undo.HasFlag(PlacedParts.Presence) && candidates.TryGetValue(change.Key, out var index) && goesBack(index);
}
