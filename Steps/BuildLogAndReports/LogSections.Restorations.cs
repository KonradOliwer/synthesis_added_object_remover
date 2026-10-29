using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    /// <summary>The detailed log's list of the restored objects, under a heading of its own; nothing when none was restored.</summary>
    public static LogSection BuildRestoredSection(
        CollectedObjects world, IReadOnlyList<ObjectWithReason> restorations, IRemovalDecisions final, ReportContext context)
    {
        if (!context.Detailed || restorations.Count == 0) return new LogSection("restored", []);

        List<string> lines = ["Restored:"];
        foreach (var restoration in restorations) lines.Add($"  {ObjectLine(world, restoration, final, context)}");
        return new LogSection("restored", [.. lines]);
    }

    /// <summary>The count of the restored objects by reason, which follows the removal summary; nothing when none was restored.</summary>
    public static LogSection BuildRestoreSummarySection(
        IReadOnlyList<ObjectWithReason> restorations,
        IRemovalDecisions final,
        PluginName target,
        StepOutcome changedTerrain,
        StepOutcome visibleLeftBehind) =>
        new(
            "restore-summary",
            restorations.Count == 0
                ? []
                :
                [
                    $"Restored {TextFormat.Count(restorations.Count)} {TargetOrigins.ChangedPhrase(target)} "
                    + $"({ReasonItems.Describe(restorations, final, changedTerrain, visibleLeftBehind, ReasonItems.ForRestorations)}).",
                ]);
}
