namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

/// <summary>What a left-behind step decided for an object: removed, restored to its earlier version, or kept, each with its reason.</summary>
public enum LeftBehindOutcome
{
    RemovedInsideOtherObject,
    RemovedSurroundingsRemoved,

    /// <summary>Disabled because its surroundings were removed, some of them restored.</summary>
    RemovedSurroundingsRemovedAndRestored,
    RemovedLinkedToRemoval,

    /// <summary>Disabled as a linked group member, with a member the round restored.</summary>
    RemovedLinkedToRestoration,
    RestoredInsideOtherObject,
    RestoredSurroundingsRemoved,

    /// <summary>Put back to its earlier version because its surroundings were removed, some of them restored.</summary>
    RestoredSurroundingsRemovedAndRestored,

    /// <summary>Put back to its earlier version as a linked group member, with a member that left its place.</summary>
    RestoredAsLinked,
    KeptTooFewSurroundingObjects,
    KeptSurroundingsMostlyKept,
    KeptProtectedType,
    KeptReferenced,

    /// <summary>Not removed, because the cell it stands in is deleted by a later plugin and the patch writes nothing into a deleted cell.</summary>
    KeptInDeletedCell,
}

public static class LeftBehindOutcomes
{
    public static bool IsRemoval(LeftBehindOutcome outcome) =>
        outcome is LeftBehindOutcome.RemovedInsideOtherObject or LeftBehindOutcome.RemovedSurroundingsRemoved
            or LeftBehindOutcome.RemovedSurroundingsRemovedAndRestored
            or LeftBehindOutcome.RemovedLinkedToRemoval or LeftBehindOutcome.RemovedLinkedToRestoration;

    public static bool IsRestoration(LeftBehindOutcome outcome) =>
        outcome is LeftBehindOutcome.RestoredInsideOtherObject or LeftBehindOutcome.RestoredSurroundingsRemoved
            or LeftBehindOutcome.RestoredSurroundingsRemovedAndRestored or LeftBehindOutcome.RestoredAsLinked;
}
