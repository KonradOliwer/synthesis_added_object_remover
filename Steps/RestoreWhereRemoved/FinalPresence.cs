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
    public bool EndsRemoved(RecordKey placed, Func<int, bool> goesBack) => EndsRemoved(placed, goesBack, followed: null);

    /// <summary>The doors the navmeshes the cleaned mod changed name that end removed, in key order.</summary>
    /// <param name="goesBack">Whether the placed record at an index among those whose areas let them go back goes back.</param>
    public ImmutableArray<RecordKey> DoorsEndingRemoved(Func<int, bool> goesBack) => [.. doors.Where(door => EndsRemoved(door, goesBack))];

    /// <param name="followed">The records whose enable parent the walk has followed so far; null before it follows the first.</param>
    private bool EndsRemoved(RecordKey placed, Func<int, bool> goesBack, HashSet<RecordKey>? followed)
    {
        if (removedByRun.Contains(placed)) return true;
        var presence = FinalVersionOf(placed, goesBack);
        if (presence.Removed) return true;
        if (presence.Parent is not { } parent) return false;

        followed ??= [];
        return followed.Add(placed) && EndsRemoved(parent, goesBack, followed) != presence.OppositeOfParent;
    }

    private PlacedPresence FinalVersionOf(RecordKey placed, Func<int, bool> goesBack) =>
        changedRecords.TryGetValue(placed, out var change) && PresenceGoesBack(change, goesBack) ? change.PresenceBefore : winning.GetValueOrDefault(placed);

    private bool PresenceGoesBack(RecordChangedByCleanedMod change, Func<int, bool> goesBack) =>
        change.Earlier.Undo.HasFlag(PlacedParts.Presence) && candidates.TryGetValue(change.Key, out var index) && goesBack(index);
}
