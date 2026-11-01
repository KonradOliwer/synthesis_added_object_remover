namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>Whether each door stands and where its navmesh link points in the final world, given which candidates go back.</summary>
/// <param name="goesBack">Whether the placed record at an index among those whose areas let them go back goes back.</param>
internal sealed class DoorsAsTheyEnd(FinalPresence presence, DoorLinks links, Func<int, bool> goesBack)
{
    public bool Stands(RecordKey door) => !presence.EndsRemoved(door, goesBack);

    public NavDoorLinkFact? FinalLinkOf(DoorLinkFact door) => links.FinalLinkOf(door, goesBack);
}
