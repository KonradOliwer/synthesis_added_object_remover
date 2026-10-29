using System.Diagnostics;

namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

public enum VisibleLeftBehindOutcome
{
    RemovedSurroundingsRemoved,

    /// <summary>Disabled because its surroundings were removed, some of them restored.</summary>
    RemovedSurroundingsRemovedAndRestored,
    RemovedLinkedToRemoval,

    /// <summary>Disabled as a linked group member, with a member the round restored.</summary>
    RemovedLinkedToRestoration,
    RestoredSurroundingsRemoved,

    /// <summary>Put back to its earlier version because its surroundings were removed, some of them restored.</summary>
    RestoredSurroundingsRemovedAndRestored,

    /// <summary>Put back to its earlier version as a linked group member, with a member that left its place.</summary>
    RestoredAsLinked,
    KeptTooFewSurroundingObjects,
    KeptSurroundingsMostlyKept,
    KeptProtectedType,
    KeptReferenced,
}

public static class VisibleLeftBehindOutcomes
{
    public static bool IsRemoval(VisibleLeftBehindOutcome outcome) =>
        outcome is VisibleLeftBehindOutcome.RemovedSurroundingsRemoved or VisibleLeftBehindOutcome.RemovedSurroundingsRemovedAndRestored
            or VisibleLeftBehindOutcome.RemovedLinkedToRemoval or VisibleLeftBehindOutcome.RemovedLinkedToRestoration;

    /// <summary>The outcomes of an object disabled as a linked group member.</summary>
    public static bool IsRemovedAsLinked(VisibleLeftBehindOutcome outcome) =>
        outcome is VisibleLeftBehindOutcome.RemovedLinkedToRemoval or VisibleLeftBehindOutcome.RemovedLinkedToRestoration;

    public static bool IsRestoration(VisibleLeftBehindOutcome outcome) =>
        outcome is VisibleLeftBehindOutcome.RestoredSurroundingsRemoved or VisibleLeftBehindOutcome.RestoredSurroundingsRemovedAndRestored
            or VisibleLeftBehindOutcome.RestoredAsLinked;

    /// <summary>The outcome of an object the rule proposed to remove and the round restored instead.</summary>
    public static VisibleLeftBehindOutcome AsRestored(VisibleLeftBehindOutcome proposed) => proposed switch
    {
        VisibleLeftBehindOutcome.RemovedSurroundingsRemoved => VisibleLeftBehindOutcome.RestoredSurroundingsRemoved,
        VisibleLeftBehindOutcome.RemovedSurroundingsRemovedAndRestored => VisibleLeftBehindOutcome.RestoredSurroundingsRemovedAndRestored,
        _ => throw new UnreachableException($"The visible left-behind rule did not propose to remove an object with the outcome {proposed}."),
    };
}
