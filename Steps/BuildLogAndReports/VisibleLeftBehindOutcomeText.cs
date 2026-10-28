using System.Diagnostics;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static class VisibleLeftBehindOutcomeText
{
    public static string Describe(VisibleLeftBehindOutcome outcome) => outcome switch
    {
        VisibleLeftBehindOutcome.RemovedSurroundingsRemoved => "surroundings removed",
        VisibleLeftBehindOutcome.RemovedLinkedToRemoval => "linked to a removed object",
        VisibleLeftBehindOutcome.KeptTooFewSurroundingObjects => "too few surrounding objects",
        VisibleLeftBehindOutcome.KeptSurroundingsMostlyKept => "surroundings mostly kept",
        VisibleLeftBehindOutcome.KeptProtectedType => "protected type",
        VisibleLeftBehindOutcome.KeptReferenced => "referenced",
        _ => throw new UnreachableException($"Unknown visible left-behind outcome {outcome}."),
    };

    /// <summary>The outcome's reason, with the linking record for a referenced object.</summary>
    public static string DescribeReason(VisibleLeftBehindCheck evaluation) =>
        evaluation.KeepReason is { } keepReason
            ? $"{Describe(evaluation.Decision)}: {KeepReasonText.Detail(keepReason)}"
            : Describe(evaluation.Decision);

    /// <summary>The kind's name as the settings screen lists it under "Never remove these as visible objects left behind".</summary>
    public static string KindName(VisibleObjectKind kind) => kind switch
    {
        VisibleObjectKind.NpcsAndCreatures => "NPCs and creatures",
        VisibleObjectKind.Containers => "Containers",
        VisibleObjectKind.Furniture => "Furniture",
        VisibleObjectKind.Activators => "Activators",
        _ => throw new UnreachableException($"Unknown visible object kind {kind}."),
    };

    /// <summary>Empty for an object of no listed kind.</summary>
    public static string KindNameOrEmpty(VisibleObjectKind? kind) => kind is { } listed ? KindName(listed) : string.Empty;
}
