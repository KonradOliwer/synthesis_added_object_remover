namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

public enum VisibleLeftBehindOutcome
{
    RemovedSurroundingsRemoved,
    RemovedLinkedToRemoval,
    KeptTooFewSurroundingObjects,
    KeptSurroundingsMostlyKept,
    KeptProtectedType,
    KeptReferenced,
}

public static class VisibleLeftBehindOutcomes
{
    public static bool IsRemoval(VisibleLeftBehindOutcome outcome) =>
        outcome is VisibleLeftBehindOutcome.RemovedSurroundingsRemoved or VisibleLeftBehindOutcome.RemovedLinkedToRemoval;
}
