using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

/// <summary>A too-close target (index into the scanned targets) and the first other object found.</summary>
public readonly record struct TooCloseObject(int TargetIndex, OtherObject TooCloseTo);
