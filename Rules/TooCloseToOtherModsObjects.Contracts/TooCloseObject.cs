using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.TooCloseToOtherModsObjects.Contracts;

/// <summary>A too-close target (index into the scanned targets), the first other object found and how it was found.</summary>
public readonly record struct TooCloseObject(int TargetIndex, OtherObject TooCloseTo, TooCloseCause Cause);
