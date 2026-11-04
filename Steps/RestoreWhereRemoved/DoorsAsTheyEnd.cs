using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>Whether each door stands and where its navmesh link points in the final world, given which candidates go back.</summary>
internal sealed class DoorsAsTheyEnd(PresenceInPass presence, DoorLinks links)
{
    public bool Stands(RecordKey door) => !presence.EndsRemoved(door);

    public NavDoorLinkFact? FinalLinkOf(DoorLinkChange door) => links.FinalLinkOf(door, presence.GoesBack);
}
