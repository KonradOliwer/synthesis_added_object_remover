using System.Diagnostics;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static class LeftBehindOutcomeText
{
    public static string Describe(LeftBehindOutcome outcome) => outcome switch
    {
        LeftBehindOutcome.RemovedInsideOtherObject => "inside another mod's object",
        LeftBehindOutcome.RemovedSurroundingsRemoved => "surroundings removed",
        LeftBehindOutcome.RemovedSurroundingsRemovedAndRestored => "surroundings removed and restored",
        LeftBehindOutcome.RemovedLinkedToRemoval => "linked to a removed object",
        LeftBehindOutcome.RemovedLinkedToRestoration => "linked to a restored object",
        LeftBehindOutcome.RestoredInsideOtherObject => "inside another mod's object",
        LeftBehindOutcome.RestoredSurroundingsRemoved => "surroundings removed",
        LeftBehindOutcome.RestoredSurroundingsRemovedAndRestored => "surroundings removed and restored",
        LeftBehindOutcome.RestoredAsLinked => "linked to an object that left its place",
        LeftBehindOutcome.RemovedInClearedArea => "in a cleared area",
        LeftBehindOutcome.KeptTooFewObjectsAround => "too few of the cleaned mod's other objects around",
        LeftBehindOutcome.KeptTooFewOccupiedDirections => "too few directions with objects",
        LeftBehindOutcome.KeptSurroundingsMostlyKept => "surroundings mostly kept",
        LeftBehindOutcome.KeptAreaStillStands => "area not cleared",
        LeftBehindOutcome.KeptProtectedType => "protected type",
        LeftBehindOutcome.KeptReferenced => "referenced",
        LeftBehindOutcome.KeptInDeletedCell => "in a deleted cell",
        _ => throw new UnreachableException($"Unknown left-behind outcome {outcome}."),
    };

    /// <summary>The objects a method of "Objects left behind" keeps by its minimum, named by that minimum.</summary>
    public static string TooFewObjectsAround(int minObjectsAround) => $"fewer than {minObjectsAround} of the cleaned mod's other objects around";

    /// <summary>The outcome as a count of objects names the minimum it was judged by.</summary>
    public static string Describe(LeftBehindOutcome outcome, int minObjectsAround) =>
        outcome == LeftBehindOutcome.KeptTooFewObjectsAround ? TooFewObjectsAround(minObjectsAround) : Describe(outcome);

    /// <summary>What the step did with the object.</summary>
    public static string Verdict(LeftBehindVerdict evaluation) =>
        evaluation.IsRemoved ? "removed" : evaluation.IsRestored ? "restored" : "kept";

    /// <summary>The outcome's reason, with the linking record for a referenced object.</summary>
    public static string DescribeReason(LeftBehindVerdict evaluation) =>
        evaluation.KeepReason is { } keepReason
            ? $"{Describe(evaluation.Decision)}: {KeepReasonText.Detail(keepReason)}"
            : Describe(evaluation.Decision);
}
