namespace AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;

public enum LeftBehindOutcome
{
    RemovedInsideOtherObject,
    RemovedSurroundingsRemoved,
    KeptTooFewSurroundingObjects,
    KeptSurroundingsMostlyKept,
    KeptProtectedType,
    KeptReferenced,
}

public static class LeftBehindOutcomes
{
    public static bool IsRemoval(LeftBehindOutcome outcome) =>
        outcome is LeftBehindOutcome.RemovedInsideOtherObject or LeftBehindOutcome.RemovedSurroundingsRemoved;
}
