using System.Diagnostics;

namespace AddedObjectRemover.Steps.ObjectsLeftBehind.Contracts;

/// <summary>The outcomes of both left-behind steps; only the invisible step judges objects inside another mod's object.</summary>
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

    /// <summary>The outcomes of an object disabled as a linked group member.</summary>
    public static bool IsRemovedAsLinked(LeftBehindOutcome outcome) =>
        outcome is LeftBehindOutcome.RemovedLinkedToRemoval or LeftBehindOutcome.RemovedLinkedToRestoration;

    public static bool IsRestoration(LeftBehindOutcome outcome) =>
        outcome is LeftBehindOutcome.RestoredInsideOtherObject or LeftBehindOutcome.RestoredSurroundingsRemoved
            or LeftBehindOutcome.RestoredSurroundingsRemovedAndRestored or LeftBehindOutcome.RestoredAsLinked;

    /// <summary>The outcome of an object the rule proposed to remove and the round restored instead.</summary>
    public static LeftBehindOutcome AsRestored(LeftBehindOutcome proposed) => proposed switch
    {
        LeftBehindOutcome.RemovedInsideOtherObject => LeftBehindOutcome.RestoredInsideOtherObject,
        LeftBehindOutcome.RemovedSurroundingsRemoved => LeftBehindOutcome.RestoredSurroundingsRemoved,
        LeftBehindOutcome.RemovedSurroundingsRemovedAndRestored => LeftBehindOutcome.RestoredSurroundingsRemovedAndRestored,
        _ => throw new UnreachableException($"The left-behind rule did not propose to remove an object with the outcome {proposed}."),
    };
}
