using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// Where each door's navmesh link points once written (design §6.4 "Doors"). A door whose position, rotation or presence the cleaned
/// mod last changed takes its link with it: the link before the cleaned mod when its placed restore goes back, the winning link
/// otherwise; its placed restore writes the link. Any other door's link goes back with the navmesh group that names it, which writes it.
/// </summary>
/// <param name="changedRecords">The placed records of other plugins the cleaned mod changed.</param>
/// <param name="candidates">The placed records whose areas let them go back, in key order, each with the parts its restore writes.</param>
internal sealed class DoorLinks(IEnumerable<RecordChangedByCleanedMod> changedRecords, IReadOnlyList<RecordChangedByCleanedMod> candidates)
{
    private const PlacedParts PlacementAndPresence = PlacedParts.Position | PlacedParts.Rotation | PlacedParts.Presence;

    /// <summary>The doors whose link goes back with their placed restore.</summary>
    private readonly IReadOnlySet<RecordKey> _goWithPlacement = changedRecords.Where(GoesWithPlacement).Select(change => change.Key).ToHashSet();

    private readonly IReadOnlyDictionary<RecordKey, int> _candidateOf = candidates.Select((change, index) => KeyValuePair.Create(change.Key, index)).ToDictionary();

    /// <summary>The record with the parts its restore writes: the door link too, when it goes with the door and the cleaned mod is the last to change it.</summary>
    public static RecordChangedByCleanedMod WithLinkGoingBack(RecordChangedByCleanedMod change) =>
        GoesWithPlacement(change) && change.DoorLink!.NavDoorLink is PartOutcome.Undo
            ? change with { Earlier = change.Earlier with { Undo = change.Earlier.Undo | PlacedParts.NavDoorLink } }
            : change;

    /// <summary>A door whose position, rotation or presence goes back, whose link goes back with it.</summary>
    public static bool GoesWithPlacement(RecordChangedByCleanedMod change) => change.DoorLink is not null && (change.Earlier.Undo & PlacementAndPresence) != 0;

    /// <summary>The link once written when it goes back: the one before the cleaned mod when the cleaned mod is the last to change it, the winning one otherwise.</summary>
    public static NavDoorLinkFact? LinkGoingBack(DoorLinkFact door) => door.NavDoorLink is PartOutcome.Undo ? door.Earlier : door.Current;

    /// <summary>Whether a navmesh group writes the door's link: the cleaned mod is the last to change it, and it does not go with the door.</summary>
    public bool WrittenByGroup(DoorLinkFact door) => door.NavDoorLink is PartOutcome.Undo && !_goWithPlacement.Contains(door.Door);

    /// <param name="goesBack">Whether the placed record at an index among the candidates goes back.</param>
    public NavDoorLinkFact? FinalLinkOf(DoorLinkFact door, Func<int, bool> goesBack) =>
        !_goWithPlacement.Contains(door.Door) || (_candidateOf.TryGetValue(door.Door, out var candidate) && goesBack(candidate)) ? LinkGoingBack(door) : door.Current;

    /// <summary>The restore of each of the doors that is a candidate, as its placed restore writes it, in the order given.</summary>
    public IEnumerable<WriteOrder> RestoresOf(IEnumerable<RecordKey> doors) =>
        doors.Where(door => _candidateOf.ContainsKey(door)).Select(door => new UndoPlacedParts(door, candidates[_candidateOf[door]].Earlier.Undo));
}
