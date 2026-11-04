using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsInClearedAreas.Contracts;

/// <summary>One of the cleaned mod's objects checked for standing in a cleared area.</summary>
/// <param name="ObjectsAround">The objects <paramref name="Area"/> counts, the object itself left out.</param>
/// <param name="Area">The share of the cleaned mod's drawn objects standing in the area around it, on the decisions of the earlier steps.</param>
/// <param name="KeepReason">Why a referenced object is kept; null otherwise.</param>
public sealed record ClearedAreaCheck(
    int TargetIndex,
    ClearedAreaObjectKind Kind,
    int ObjectsAround,
    AreaShare Area,
    LeftBehindOutcome Decision,
    KeepReason? KeepReason) : LeftBehindVerdict(TargetIndex, Decision, KeepReason);
