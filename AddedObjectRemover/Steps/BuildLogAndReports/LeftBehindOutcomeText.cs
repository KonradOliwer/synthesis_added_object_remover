using System.Diagnostics;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static class LeftBehindOutcomeText
{
    public static string Describe(LeftBehindOutcome outcome) => outcome switch
    {
        LeftBehindOutcome.RemovedInsideOtherObject => "inside another mod's object",
        LeftBehindOutcome.RemovedSurroundingsRemoved => "surroundings removed",
        LeftBehindOutcome.KeptTooFewSurroundingObjects => "too few surrounding objects",
        LeftBehindOutcome.KeptSurroundingsMostlyKept => "surroundings mostly kept",
        LeftBehindOutcome.KeptProtectedType => "protected type",
        LeftBehindOutcome.KeptReferenced => "referenced",
        _ => throw new UnreachableException($"Unknown left-behind outcome {outcome}."),
    };

    /// <summary>The outcome's reason, with the linking record for a referenced object.</summary>
    public static string DescribeReason(LeftBehindCheck evaluation) =>
        evaluation.KeepReason is { } keepReason
            ? $"{Describe(evaluation.Decision)}: {KeepReasonText.Detail(keepReason)}"
            : Describe(evaluation.Decision);
}
