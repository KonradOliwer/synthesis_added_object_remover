using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;

/// <summary>The left-behind step's reason: the invisible object sits inside another mod's object.</summary>
public sealed record InsideAnotherModsObject(OtherId Container) : RemovalReason;

/// <summary>The left-behind step's reason: the surroundings of the invisible object were removed.</summary>
public sealed record SurroundingsRemoved : RemovalReason;
