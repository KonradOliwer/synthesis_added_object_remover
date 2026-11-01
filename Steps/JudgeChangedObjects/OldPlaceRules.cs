using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FitClashingObjects.Contracts;

namespace AddedObjectRemover.Steps.JudgeChangedObjects;

/// <summary>The rules that check an earlier version standing at its old place.</summary>
/// <param name="TooClose">
/// Builds the too-close rule that checks the given object, with the objects that exist only because of the cleaned mod as its
/// causes, except the cleaned mod's own version of that object.
/// </param>
/// <param name="ChangedTerrain">The changed-terrain rule, in the world without the cleaned mod.</param>
internal sealed record OldPlaceRules(Func<TargetObject, IPlacementRule> TooClose, IPlacementRule ChangedTerrain);
