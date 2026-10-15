using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

/// <summary>An other-mod placed NPC with a possible body sized as a point because its real size could not be determined.</summary>
public readonly record struct PointNpc(OtherObject Npc, PointReason Reason);
