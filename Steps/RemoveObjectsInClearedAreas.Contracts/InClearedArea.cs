using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsInClearedAreas.Contracts;

/// <summary>The step's reason: almost nothing of the cleaned mod's drawn objects still stands in the area around the object.</summary>
/// <param name="Area">The share standing there when the object was judged.</param>
public sealed record InClearedArea(AreaShare Area) : RemovalReason;
