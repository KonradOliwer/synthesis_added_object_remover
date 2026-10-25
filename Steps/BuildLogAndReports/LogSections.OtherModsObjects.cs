using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    private const string LeftWithoutSupportName = "Other mods' objects left without support";
    private const string HeldUpName = "Keeping what other mods' objects rest on";

    /// <summary>
    /// The detailed log's count of the target objects kept because other mods' objects rest on them, directly or through other
    /// such objects, when there is one, and otherwise its step-time line; the not-performed line when an unexpected error stopped the search.
    /// </summary>
    public static LogSection BuildHeldUpSection(HeldUpTargets heldUp, PluginName target, TimeSpan elapsed, ReportContext context) =>
        new("held-up", PartLines(
            heldUp.Outcome,
            HeldUpName,
            heldUp.Targets.Count > 0 && context.Detailed,
            () => $"Objects of {target} that other mods' objects rest on, kept: {TextFormat.Count(heldUp.Targets.Count)} "
                + $"({TextFormat.Count(heldUp.Targets.Values.Count(resting => resting.KeptFor is null))} directly, "
                + $"{TextFormat.Count(heldUp.Targets.Values.Count(resting => resting.KeptFor is not null))} under those){Describe.TimedSuffix(elapsed, context)}.",
            elapsed,
            context));

    /// <summary>
    /// The not-performed line when an unexpected error stopped the step; otherwise the objects of the cleaned mod its rounds kept,
    /// and the detailed log's count and time when there is an object, with the objects of the cleaned mod removed with them when
    /// there is one, or its step-time line when there is none. The outcomes print at the end of the log.
    /// </summary>
    /// <param name="decisions">The run's decisions with the step's rounds applied.</param>
    public static LogSection BuildLeftWithoutSupportStepSection(
        CollectedObjects world, OtherModsObjectsResult settled, IRemovalDecisions decisions, PluginName target, TimeSpan elapsed, ReportContext context) =>
        new("left-without-support-step",
        [
            .. BuildKeptLines(world, settled.Rounds.SelectMany(round => RemovalList.KeptIn(decisions, round))),
            .. PartLines(
                settled.Outcome,
                LeftWithoutSupportName,
                !settled.Objects.IsEmpty && context.Detailed,
                () => $"{LeftWithoutSupportName}: {TextFormat.Count(settled.Objects.Length)} found{RemovedWithThem(settled, decisions, target)}{Describe.TimedSuffix(elapsed, context)}.",
                elapsed,
                context),
        ]);

    /// <summary>The removals of the step's rounds the rule itself decided; the linked group members removed with them are not counted.</summary>
    private static string RemovedWithThem(OtherModsObjectsResult settled, IRemovalDecisions decisions, PluginName target)
    {
        var removed = settled.Rounds.Sum(round => decisions.RemovedIn(round).Count(removal => decisions.Of(removal)!.Reason is not RemovalReason.LinkedTo));
        return removed > 0 ? $"; objects of {target} removed with them: {TextFormat.Count(removed)}" : string.Empty;
    }

    /// <summary>
    /// The not-performed line when an unexpected error stopped the part; otherwise its line, which carries the part's time, when it is
    /// shown, and the step-time line when it is not.
    /// </summary>
    private static ImmutableArray<string> PartLines(
        StepOutcome outcome, string partName, bool shown, Func<string> line, TimeSpan elapsed, ReportContext context) => outcome switch
    {
        StepOutcome.StoppedByUnexpectedError => [BuildNotPerformedLine(partName)],
        StepOutcome.Performed when shown => [line()],
        StepOutcome.Performed => StepTimeLines(partName, elapsed, context),
        StepOutcome.SwitchedOff => [],
        _ => throw new UnreachableException($"Unknown step outcome {outcome}."),
    };

    /// <summary>What became of the other mods' objects left without support, counted by fate; the detailed log lists each. Nothing when there are none.</summary>
    public static LogSection BuildLeftWithoutSupportSection(CollectedObjects world, OtherModsObjectsResult settled, ReportContext context)
    {
        if (settled.Objects.IsEmpty) return new LogSection("left-without-support", []);
        var counts = Enum.GetValues<OtherModsObjectFate>()
            .Select(fate => (Fate: fate, Count: settled.Objects.Count(outcome => outcome.Fate == fate)))
            .Where(entry => entry.Count > 0)
            .Select(entry => $"{OtherModsObjectText.Summary(entry.Fate)}: {TextFormat.Count(entry.Count)}");
        return new LogSection(
            "left-without-support",
            [
                $"{LeftWithoutSupportName}: {TextFormat.Count(settled.Objects.Length)}; {string.Join("; ", counts)}.",
                .. context.Detailed ? settled.Objects.Select(outcome => LeftWithoutSupportLine(world, outcome, context)) : [],
            ]);
    }

    private static string LeftWithoutSupportLine(CollectedObjects world, OtherModsObjectOutcome outcome, ReportContext context)
    {
        var other = outcome.Object;
        return $"  {Describe.OtherObject(other)} (base {RecordNames.DescribeBase(context.Bases, other.Base)}) in {Describe.Space(world, other.SpaceKey)} "
            + $"at {DescribePosition(other)}: {OtherModsObjectText.Outcome(outcome.Fate)}{FateDetail(outcome)}; "
            + $"{TextFormat.Percent(outcome.LostShare)} of its support gone.";
    }

    private static string FateDetail(OtherModsObjectOutcome outcome) => outcome.Fate switch
    {
        OtherModsObjectFate.PulledDown => $" by {TextFormat.Fixed(outcome.Drop, 1)} units",
        OtherModsObjectFate.LeftNotRemovable or OtherModsObjectFate.LeftNothingBelowNotRemovable => $" ({OtherModsObjectText.Roles(outcome.Blocking)})",
        OtherModsObjectFate.LeftWonAfterPatch => $" ({outcome.Object.WinningMod} loads after the patch)",
        _ => string.Empty,
    };

    private static string DescribePosition(OtherObject other) =>
        $"({TextFormat.Fixed(other.Position.X, 0)}, {TextFormat.Fixed(other.Position.Y, 0)}, {TextFormat.Fixed(other.Position.Z, 0)})";
}
