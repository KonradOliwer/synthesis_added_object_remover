using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// Whether a placed record ends removed (<see cref="FinalPresence"/>) for one choice of the placed records that go back, each record
/// worked out once. The choice is fixed, so every answer depends only on its record, whichever thread asks first.
/// </summary>
/// <param name="goesBack">Whether the placed record at an index among those whose areas let them go back goes back.</param>
internal sealed class PresenceInPass(FinalPresence presence, Func<int, bool> goesBack)
{
    private readonly ComputedOncePerKey<RecordKey, bool> _endsRemoved = new(Publication.FirstWriteWins, EqualityComparer<RecordKey>.Default);

    public Func<int, bool> GoesBack => goesBack;

    public bool EndsRemoved(RecordKey placed) => _endsRemoved.Get(placed, () => presence.EndsRemoved(placed, goesBack));
}
