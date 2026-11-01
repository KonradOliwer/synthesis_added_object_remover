using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

/// <summary>The visible left-behind step's reason: the surroundings of the visible object were removed.</summary>
public sealed record VisibleObjectSurroundingsRemoved : RemovalReason;
